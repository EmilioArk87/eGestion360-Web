# ADR-030 · Auditoría de seguridad y de plataforma (F1.8)

- **Estado:** Aceptada por Emilio el 2026-11-18 (decisiones D70 a D76 del plan de la etapa F1.8, sin cambios sobre lo recomendado). F1.8a (tablas, servicio y eventos de entrada) en curso.
- **Fecha:** 2026-11-18
- **Decidió:** Emilio (D70 a D76). Diseño y ejecución de Claude
- **Fase:** F1.8
- **Desarrolla:** [ADR-001](ADR-001-aislamiento-multitenant.md) (acceso de soporte con motivo y bitácora), [ADR-015](ADR-015-predicados-rls-solo-por-tenant.md) (RLS solo por tenant) y [ADR-025](ADR-025-tenant-de-soporte.md) (soporte del operador)

## Contexto

Hoy el sistema solo guarda quién cambió los datos de una persona (`bitacora_cambios`, 10 entidades, por campo, inmutable). Una entrada, un fallo de clave, un bloqueo, un cambio de permisos de un rol, un usuario creado o un operador que entra a un tenant solo dejan una línea en el log de consola, que en Somee probablemente no se conserva. `saas.cuenta_intentos` cuenta fallos para bloquear una cuenta, pero no es un historial. La bitácora de cambios tampoco guarda quién fue (la cuenta) ni con qué petición.

## Decisión

| # | Decisión | Cómo queda |
|---|----------|------------|
| D70 | **Dos tablas**: una de tenant y una de plataforma | `dbo.auditoria_seguridad` (tenant: `id_tenant` obligatorio, RLS, inmutable) para lo que ocurre dentro de un tenant; `saas.auditoria_plataforma` (global: sin RLS, inmutable) para lo de las cuentas y del operador. Un evento del operador sobre un tenant (soporte) va a las dos, para que el tenant lo vea |
| D71 | **Catálogo de eventos** `grupo.accion`; los intentos con una cuenta que no existe se agregan | F1.8a registra: `sesion.entrada`, `sesion.salida`, `sesion.bloqueo`, `mfa.verificada`, `mfa.alta`, `mfa.recuperacion`, `mfa.desactivada`, `clave.cambiada`, `clave.restablecida`, `invitacion.aceptada` y `contexto.elegido`. F1.8b agrega los de administración y soporte. Un fallo con una cuenta inexistente deja **una fila por dirección y por hora**, con el correo enmascarado y la cuenta de intentos omitidos en el detalle |
| D72 | **Qué lleva cada fila y qué nunca** | Lleva fecha UTC, tipo, resultado, cuenta, nombre, si es operador, tenant, empresa, objeto, detalle JSON, dirección completa, agente recortado a 200 caracteres e id de correlación. Nunca lleva contraseñas ni su hash, códigos de segundo factor o de recuperación, tokens de invitación, cadenas de conexión ni documentos completos |
| D73 | **Si falla la auditoría** | Los eventos de entrada se intentan (en un contexto aparte, con su propia conexión) y, si fallan, se anota en el log y la persona entra igual. Los cambios de seguridad (F1.8b) se auditan en la misma transacción: si no se puede dejar la fila, el cambio no se hace |
| D74 | **Pantallas y permisos** | F1.8c: «Auditoría» del administrador de la empresa (`administracion.auditoria.ver`, solo su tenant) y «Auditoría de la plataforma» del operador (`plataforma.auditoria.ver`), de solo lectura. Los permisos entran por el script 046 |
| D75 | **Conservación** | 24 meses, sin purga automática. Pasado ese tiempo la purga es un script con aprobación que levanta la inmutabilidad solo dentro de la purga; F1.8 documenta el procedimiento y no lo programa |
| D76 | **Cobertura vigilada por una prueba** | F1.8b: una prueba de arquitectura exige que toda pantalla de administración o plataforma que escribe deje una fila o esté en una lista revisada de excepciones |

## F1.8a: las tablas

Las dos tablas tienen las mismas columnas y se diferencian en `id_tenant` (obligatorio en la de tenant, nulo en la de plataforma, donde significa «el tenant sobre el que se actuó» y puede faltar):

