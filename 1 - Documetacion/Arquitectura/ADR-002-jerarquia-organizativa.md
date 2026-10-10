# ADR-002 · Jerarquía organizativa: Tenant → Empresa → Sucursal → Punto de emisión

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D2 del plan)
- **Fase:** tenant en F1; sucursales, puntos de emisión y dimensiones en F2

## Contexto

- Hoy `empresas` cumple tres papeles: cliente comercial, frontera de aislamiento y entidad legal. No existen sucursales;
  `ESTANDARES_ERP.md` pedía no inventarlas salvo decisión explícita. Esta es esa decisión.
- Un cliente puede tener varias sociedades y querer administrarlas, consolidarlas y pagar una sola vez.
- La numeración fiscal en Honduras se organiza por establecimiento y punto de emisión. El detalle (formato y CAI) se
  valida con el reglamento de facturación del SAR antes de implementarlo.

## Decisión

```
Plataforma (SIP)
 └── Tenant            cuenta comercial: quien paga, frontera de aislamiento, unidad de respaldo y de límites
      └── Empresa      entidad legal: RTN, libros contables, moneda funcional, documentos fiscales
           └── Sucursal        establecimiento
                ├── Punto de emisión   numeración fiscal
                └── Bodega             existencias
```

- **Dimensiones fuera del árbol:** centros de costo (jerárquicos), unidades organizativas o departamentos y proyectos
  pertenecen a la empresa y cruzan sucursales. Un centro «Mantenimiento» puede tener gastos en dos sucursales.
- Las empresas de un mismo tenant pueden consolidar información y compartir maestros del tenant (personas, productos).
- El operador de la plataforma no es un tenant del plano de control, pero tiene un **tenant interno** para su propio ERP
  ([ADR-006](ADR-006-factura-fiscal-de-la-plataforma.md)).

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Tenant = empresa (como hoy) | Un cliente con varias sociedades pagaría y se administraría varias veces, sin consolidación ni usuarios compartidos. |
| Tenant → Grupo → Empresa | El tenant ya funciona como grupo. El nivel se puede agregar después sin romper nada. |

## Consecuencias

- `empresas` gana `id_tenant` (F1). Tablas nuevas para sucursales y puntos de emisión (F2).
- `factura_secuencias` se generaliza en un servicio de numeración por punto de emisión (F2).
- Los centros de costo salen del módulo contable (`ct_centros_costo`) y pasan al Core ERP como dimensión
  ([ADR-014](ADR-014-script-010-en-espera.md)).
- Las 4 empresas actuales pasan a ser empresas de tenants. En F1 se confirma a qué cliente pertenece cada una.

## Cómo se verifica

- Las FK compuestas impiden que una sucursal, un punto de emisión o una dimensión apunten a una empresa de otro tenant.
- Facturación emite con la numeración del punto de emisión (criterio de salida de F2).
