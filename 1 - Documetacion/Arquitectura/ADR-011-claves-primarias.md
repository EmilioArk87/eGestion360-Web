# ADR-011 · Claves primarias: INT/BIGINT más uid_publico donde se expone

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D11 del plan)
- **Fase:** F1 en adelante

## Contexto

- Todas las tablas usan claves `INT IDENTITY` (`id_<entidad>`). Cambiar a GUID sería reescribir el esquema completo.
- Algunas entidades se expondrán fuera de la aplicación: API, webhooks, metadatos de pasarelas de pago.
- El modelo híbrido ([ADR-001](ADR-001-aislamiento-multitenant.md)) podría mover un tenant grande a una base propia.

## Decisión

- Mantener `INT IDENTITY`; usar `BIGINT IDENTITY` en tablas transaccionales de alto volumen nuevas.
- Agregar `uid_publico UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID()` con índice único en las entidades que se
  expongan por API, webhooks o pasarelas. Fuera de la aplicación nunca se exponen ids numéricos.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| GUID en todas las tablas | Reescritura total sin beneficio hoy; índices más grandes. |
| Secuencias por tenant | Más complejidad sin necesidad mientras haya aislamiento por `id_tenant`. |

## Consecuencias

- Mover un tenant a una base dedicada conserva sus ids (`IDENTITY_INSERT`). Devolverlo a una base compartida exigiría
  remapear ids; se acepta porque es un caso excepcional.

## Cómo se verifica

- Ningún endpoint externo ni payload de webhook contiene ids numéricos internos.