| Columna | Tipo | Qué guarda |
|---|---|---|
| `id_evento` | `BIGINT` identidad | Clave; crece en orden |
| `fecha_hora` | `DATETIME2(3)` UTC | Cuándo ocurrió (el reloj del servicio; la columna también tiene `SYSUTCDATETIME()` por omisión, para quien inserte sin fecha) |
| `tipo` | `VARCHAR(60)` | `grupo.accion` |
| `resultado` | `VARCHAR(10)` | `ok`, `fallo` o `denegado` (CHECK) |
| `id_cuenta` | `INT` nulo | Cuenta global de quien lo hizo; nulo si no se sabe (cuenta inexistente). Sin clave foránea: el registro no puede bloquear ni ser afectado por otra tabla |
| `usuario` | `NVARCHAR(100)` nulo | Con qué nombre entró o se intentó entrar (enmascarado si la cuenta no existe) |
| `es_operador` | `BIT` | Si la cuenta es del personal de la plataforma |
| `id_tenant` | `INT` | Tenant; clave foránea a `saas.tenants` |
| `id_empresa` | `INT` nulo | Empresa, si la hay |
| `objeto_tipo`, `objeto_id` | `VARCHAR(40)`, `NVARCHAR(60)` nulos | Sobre qué (por ejemplo `invitacion` y su id) |
| `detalle` | `NVARCHAR(2000)` nulo | JSON breve (CHECK `ISJSON`), sin secretos |
| `ip`, `agente` | `VARCHAR(45)`, `NVARCHAR(200)` nulos | Dirección de red y agente |
| `id_correlacion` | `VARCHAR(64)` | `HttpContext.TraceIdentifier` (el mismo valor de `X-Correlation-Id` y de los logs) |

- Un disparador `INSTEAD OF UPDATE, DELETE` rechaza cualquier modificación o borrado, igual que `TR_bitacora_cambios_inmutable`. EF lo declara (`HasTrigger`) para no usar `OUTPUT` directo.
- `dbo.auditoria_seguridad` entra a `seg.pol_tenant` con las mismas funciones que las demás tablas de tenant (1 filtro y 4 bloqueos); `saas.auditoria_plataforma` no (plano de control, ADR-013).
- `dbo.bitacora_cambios` recibe dos columnas nulas, `id_cuenta` e `id_correlacion`, que el interceptor de la bitácora llena desde ahora. Las filas anteriores quedan sin ellas.
- Script **045** (solo agrega; PRECHECK, CAMBIO en una transacción, POSTCHECK y ROLLBACK). El código viejo ignora todo lo nuevo, así que se aplica antes de publicar.

## F1.8a: el servicio

`IAuditoriaDeSeguridad.RegistrarAsync(evento)`:

1. **Contexto de la petición**: cuenta, nombre y si es operador salen de la cookie; la dirección, el agente y el id de correlación, de `HttpContext`. Cuando el evento ocurre **antes** de que la cookie lleve el contexto nuevo (la entrada y el cambio de sitio), el evento trae su tenant y su empresa explícitos.
2. **Destino** según el catálogo: plataforma, tenant, o las dos. `contexto.elegido` va al tenant elegido, o a la plataforma si el operador entra sin empresa.
3. **Escritura aparte**: abre un ámbito de servicios nuevo (`IServiceScopeFactory`, igual que el validador de identidad), fija en él el tenant del evento y guarda con su propio `ApplicationDbContext` y su propia conexión. Así RLS comprueba el tenant del evento (que puede ser distinto del de la petición), un fallo de la auditoría no deja entidades a medias en el contexto de la petición y la auditoría no guarda por accidente otros cambios pendientes.
4. **Mejor esfuerzo** (D73): captura cualquier excepción menos la cancelación, la anota en el log y sigue.
5. **Agregación** (D71): los fallos con cuenta inexistente guardan una fila por dirección y hora; el servicio lleva en memoria cuántos omitió y lo anota en el detalle de la siguiente. Un tope por hora protege la tabla de un ataque desde muchas direcciones.

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| 1 | Una sola tabla en `saas` sin RLS | El tenant vería su auditoría solo por un filtro de código, sin la segunda barrera de las demás tablas de tenant |
| 2 | Escribir con el `ApplicationDbContext` de la petición | Un `SaveChanges` de auditoría guardaría cambios pendientes que no le tocan, un fallo dejaría entidades inválidas rastreadas y RLS rechazaría el evento de un tenant distinto del de la petición |
| 3 | Conexión `SqlConnection` directa | Evita EF, pero obliga a copiar a mano el contexto de sesión de RLS y a ampliar la lista de conexiones directas revisadas |
| 4 | Guardar cada intento con cuenta inexistente | Un ataque anónimo haría crecer la tabla sin límite |

## Consecuencias

- El 045 agrega una tabla de tenant: los verificadores de la comprobación diaria aceptan las cifras con y sin el 045 (+5 predicados, +1 tabla, +1 clave foránea hacia `saas.tenants`), como hicieron con el 041 y el 042.
- La dirección de red es la de la conexión directa (Somee). Detrás de un proxy (Azure) habrá que configurar los encabezados reenviados en el paso de migración.
- Las dos tablas son datos personales (dirección, nombre) y no se pueden borrar: la regla de conservación (D75) y la lista de lo que nunca se guarda (D72) son parte del diseño.
- Si algo falla: el bloque ROLLBACK del 045 quita las dos tablas, sus predicados y las dos columnas de la bitácora, y se detiene si alguna tabla de auditoría tiene filas (no se descarta una auditoría sin una decisión expresa).
