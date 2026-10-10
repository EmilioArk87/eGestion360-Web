# ADR-015 · Predicados de RLS solo por tenant; la plataforma no lee datos de tenants

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D15 del plan)
- **Fase:** resultado de la prueba de concepto F0.5; se aplica en F1
- **Modifica:** [ADR-001](ADR-001-aislamiento-multitenant.md), en el punto «los reportes entre tenants usan un usuario
  SQL de plataforma distinto, de solo lectura y auditado».

## Contexto

La prueba de concepto de Row-Level Security (F0.5) corrió sobre SQL Server 2022 LocalDB 16.0.1000.6, la misma versión
que eBD_SPD, con 24 tenants ficticios. Está automatizada en `eGestion360Web.Tests/SqlServer/Rls/` (15 pruebas). Se
confirmó todo lo que pide ADR-001 y se encontró un costo que ADR-001 no preveía.

**Confirmado:**

| Caso | Resultado |
|---|---|
| Sin contexto de tenant | No se ve ni se escribe ninguna fila de tenant; solo las filas globales de las tablas mixtas. |
| Consulta que ignora los filtros de EF (`IgnoreQueryFilters`) | La base entrega solo las filas del tenant de la sesión. |
| Insertar en otro tenant o mover una fila a otro tenant | La base lo rechaza (error 33504). |
| Modificar o borrar filas de otro tenant | No las alcanza: 0 filas afectadas. |
| Interceptor de EF | Asigna el tenant a las filas nuevas y rechaza cambiarlo antes de llegar a la base. |
| Clave foránea compuesta `(id_tenant, id_cliente)` | Impide que una factura apunte al cliente de otro tenant (error 547). |
| Contexto fijado con `@read_only = 1` | No se puede cambiar dentro de la conexión, ni el tenant ni el ámbito. |
| Pool de conexiones | Al reutilizar la misma sesión física, el contexto anterior llega limpio y se puede fijar otro tenant. |
| Job que recorre tenants | Cada tenant, con su propio contexto, ve solo sus filas; los totales cuadran. |
| Usuario con permisos de aplicación (lectura y escritura de datos) | No puede apagar ni borrar la política. |

**Costo encontrado.** Se midió el plan de ejecución de `SELECT COUNT(*), SUM(total)` de un tenant sobre una tabla de
100 000 filas de 200 tenants, con índice `(id_tenant, id_cliente)`:

| Predicado de lectura | Operador | Filas leídas para devolver 500 |
|---|---|---|
| `id_tenant = contexto` | Index Seek | 500 |
| `id_tenant = contexto OR ámbito = 'plataforma'` | Index Scan | 100 000 |
| `id_tenant = contexto UNION ALL (ámbito = 'plataforma')` | Index Scan | 100 000 |
| `id_tenant = contexto OR usuario = usuario de plataforma` | Index Scan | 100 000 |
| Tabla mixta: `id_tenant IS NULL OR id_tenant = contexto` | Index Seek (dos rangos) | 1 000 (500 globales + 500 propias) |

Cualquier excepción para que «la plataforma vea todo» convierte cada consulta de cada tenant en un recorrido de las filas
de **todos** los tenants. El costo crece con cada cliente nuevo.

## Decisión

1. **Tablas de tenant:** un solo predicado, `seg.fn_tenant_actual`, que compara `id_tenant` con el contexto de sesión.
   Se usa como filtro y en los cuatro bloqueos: `AFTER INSERT`, `AFTER UPDATE`, `BEFORE UPDATE` y `BEFORE DELETE`. Sin
   excepciones para la plataforma ni para ningún usuario.
2. **Tablas mixtas** (fila global con `id_tenant` nulo más filas propias, como `tasas_cambio`): lectura
   `id_tenant IS NULL OR id_tenant = contexto`; escritura de filas propias por su tenant y de filas globales solo en el
   ámbito de plataforma. Los cuatro bloqueos son obligatorios: sin `BEFORE UPDATE` y `BEFORE DELETE`, un tenant podría
   modificar, borrar o adueñarse de una fila global, porque la ve.
3. **Contexto de sesión:** al abrir cada conexión la aplicación fija dos claves con `@read_only = 1`:
   - `id_tenant`, que es nulo en el ámbito de plataforma;
   - `ambito`, con el valor `tenant` o `plataforma`.
4. **La plataforma no lee datos de tenants por la base transaccional.** Sus necesidades se cubren así:
   - **Jobs por tenant** (outbox, cobros, cierres): una conexión con el contexto de cada tenant.
   - **Soporte:** acceso temporal con el contexto del tenant, con motivo y bitácora (ADR-001).
   - **Métricas de la plataforma** (usuarios, documentos, almacenamiento por tenant): contadores en el esquema `saas`,
     que no lleva RLS de tenant, alimentados por eventos.
   - **Analítica entre tenants** (F9): una copia de lectura o un proceso de extracción fuera de la base transaccional.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Excepción por ámbito de plataforma en el predicado | Index Scan en todas las consultas de todos los tenants (medido). |
| Excepción por usuario SQL de plataforma (lo que decía ADR-001) | Mismo recorrido completo (medido). Además Somee da un solo login, así que el usuario de plataforma tendría que obtenerse con `EXECUTE AS`, que también podría usar un SQL inyectado. |
| `UNION ALL` dentro del predicado | Mismo recorrido completo (medido). |
| Dos políticas que se activan según el caso | SQL Server admite un solo predicado de filtro por tabla, y activar o desactivar una política afecta a todas las sesiones a la vez. |

## Consecuencias

- Las consultas de un tenant leen solo sus filas, sin importar cuántos tenants haya.
- Los reportes de la plataforma no pueden hacer `SELECT` sobre tablas de tenant mezclando tenants; necesitan contadores
  en `saas` o, más adelante, una copia de lectura.
- **Límite que no resuelve RLS:** en Somee la aplicación entra con un único login que es `db_owner`, y `db_owner` puede
  apagar la política. RLS protege contra errores de la aplicación (una consulta sin filtro, un id equivocado), no contra
  un SQL inyectado con permisos de dueño. Mientras tanto se mantienen las consultas parametrizadas, que la aplicación ya
  usa con EF. En el hosting nuevo ([ADR-007](ADR-007-hosting.md)) la aplicación entrará con un login sin permisos para
  cambiar el esquema ni la política, y las migraciones con otro login. La prueba demuestra que un usuario con solo
  permisos de datos no puede apagarla.

## Cómo se verifica

- `RlsPocTests.La_consulta_del_tenant_usa_el_indice_que_empieza_por_id_tenant` falla si el plan deja de buscar por el
  índice o recorre la tabla.
- En F1, la misma comprobación se repite sobre las tablas reales con más volumen.
