# ADR-022 · Filtro de borrado lógico solo en las entidades que nadie referencia (D33 y D34)

- **Estado:** Propuesta (implementada en F1.3c; pendiente de confirmación de Emilio)
- **Fecha:** 2026-10-13
- **Decidió:** Claude, al implementar F1.3c del plan de la Fase 1; Emilio las confirma o las cambia
- **Fase:** F1
- **Completa:** [ADR-020](ADR-020-filtro-global-de-tenant-en-ef.md) (D28: F1.3 en tres partes) y
  [ADR-001](ADR-001-aislamiento-multitenant.md) (capa 4); usa los filtros con nombre de EF 10 ([ADR-017](ADR-017-dotnet-10.md)).

## Contexto

El plan pedía un filtro global de «borrado lógico», separado del de tenant, en cada entidad con `Eliminado` (32). Su valor es que
ninguna consulta nueva olvide `!Eliminado`. Su riesgo es que EF aplica el filtro también a las navegaciones: una fila que apunta
(con `Include` o con una proyección) a una entidad borrada pierde esa navegación o, si la relación es obligatoria, desaparece de
la consulta. Con el filtro en un vehículo, una ruta o una persona, el historial de cargas, salidas o salarios que los
referencia dejaría de verse al borrarlos. Hoy la base real solo tiene 3 clientes borrados y ninguna fila de historial que
apunte a un padre borrado, así que el efecto no se nota todavía, pero las pantallas de baja existen.

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| D33 | El filtro `borrado` va solo en las entidades con `Eliminado` que ninguna otra referencia | 13 «hojas»: cargas de combustible, cargos, control de salidas, empleados, gastos de repuestos, notas, odómetro, órdenes de mantenimiento, peajes, documentos de personas, pólizas, proveedores y salarios. Las 19 que otras referencian (vehículos, rutas, talleres, tipos de vehículo, categorías, personas, vínculos, clientes, facturas, pagos, empresas y los catálogos de facturación y contables) siguen filtrando con `!Eliminado` donde corresponde. `ApplicationDbContext` lo decide por el modelo: si una entidad llega a ser referenciada, deja de llevarlo. |
| D34 | Ignorar el filtro de borrado se pide por su nombre; ignorar el de tenant sigue prohibido | Una pantalla que deba ver filas borradas de una hoja usa `IgnoreQueryFilters([ApplicationDbContext.FiltroBorrado])`. Cualquier otro uso de `IgnoreQueryFilters` en el código de la aplicación falla una prueba. |

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| D33 | Filtro en las 32 entidades, como decía el plan | Oculta el historial que apunta a filas borradas (probado: con las 32, tres pruebas de fusión de personas dejan de ver sus filas) y obliga a revisar decenas de pantallas para que lo ignoren. |
| D33 | Sin filtro de borrado | Las hojas son justo donde más se listan filas operativas (cargas, peajes, salidas) y donde olvidar `!Eliminado` muestra datos dados de baja. |
| D34 | Permitir `IgnoreQueryFilters()` sin nombre | Quitaría también el filtro de tenant. |

## Consecuencias

- Una hoja borrada desaparece de listas y de `Find`/`FirstOrDefault` (una pantalla de edición responde 404, no 500).
- Las pruebas del arnés fijan las dos listas: una entidad nueva con `Eliminado`, o una relación nueva hacia una hoja, hace fallar
  `FiltroDeBorradoTests` y obliga a decidir (ADR-022).
- Cerrar el hueco de las 19 entidades «principales» exigiría que el historial muestre «(eliminado)» en vez de perder la fila; queda
  como mejora de producto, no de aislamiento.

## Cómo se verifica

- `FiltroDeBorradoTests`: las dos listas; una hoja con el filtro nunca es referenciada; con una fila viva, una borrada y una de otro
  tenant por cada hoja, una consulta normal devuelve solo la viva, ignorar solo el filtro de borrado devuelve la borrada sin
  salirse del tenant, e ignorar los dos devuelve también el otro tenant; los principales siguen viendo sus filas borradas.
- `AmbitoDePlataformaTests`: solo se admite `IgnoreQueryFilters` con `ApplicationDbContext.FiltroBorrado`.
- Recorrido de la aplicación sobre la copia con 025 a 035: 19 comprobaciones de aislamiento, altas reales y una hoja borrada que
  responde 404 en su pantalla de edición.
