# ADR-020 · Filtro global de tenant en EF y ámbito de plataforma interino (D26 a D28)

- **Estado:** Propuesta (implementada en F1.3a; pendiente de confirmación de Emilio)
- **Fecha:** 2026-10-12
- **Decidió:** Claude, al implementar F1.3 del plan de la Fase 1; Emilio las confirma o las cambia
- **Fase:** F1
- **Completa:** [ADR-001](ADR-001-aislamiento-multitenant.md) (capa 4: EF Core) y
  [ADR-015](ADR-015-predicados-rls-solo-por-tenant.md); usa los filtros con nombre de EF 10 ([ADR-017](ADR-017-dotnet-10.md)).

## Contexto

F1.2 dejó `id_tenant` en las 44 entidades de tenant y un interceptor que lo asigna al guardar. Falta que las **consultas**
se limiten al tenant de la petición sin que cada pantalla lo recuerde (el aislamiento por `id_empresa` manual ya falló
dos veces, ADR-001). Hay pantallas que por naturaleza ven varios tenants: el login (todavía no hay sesión) y las del
administrador general (hoy el operador de la plataforma, D23).

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| D26 | Filtro global con nombre `tenant` en cada entidad de tenant, cerrado por defecto | `ApplicationDbContext` lo aplica cuando recibe un `ITenantContext` (siempre, por inyección). Una petición sin tenant no ve ninguna fila de tenant; una tabla mixta (ADR-015) deja ver además las filas globales (`id_tenant` nulo). Una prueba prohíbe `new ApplicationDbContext(` en el código de la aplicación. |
| D27 | Ámbito de plataforma interino por lista revisada de rutas | `AmbitoDePlataformaPageFilter` marca la petición como plataforma (el filtro no se aplica) en: las pantallas sin sesión (`/Login`, `/ForgotPassword`, `/ResetPassword`, `/Register`) y, solo si la sesión es del administrador general, `/Empresas`, `/Admin`, `/UserManagement` y `/Catalogos/TiposCambio`. Los procesos de fondo lo fijan en su propio ámbito (`TenantContext.FijarPlataforma` / `Fijar`); el despachador del outbox lee la cola en plataforma y corre cada evento con el tenant de su empresa (D20). `IgnoreQueryFilters` queda prohibido en el código de la aplicación (prueba). |
| D28 | F1.3 se entrega en tres partes | **F1.3a** filtro de tenant de EF (esta). **F1.3b** scripts 031 a 035 (NOT NULL, claves únicas `(id_tenant, id)`, claves foráneas compuestas, unicidad por tenant). **F1.3c** filtro de borrado lógico, aparte: no es aislamiento y cambia lo que ven decenas de pantallas. |

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| D26 | Abierto cuando no hay tenant | Un fallo que deje la petición sin tenant mostraría todos los tenants. Cerrado falla visible y seguro. |
| D27 | `IgnoreQueryFilters` en cada consulta de plataforma | Hay decenas de consultas en esas pantallas; una lista de rutas se revisa de una vez y no se olvida una. |
| D27 | Un ámbito de plataforma sin límite en el tiempo | ADR-015: la plataforma no lee datos de tenants. Esto es interino. |
| D28 | Todo F1.3 en un solo incremento | Un script de contraer mal ensayado bloquea la base; el filtro de EF no toca la base. |

## Consecuencias

- Las pantallas de la lista de plataforma siguen viendo todo mientras no llegue F1.4 (RLS sin excepciones) y F1.6 (acceso de
  soporte con motivo): ahí se estrechan y esta lista se encoge hasta desaparecer.
- Un administrador de empresa que entre a las mismas rutas conserva el filtro de su tenant.
- Ninguna pantalla de un tenant escribe `Where(IdEmpresa == …)` para aislar tenants; el filtro de empresa sigue siendo la
  regla de negocio entre empresas del mismo tenant.
- Requiere los scripts 025 a 030 aplicados antes de publicar (EF lee `id_tenant`).

## Cómo se verifica

- `FiltroDeTenantTests`: todas las entidades de tenant tienen el filtro y, con dos tenants, cada contexto ve solo lo suyo;
  sin tenant no ve nada de tenant; la plataforma ve todo; las tablas mixtas dejan ver las filas globales.
- `AmbitoDePlataformaTests`: la lista de rutas es la revisada, sin `IgnoreQueryFilters` ni contextos armados a mano.
- `OutboxTenantSqlServerTests`: cada evento corre con el tenant de su empresa contra SQL Server.
