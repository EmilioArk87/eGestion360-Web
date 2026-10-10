# ADR-006 · La factura fiscal de la plataforma la emite el tenant interno de SIP

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D6 del plan)
- **Fase:** F4

## Contexto

- Hay dos mundos financieros que no deben mezclarse:
  - **Nuestra plataforma:** Tenant → Suscripción → Factura SaaS → Cobro → Pago.
  - **ERP de cada tenant:** Cliente final → Venta o servicio → Factura → CxC → Contabilidad.
- Lo que cobramos a los tenants debe documentarse con una factura fiscal con CAI.
- El módulo de Facturación con CAI ya existe.

## Decisión

- El Core SaaS (esquema `saas`) guarda la suscripción, la factura SaaS (estado de cuenta), los cobros y los pagos.
- Un **tenant interno de SIP** usa nuestro propio módulo de Facturación para emitir la factura fiscal. Lo dispara el
  evento `saas.factura.emitida` a través del outbox. En ese ERP cada tenant es un cliente más.
- La plataforma no lee ni escribe los datos financieros del ERP de ningún tenant cliente; hacia ellos solo controla el
  acceso (derechos de uso y estado del tenant).

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Módulo fiscal propio dentro del Core SaaS | Duplica la facturación con CAI que ya existe y obliga a mantener dos implementaciones fiscales. |

## Consecuencias

- El tenant interno es el primer usuario real del ERP: lo que vendemos se prueba en casa.
- El único acoplamiento entre los dos mundos es un evento. Si Facturación falla, la factura SaaS queda pendiente de
  documento fiscal y el outbox reintenta.
- Hasta F4, los primeros clientes se facturan a mano desde el tenant interno
  ([ADR-010](ADR-010-orden-contabilidad-y-cobro-saas.md)).

## Cómo se verifica

- Ninguna tabla del esquema `saas` tiene FK hacia tablas del ERP de un tenant cliente, ni al revés.
- Reprocesar el evento no emite una segunda factura fiscal (idempotencia).
