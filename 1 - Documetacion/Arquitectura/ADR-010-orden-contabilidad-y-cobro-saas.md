# ADR-010 · Contabilidad automática (F3) antes del cobro SaaS (F4)

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D10 del plan)
- **Fase:** F3 y F4

## Contexto

- Contabilidad y el cobro SaaS dependen de F1 y F2, pero no entre sí.
- Lo que todos los módulos necesitan desde el inicio es el **modelo** de planes y derechos de uso, no el cobro
  automatizado. Ese modelo va en F1.
- Los primeros clientes pueden facturarse a mano desde el tenant interno con nuestro módulo de Facturación
  ([ADR-006](ADR-006-factura-fiscal-de-la-plataforma.md)).

## Decisión

- F3: Contabilidad automática (hito: MVP técnico).
- F4: comercialización SaaS con planes, suscripciones, factura SaaS, pago manual y morosidad automática (hito: MVP
  comercial). Requiere haber migrado el hosting ([ADR-007](ADR-007-hosting.md)).
- Mientras tanto, el operador asigna el plan a mano y la factura se emite desde el tenant interno.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Cobro SaaS primero | Solo conviene con muchos tenants pagando pronto (más de unos 10 en 6 meses); con pocos, el cobro manual alcanza y el motor contable aporta más. |

## Consecuencias

- Si el ritmo de ventas cambia, F4 puede adelantarse a F3 sin rediseño: son independientes. El cambio se registraría en
  un ADR nuevo.

## Cómo se verifica

- Al cerrar F3 no hay ningún valor de plan escrito en el código: los derechos se leen del modelo de F1.
