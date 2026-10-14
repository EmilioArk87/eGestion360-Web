# ADR-023 · Política de Row-Level Security por tenant: directorio de empresas abierto, plataforma sin lectura de tenant y planes (D35 a D37)

- **Estado:** Propuesta (implementada en F1.4a; pendiente de confirmación de Emilio)
- **Fecha:** 2026-10-13
- **Decidió:** Claude, al implementar F1.4a del plan de la Fase 1; Emilio las confirma o las cambia
- **Fase:** F1
- **Completa:** [ADR-015](ADR-015-predicados-rls-solo-por-tenant.md) (predicados solo por tenant) y
  [ADR-001](ADR-001-aislamiento-multitenant.md) (capa 3: la base como segunda barrera); se apoya en el piloto de F0.5.

## Contexto

F1.2 y F1.3 dejaron `id_tenant` obligatorio en las tablas de tenant, claves foráneas compuestas y filtros globales en EF. Falta la
barrera que no depende de la aplicación: que la propia base entregue a cada conexión solo las filas de su tenant, aunque una
consulta nueva olvide el filtro o llegue por SQL directo. F1.4 se parte en tres: **4a** los scripts de la política y sus pruebas
(esta decisión), **4b** el interceptor que fija el contexto al abrir cada conexión y los procesos de fondo, **4c** el «tenant de
soporte» para las pantallas del administrador general.

Al armar la política sobre las 60 tablas reales aparecieron tres puntos que el piloto (datos inventados) no tocaba:

1. El inicio de sesión y el middleware resuelven el tenant **leyendo `empresas`**, es decir, antes de tener tenant en el contexto.
2. La plataforma (administrador general) hoy lista datos de cualquier tenant desde las mismas conexiones transaccionales.
3. Con la política encendida, las consultas que no usan un índice con `id_tenant` pueden cambiar de plan.

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| D35 | `empresas` queda con **lectura abierta** y escritura aislada por tenant | La política lleva en `empresas` solo los 4 bloqueos (`AFTER INSERT`, `AFTER UPDATE`, `BEFORE UPDATE`, `BEFORE DELETE`), sin filtro: es el directorio de cuentas que el inicio de sesión, el middleware y la plataforma leen para saber a qué tenant pertenece una empresa. Una conexión no puede crear, mover ni borrar empresas de otro tenant. |
| D36 | La plataforma **no lee datos de tenant** por la conexión transaccional: lo hace con un «tenant de soporte» | El ámbito `plataforma` del contexto de sesión no ensancha el predicado de las 37 tablas de tenant (ADR-015): solo permite escribir filas **globales** (`id_tenant` nulo) de las 2 tablas mixtas (`tasas_cambio`, `sen_boletines`). Una pantalla del administrador general que muestre datos de un tenant debe fijar ese tenant en la sesión (F1.4c). `domain_events` queda sin política: solo el despachador la lee y fija el contexto de cada evento (D20). |
| D37 | No se reescriben índices en F1.4a; el cambio de plan se tolera mientras la tabla sea pequeña | Con la política encendida, `vehiculos` (236 filas) pasó de Index Seek a Clustered Index Scan porque su índice no incluye `id_tenant`. Se acepta en tablas de menos de unas 1.000 filas; cuando una tabla crezca más, se agrega `id_tenant` como columna incluida o como primera columna del índice (es un script aparte, con `/alerta-bd`). |

### Cómo queda la política

| Elemento | Detalle |
|---|---|
| Funciones (esquema `seg`) | `fn_tenant_actual(@id_tenant)`: permite la fila si coincide con `SESSION_CONTEXT('id_tenant')`. `fn_mixta_lectura`: lo anterior o `id_tenant` nulo. `fn_mixta_escritura`: coincide con el tenant, o `id_tenant` nulo **y** ámbito `plataforma`. Las tres con `SCHEMABINDING`. |
| Política `seg.pol_tenant` | Una sola política con 199 predicados: 37 tablas de tenant × (1 FILTER + 4 BLOCK) + 2 mixtas × 5 + `empresas` × 4. Se crea en `STATE = OFF` (script 036) y se enciende aparte (script 037). |
| Contexto | `id_tenant` y `ambito` (`tenant` o `plataforma`), fijados con `sp_set_session_context ... @read_only = 1` al abrir la conexión (F1.4b): la aplicación no puede cambiarlos después. Sin contexto, una tabla de tenant no devuelve filas. |
| Apagado de emergencia | `emergencia_rls_apagar.sql`: una sentencia, idempotente, sin PRECHECK; no pasa por `/alerta-bd` porque quita protección en vez de cambiar datos. |

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| D35 | Filtrar también `empresas` por tenant y resolver el tenant con un procedimiento almacenado con `EXECUTE AS` | Agrega una pieza más al camino del inicio de sesión, el más delicado: si falla, nadie entra. El directorio solo contiene datos de cuenta (razón social, tenant, estado), no datos operativos. |
| D35 | Dejar `empresas` sin política | Cualquier conexión podría mover una empresa a otro tenant; los bloqueos de escritura cuestan poco. |
| D36 | Ámbito `plataforma` que lee todos los tenants | Es lo que ADR-015 descartó: un error de contexto o una inyección en una pantalla de plataforma abriría todos los datos. |
| D36 | Una conexión distinta con un inicio de sesión de base propio para la plataforma | Es la consola de operador de F1.9; hoy no hay una segunda cadena de conexión. |
| D37 | Reescribir ya los índices para que empiecen por `id_tenant` | Cambia 40 tablas por un efecto que hoy no se mide (tablas de cientos de filas) y obliga a rehacer los índices por empresa (ADR-021, D31). |

## Consecuencias

- Con el 036 aplicado y la política apagada **no cambia nada**; es seguro aplicarlo antes de publicar el código de F1.4b.
- El 037 **no puede aplicarse** hasta publicar la versión que fija el contexto (F1.4b y F1.4c): encendida, toda pantalla sin tenant
  en la sesión quedaría vacía. Antes se ensaya sobre una copia con la política encendida (recorrido de la aplicación).
- Las pantallas del administrador general que hoy listan datos de tenants (por ejemplo la edición de los permisos de un rol)
  necesitarán el tenant de soporte (F1.4c). `Empresas` sigue listando porque el directorio es abierto.
- Una tabla de tenant nueva hace fallar `ScriptsRlsTests` hasta que entre en la política (y en el script 036 generado).
- Riesgo aceptado (D35): un error de aplicación podría **leer** el directorio de empresas de otros tenants; no puede escribirlo ni
  leer ninguna tabla de tenant.

## Cómo se verifica

- `ScriptsRlsTests`: la política cubre exactamente las tablas que clasifica el arnés, 1 FILTER (salvo `empresas`) y 4 BLOCK por
  tabla con la función correcta, 199 predicados, el 036 la crea apagada, el 037 la enciende y el de emergencia la apaga.
- `RlsRealSqlServerTests` (LocalDB, esquema de EF + tablas mínimas): por SQL directo y tabla por tabla, cada tenant ve solo lo suyo,
  sin contexto no se ve nada de tenant, las mixtas dejan ver las globales, `empresas` se lee abierta y se escribe aislada, no se
  puede mover una fila a otro tenant ni cambiar el contexto de la sesión.
- Ensayo sobre la copia de eBD_SPD (LocalDB): política apagada sin efecto, encendida aísla las 37 tablas, apagado de emergencia,
  reaplicar, ROLLBACK y los cortes de los PRECHECK; los planes de las consultas comunes se comparan con y sin política (D37).
