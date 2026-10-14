# ADR-024 · Contexto de sesión por conexión, tenant del inicio de sesión y procesos de fondo (D38 a D40)

- **Estado:** Aceptada (implementada en F1.4b; confirmada por Emilio el 2026-10-14)
- **Fecha:** 2026-10-13
- **Decidió:** Claude las propuso al implementar F1.4b del plan de la Fase 1; Emilio las aceptó el 2026-10-14
- **Fase:** F1
- **Completa:** [ADR-023](ADR-023-politica-rls-por-tenant.md) (la política RLS, F1.4a) y
  [ADR-015](ADR-015-predicados-rls-solo-por-tenant.md); usa `ITenantContext` de [ADR-001](ADR-001-aislamiento-multitenant.md).

## Contexto

La política `seg.pol_tenant` (scripts 036 y 037) solo sirve si cada conexión de la aplicación lleva en `SESSION_CONTEXT` el tenant
y el ámbito para el que trabaja. F1.4b conecta la aplicación con esa política, sin encenderla: con la política apagada nada cambia.
Al revisar el código aparecieron cuatro puntos: EF abre y cierra la conexión en cada consulta; el inicio de sesión lee la persona,
los módulos y los permisos del usuario antes de que la sesión tenga empresa; un resolvedor y dos procesos de fondo abren sus propios
ámbitos de servicios; y había código que abría la conexión sin pasar por EF (`SenCombustibleService`, que además nadie registra:
ni él ni `KpiSyncBackgroundService` corren hoy).

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| D38 | Un interceptor de conexión de EF fija el contexto de sesión en cada apertura y vigila que no quede viejo | `ContextoDeSesionInterceptor` ejecuta `sp_set_session_context` con `id_tenant` y `ambito` (`tenant` o `plataforma`) y `@read_only = 1` cada vez que EF abre una conexión, con lo que diga `ITenantContext` en ese momento. Antes de cada comando comprueba que el contexto con el que se abrió la conexión sea el de ahora: si el tenant cambió con la conexión abierta (transacción, apertura explícita) o si alguien la abrió sin pasar por EF, el comando falla en lugar de correr con el tenant equivocado. |
| D39 | El inicio de sesión fija el tenant del usuario después de validar la clave | La sesión aún no tiene empresa, así que `LoginModel` lo resuelve: el de su empresa (`ResolverPorEmpresaAsync`) o, si el usuario no tiene empresa (personal de la plataforma), el tenant interno (`saas.tenants.tipo = 'interno'`, ADR-006) con `ResolverInternoAsync`. Solo entonces lee la persona, los módulos y los permisos. Las páginas sin sesión que solo tocan `Users` y `PasswordResetCodes` (tablas de plataforma, sin política) no cambian. |
| D40 | Todo proceso con ámbito de servicios propio declara su tenant, y nadie abre la conexión a mano | El job de la tasa oficial corre en ámbito de plataforma (`FijarPlataforma`); el despachador del outbox ya fijaba el tenant de cada evento. El resolvedor hace que la consulta de una persona, que corre en un ámbito aparte, use el mismo tenant de quien la pide. `SenCombustibleService` abre su conexión con `Database.OpenConnectionAsync` y en plataforma. Tres pruebas de código fuente fijan la regla: ninguna `new SqlConnection` salvo el candado de `BloqueoJob`, `GetDbConnection()` solo donde se abre por EF, y cada `CreateScope` declara `Fijar` o `FijarPlataforma` (con tres excepciones documentadas). |

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| D38 | Fijar el contexto una sola vez por petición, con una conexión que EF mantenga abierta | El tenant puede cambiar dentro de la petición (el inicio de sesión lo averigua a medias) y los procesos de fondo cambian de tenant en el mismo ámbito; con una conexión larga habría que vigilar mucho más. El costo de la apertura por consulta se acepta y se mide (ver Consecuencias). |
| D38 | Anteponer el `sp_set_session_context` al texto de cada comando | Rompe los lotes de EF y los procedimientos, y es más difícil de probar que un evento de conexión. |
| D39 | Que la sesión de un usuario sin empresa lleve siempre el tenant interno | Haría que las pantallas del administrador general vieran los datos de SIP como si fueran de la plataforma; esa decisión es de F1.4c (el «tenant de soporte»). |
| D39 | Una columna `Users.IdTenant` | Es el modelo de identidad de F1.5 (ADR-003 y ADR-008): se hace una vez, con los claims de la cookie. |
| D40 | Corregir o borrar `SenCombustibleService` y `KpiSyncBackgroundService` | Nadie los registra; borrar código ajeno al cambio no corresponde sin que Emilio lo pida. Se dejan correctos y vigilados. |

## Consecuencias

- Con la política apagada, el único efecto es una consulta más por cada apertura de conexión (`sp_set_session_context`). Con la
  aplicación y la base en el mismo centro de datos son décimas de milisegundo; se mide en producción antes de encender la política.
  Si pesara, la salida es mantener la conexión por petición (D38, primera alternativa).
- **Recorrido de la aplicación con la política encendida** (copia de eBD_SPD con 025 a 037, cuatro usuarios, 88 visitas a pantallas): las
  pantallas de los tres usuarios de empresa son idénticas a las de la política apagada; altas y ediciones reales de cargos en dos
  tenants funcionan y cada uno solo ve lo suyo; el cargo de otro tenant responde 404. Dos hallazgos, ambos del administrador
  general, que son el trabajo de F1.4c:
  1. `/Admin/Personas/Index` queda vacía (de 26 a 2 filas): lista las personas de todos los tenants, lo que D36 ya no permite.
  2. Crear una empresa falla (el bloqueo de `empresas` rechaza insertar sin el tenant elegido en el contexto), así que hay que
     fijarlo antes de guardar.
  Por eso el script 037 **no se aplica** hasta que F1.4c esté publicada (la resuelve [ADR-025](ADR-025-tenant-de-soporte.md)).
- Una consulta o un proceso nuevo que cambie de tenant con una conexión o transacción abierta falla con un mensaje que lo explica;
  la solución es cerrar la conexión antes de cambiar de tenant.

## Cómo se verifica

- `SqlServer/Rls/ContextoDeSesionSqlServerTests` (política encendida, esquema de EF): cada contexto de EF ve solo su tenant aun con
  el filtro de EF apagado, en todas las tablas de tenant; sin tenant o en plataforma no se ve ninguna fila de tenant y sí las globales;
  el cambio de tenant con la conexión abierta y la conexión abierta sin EF fallan; el pool no arrastra el tenant anterior.
- `SqlServer/Rls/AplicacionConRlsSqlServerTests`: guardar en una petición asigna el tenant y la base lo acepta; sin el interceptor
  de la aplicación la base rechaza una fila de otro tenant; la tasa global solo se escribe en plataforma; la plataforma no ve datos
  de tenant pero sí el directorio de empresas; el resolvedor; el despachador del outbox con la política encendida.
- `Paginas/LoginTests` (tenant de la empresa, tenant interno y clave equivocada), `Services/TasasCambio/TasasCambioEnPlataformaTests`
  y `Arquitectura/ConexionesDirectasTests`.
