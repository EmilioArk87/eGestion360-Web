# Arquitectura SaaS multi-tenant — eGestion360

Registro de las decisiones de arquitectura (ADR) que convierten eGestion360 en una plataforma ERP + CRM SaaS
multi-tenant. Las 14 decisiones del plan se aprobaron el **2026-10-10**, y ese mismo día la decimoquinta (ADR-015),
que salió de la prueba de concepto de RLS. El análisis completo (diagnóstico, diagramas,
dependencias, MVP y roadmap) está en el artifact del plan:
https://claude.ai/artifact/DCHBZVf4QPoqkdLBJ5xmEt (privado; solo lo abre quien tenga acceso).

> Esta carpeta describe el **modelo objetivo**. Mientras una fase no se implemente, el código sigue funcionando como
> describe [ESTANDARES_ERP.md](../ESTANDARES_ERP.md). Cada ADR dice en qué fase se aplica.

---

## Resumen de la arquitectura aprobada

**Monolito modular**: una sola aplicación desplegable, dividida en módulos con fronteras explícitas. Tres piezas:

| Pieza | Responsabilidad | Ejemplos |
|---|---|---|
| **Core SaaS** (plano de control, esquema `saas`) | Administrar la plataforma y cobrar a los tenants | Tenants y su estado, planes y versiones, suscripciones, facturas SaaS, cobros y pagos, puerto de pasarelas |
| **Kernel compartido** | Infraestructura transversal, sin dominio | Aislamiento, identidad, autorización, derechos de uso, auditoría, outbox, catálogos globales |
| **Core ERP/CRM** (por tenant) | Maestros que usan todos los módulos | Empresas, sucursales, puntos de emisión, terceros, productos, impuestos, monedas, numeraciones, periodos, dimensiones |

Reglas de convivencia entre módulos:

1. Cada módulo es dueño de sus tablas; ningún módulo escribe en tablas de otro.
2. Las lecturas entre módulos pasan por servicios de consulta con interfaz pública.
3. Las reacciones entre módulos pasan por eventos del outbox (`domain_events`): asíncronos, idempotentes y versionados.
4. Lo transversal (tenant, permisos, auditoría, numeración, impuestos, conversión de moneda) se usa desde el kernel o el
   Core ERP; no se reimplementa en cada módulo.
5. El código nunca pregunta por el nombre de un plan; pregunta por un derecho de uso.

### Roadmap

| Fase | Contenido | Hito |
|---|---|---|
| F0 | Fundamentos: ADR, CI, LocalDB y prueba de concepto de RLS, saneamiento, comparación de hosting | |
| F1 | Kernel: tenancy, aislamiento, identidad, autorización, derechos de uso, auditoría | |
| F2 | Core ERP/CRM: maestros, sucursales, numeraciones, periodos, dimensiones | |
| F3 | Contabilidad automática | MVP técnico |
| F4 | Comercialización SaaS: planes, suscripciones, cobro con pago manual (requiere migrar hosting) | MVP comercial |
| F5 | Bancos y Tesorería, Compras básicas y CxP | |
| F6 | CRM y Servicios | |
| F7 | Inventarios y Activos fijos | |
| F8 | Presupuesto y aprovisionamiento, Talento Humano | |
| F9 | Finanzas gerenciales, analítica, automatización, escala | |

CRM y Talento Humano pueden avanzar en paralelo desde F2; las pasarelas de pago, desde F4. Cada fase cierra con
pruebas, QA con el rol real, informe y autorización de Emilio.

### Regla de aislamiento de cada tabla

Cada tabla tiene su categoría (tenant, mixta, infraestructura, global, plataforma o legado) en
`eGestion360Web.Tests/Arquitectura/ClasificacionDeTablas.cs`. El arnés `ArnesAislamientoTests` falla si se agrega una
tabla sin clasificar o si una tabla de tenant nueva nace sin `id_tenant`. La lista de tablas que aún no tienen
`id_tenant` solo puede bajar; F1 termina cuando queda vacía. Ver
[PRUEBAS_AUTOMATIZADAS.md](../PRUEBAS_AUTOMATIZADAS.md).

