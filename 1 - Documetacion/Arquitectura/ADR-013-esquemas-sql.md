# ADR-013 · Esquemas SQL: saas y seg; módulos ERP en dbo con prefijo

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D13 del plan)
- **Fase:** F0 (`seg` en la prueba de concepto), F1 en adelante

## Contexto

- Todo el esquema vive en `dbo`. [ESTANDARES_ERP.md](../ESTANDARES_ERP.md) (§5.1) fija prefijos de módulo para las
  tablas nuevas (`ct_` adoptado; `inv_`, `ban_`, `cxc_`, `cxp_`, `rh_` propuestos) y no renombra las existentes.
- El plano de control (Core SaaS) no pertenece a ningún tenant y no lleva RLS de tenant.
- Las funciones y políticas de RLS deben quedar separadas de los datos.

## Decisión

| Esquema | Contenido |
|---|---|
| `saas` | Plano de control: tenants, planes y versiones, derechos de uso, suscripciones, facturas SaaS, cobros, pagos, pasarelas, operadores. Sin RLS de tenant. Candidato a base propia en el futuro. |
| `seg` | Objetos de seguridad: funciones de predicado y políticas de Row-Level Security. |
| `dbo` | Kernel compartido, Core ERP y módulos, con el prefijo de módulo de ESTANDARES_ERP para las tablas nuevas. Las tablas existentes no se renombran. |

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Todo en `dbo` con prefijo | El plano de control quedaría mezclado con los datos de los tenants y sería más difícil darle permisos propios o moverlo a otra base. |
| Un esquema por módulo ERP | Cambia la convención a mitad del proyecto y obliga a renombrar o a convivir con dos estilos sin ganancia de seguridad. |

## Consecuencias

- Los scripts que creen objetos en `saas` o `seg` crean primero el esquema si no existe.
- Las convenciones de nombres de ESTANDARES_ERP (PK_, FK_, CK_, UX_, IX_) se aplican igual en los tres esquemas.

## Cómo se verifica

- Ninguna tabla de `saas` tiene política de RLS de tenant; toda tabla de tenant en `dbo` sí la tiene (F1).
