# ADR-014 · El script 010 de contabilidad no se aplica hasta rediseñarlo

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D14 del plan)
- **Fase:** F1 (`id_tenant`), F2 (centros de costo al Core), F3 (resto del módulo)

## Contexto

- `2 - Script SQL/010_ct_nucleo_contable.sql` (2026-08-06) nunca se aplicó: en eBD_SPD no hay tablas `ct_`.
- Se revisó el 2026-10-06 (commit `4a370dc`): FK compuestas `(id, id_empresa)` en las 7 relaciones internas, una sola
  transacción con POSTCHECK completo, `token_concurrencia`, moneda `CHAR(3)` con FK y reversa nueva. Quedó listo para
  aplicar.
- Le falta lo que exige la arquitectura aprobada: `id_tenant` ([ADR-001](ADR-001-aislamiento-multitenant.md)), y tiene
  `ct_centros_costo` dentro del módulo contable aunque los centros de costo son una dimensión del Core
  ([ADR-002](ADR-002-jerarquia-organizativa.md)).

## Decisión

- El 010 **no se aplica todavía**. Se rediseña por partes:
  - F1: `id_tenant` y FK compuestas con `id_tenant`.
  - F2: los centros de costo salen de `ct_` al Core ERP como dimensión jerárquica; ejercicios y periodos se toman del
    calendario fiscal del Core.
  - F3: reglas de contabilización, determinación de cuentas, bandeja de excepciones, saldos y multimoneda (moneda,
    monto original y tasa en cada movimiento).
- Se conserva lo bueno de la revisión del 2026-10-06: FK compuestas, transacción única, POSTCHECK, concurrencia
  optimista y moneda `CHAR(3)`.
- El 009 (integración contable del almacén de repuestos) también queda congelado. Figura en el índice como pendiente,
  pero el archivo `009_integracion_almacen_contable.sql` no está en el repositorio; se revisará en F3.
- `ContabilidadEventHandler` sigue deshabilitado en `Program.cs`.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Aplicarlo ya y agregar `id_tenant` después con ALTER | Las tablas quedarían vacías hasta F3 sin aportar nada, y habría que alterarlas dos veces (F1 y F2). |

## Consecuencias

- No hay tablas `ct_` hasta F3 y, por lo tanto, ningún dato contable que migrar.
- El índice de scripts conserva el 010 como «Pendiente»; el rediseño saldrá como scripts nuevos con su propio número.

## Cómo se verifica

- Antes de F3, `SELECT COUNT(*) FROM sys.tables WHERE name LIKE 'ct[_]%'` en eBD_SPD devuelve 0.