---

## Índice de ADR

| ADR | Decisión | Fase | Estado |
|---|---|---|---|
| [ADR-001](ADR-001-aislamiento-multitenant.md) | Base compartida con `id_tenant` y Row-Level Security | F0–F1 | Aceptada 2026-10-10; un punto reemplazado por ADR-015 |
| [ADR-002](ADR-002-jerarquia-organizativa.md) | Tenant → Empresa → Sucursal → Punto de emisión; dimensiones fuera del árbol | F1–F2 | Aceptada 2026-10-10 |
| [ADR-003](ADR-003-identidad-y-membresias.md) | Cuenta de acceso global con membresías por tenant | F1 | Aceptada 2026-10-10 |
| [ADR-004](ADR-004-persona-maestra-por-tenant.md) | Persona maestra acotada al tenant | F1–F2 | Aceptada 2026-10-10 |
| [ADR-005](ADR-005-catalogo-contable-por-empresa.md) | Catálogo contable por empresa, creado desde plantilla | F3 | Aceptada 2026-10-10 |
| [ADR-006](ADR-006-factura-fiscal-de-la-plataforma.md) | La factura fiscal SaaS la emite el tenant interno de SIP | F4 | Aceptada 2026-10-10 |
| [ADR-007](ADR-007-hosting.md) | Somee para desarrollo; migrar antes del primer cliente externo | F0, antes de F4 | Aceptada 2026-10-10 |
| [ADR-008](ADR-008-autenticacion-cookie-claims.md) | Autenticación por cookie con claims y políticas de autorización | F1 | Aceptada 2026-10-10 |
| [ADR-009](ADR-009-alcance-del-mvp.md) | El MVP incluye Contabilidad automática | F3–F4 | Aceptada 2026-10-10 |
| [ADR-010](ADR-010-orden-contabilidad-y-cobro-saas.md) | Contabilidad (F3) antes del cobro SaaS (F4) | F3–F4 | Aceptada 2026-10-10 |
| [ADR-011](ADR-011-claves-primarias.md) | Claves INT/BIGINT más `uid_publico` donde se expone | F1 en adelante | Aceptada 2026-10-10 |
| [ADR-012](ADR-012-pasarela-de-pago-inicial.md) | Solo pago manual en el MVP; pasarelas por puerto y adaptador | F4 | Aceptada 2026-10-10 |
| [ADR-013](ADR-013-esquemas-sql.md) | Esquemas `saas` y `seg`; módulos ERP en `dbo` con prefijo | F1 en adelante | Aceptada 2026-10-10 |
| [ADR-014](ADR-014-script-010-en-espera.md) | El script 010 no se aplica hasta rediseñarlo | F1–F3 | Aceptada 2026-10-10 |
| [ADR-015](ADR-015-predicados-rls-solo-por-tenant.md) | Predicados de RLS solo por tenant; la plataforma no lee datos de tenants (resultado de la PoC F0.5) | F1 | Aceptada 2026-10-10 |

---

## Cómo registrar una decisión nueva

- Un archivo por decisión: `ADR-NNN-descripcion-corta.md`, con el siguiente número libre. Nunca se reutiliza un número.
- Estados: **Propuesta** → **Aceptada** (con fecha) → **Reemplazada por ADR-NNN** o **Retirada**.
- Un ADR aceptado no se reescribe: si la decisión cambia, se crea uno nuevo que lo reemplaza y el viejo solo cambia su
  estado. Corregir erratas o agregar enlaces sí está permitido.
- Agregar la fila al índice de este archivo.

Plantilla:

```markdown
# ADR-NNN · Título de la decisión

- **Estado:** Propuesta
- **Fecha:** AAAA-MM-DD
- **Decidió:** nombre
- **Fase:** F#

## Contexto
Qué problema hay y qué datos lo respaldan.

## Decisión
Qué se decide, en términos verificables.

## Alternativas consideradas
Cada opción y por qué no se eligió.

## Consecuencias
Qué cambia en el código, en la base y en la operación; costos y riesgos.

## Cómo se verifica
Pruebas o revisiones que demuestran que se cumple.
```
