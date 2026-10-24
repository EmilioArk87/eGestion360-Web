# ADR-026 · Derechos de uso y estado del tenant en cada petición (D53 a D55)

- **Estado:** Aceptada en lo decidido (D53 a D55, Emilio, 2026-10-15); los detalles de implementación de abajo los propuso Claude al hacer el código de F1.7 y quedan por confirmar al publicarlo
- **Fecha:** 2026-10-18
- **Decidió:** Emilio (D53 a D55); Claude (detalles de implementación)
- **Fase:** F1.7
- **Completa:** [ADR-010](ADR-010-orden-contabilidad-y-cobro-saas.md) y [ADR-013](ADR-013-esquemas-sql.md): ahí se decide
  que el código pregunta por derechos y nunca por planes; aquí se decide cómo se aplican en cada petición. Las tablas las crea el
  script 040.

## Contexto

Hasta F1.6 el acceso a un módulo salía de una copia que el inicio de sesión dejaba en la sesión (`Modulos`): suspender un tenant,
quitarle un módulo o cambiarle el plan no surtía efecto hasta que cada usuario volviera a entrar, y nada impedía que un tenant
suspendido siguiera guardando datos. El script 040 crea los planes, sus versiones, los derechos de uso, el plan vigente de cada
tenant y el modo de suspensión (`saas.tenants.modo_suspension`).

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| D53 | Suspender un tenant lo deja **en solo lectura** por defecto; el **bloqueo** total es un modo aparte | `modo_suspension` vale `lectura` (por defecto) o `bloqueo`. Cancelado bloquea siempre. En lectura se puede consultar (GET, HEAD, OPTIONS) y todo lo demás vuelve a `/SinAcceso`; en bloqueo ni siquiera se consulta. Iniciar y cerrar sesión, `/SinAcceso` y `/Error` quedan siempre disponibles para que el usuario pueda ver por qué no entra. |
| D54 | Solo se aplican dos límites: `limite.usuarios` y `limite.empresas` | Crear una empresa o un usuario **activo** comprueba el tope del plan del tenant (sin tope = sin límite). El resto de límites se define cuando haya cobro. |
| D55 | Lo leído se recuerda **30 segundos** | Suspender, reactivar o cambiar un plan se nota en 30 s como máximo sin tocar la base en cada petición; `IDerechosDeUso.Invalidar` lo olvida antes (lo usará la consola del operador, F1.9). |
| — | Tener un módulo = **el plan lo concede Y la empresa lo tiene activo y vigente** | `DerechosDeLaPeticion.TieneModulo`. La activación por empresa (`empresa_modulos`) queda dentro de lo que el plan permite; el día del vencimiento todavía vale. `AuthHelper.HasModulo` lee esto de `HttpContext.Items` y usa la copia de la sesión solo si la petición no pasó por el middleware (pruebas, procesos sin petición web). |
| — | `EstadoDelTenantMiddleware` va justo después de `TenantMiddleware` | Carga el estado y los derechos del tenant una vez por petición, rechaza lo que corresponda (redirección a `/SinAcceso`, o `403` directo si la llamada pide JSON o es `XMLHttpRequest`) y registra cada rechazo en el log con tenant, estado y ruta. Sin tenant en la petición no hace nada. Un tenant sin plan vigente no se bloquea, pero no concede ningún derecho y queda en el log. |
| — | El **administrador general nunca se bloquea** | Entra a un tenant suspendido o cancelado (por el tenant de soporte, ADR-025) para atenderlo. |
| — | `DerechoDeModuloPageFilter` exige el derecho por carpeta | `/Flota/`, `/Facturacion/`, `/Catalogos/` y `/Contabilidad/` responden `403` si el tenant no tiene el módulo (excepción: `/Flota/SinEmpresa`). Una prueba falla si una pantalla exige un módulo cuya carpeta no está en la lista. |

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| D53 | Suspensión siempre total | Un cliente moroso perdería hasta la consulta de sus propios datos; el bloqueo queda disponible como modo. |
| D55 | Leer la base en cada petición | Cuatro consultas más por petición para un dato que cambia pocas veces al mes. |
| D55 | Caché de varios minutos | Una suspensión urgente tardaría demasiado en notarse. |
| — | Guardar la decisión en la sesión del usuario | Es la copia vieja que se quiere dejar: no refleja un cambio hasta el próximo inicio de sesión. |
| — | Una guarda manual en cada pantalla | Son decenas y crecen; el filtro por carpeta cubre también las que se agreguen. |

## Consecuencias

- Una petición que no es de lectura de un tenant suspendido no llega a la pantalla: no hay riesgo de guardado parcial.
- Un cambio de plan o de estado hecho directamente en la base se nota en 30 s como máximo (hasta F1.9 no hay otra forma de cambiarlos).
- Cuatro consultas por tenant y por empresa cada 30 s (estado, plan, derechos, módulos activos); en las demás peticiones, ninguna.
- Si un dato mal sembrado dejara a alguien sin acceso, `2 - Script SQL/emergencia_derechos_abrir.sql` lo abre (solo si Emilio lo pide).
- El inicio de sesión sigue escribiendo la copia de módulos en la sesión; se retirará con la reescritura del inicio de sesión de F1.5.

## Cómo se verifica

- `Services/Derechos/DerechosDeUsoTests`: plan, derechos y topes del tenant; estado y modo; módulos activos y vencidos; memoria de 30 s e
  invalidación; límites de empresas y usuarios.
- `Services/Derechos/EstadoYAccesoPorModuloTests`: la tabla completa de quién se bloquea; el middleware en una petición (redirección,
  403 para JSON, operador, sin tenant, sin plan); el filtro por carpeta; `AuthHelper.HasModulo`; que toda carpeta de módulo esté en el
  filtro y que `Program.cs` registre el servicio, el filtro y el middleware en el orden correcto.
- Recorrido de la aplicación sobre una copia con 025 a 037 y 040 y la política encendida (2026-10-18): tenant activo (Flota abre; Catálogos y
  Facturación, 403); suspendido en lectura (consulta bien, el envío de un formulario vuelve a `/SinAcceso`, una llamada JSON recibe 403);
  en bloqueo (entra al inicio de sesión y ve la explicación, nada más); sin el derecho `modulo.flota` (las pantallas de Flota, 403, y el
  menú ya no lo ofrece); límites en `Admin/Usuarios/Create` y `Empresas/Create`; el administrador general entra a un tenant en
  bloqueo; `/Admin/Soporte` muestra plan y modo.
