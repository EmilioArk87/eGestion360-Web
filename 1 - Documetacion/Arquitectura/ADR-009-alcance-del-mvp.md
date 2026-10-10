# ADR-009 · Alcance del MVP: plataforma + Facturación + Contabilidad automática

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D9 del plan)
- **Fase:** se completa al cerrar F4

## Contexto

- El objetivo es una plataforma SaaS sólida y vendible, no muchos módulos a medias.
- Ya existen Facturación con CAI, CxC básica, Flota, la persona maestra y el outbox de eventos.
- La contabilidad automática es el diferenciador buscado y el motor que necesitan los módulos posteriores.

## Decisión

El MVP incluye:

- Kernel multi-tenant con aislamiento probado, identidad con MFA para administradores, roles, permisos y auditoría.
- Derechos de uso por plan (módulos y límites) asignados por el operador.
- Core ERP: empresas, sucursales, puntos de emisión, terceros, productos, impuestos, monedas, numeraciones, periodos y
  centros de costo.
- Facturación con CAI y CxC, adaptadas al modelo nuevo.
- Contabilidad automática: catálogo, asientos manuales y automáticos desde Facturación, mayor, balanza de comprobación,
  balance general, estado de resultados y cierre de periodo.
- Flota.
- Suscripción con factura SaaS, pago manual, morosidad y suspensión automáticas.
- Consola del operador y alta de tenants asistida.

Queda fuera: pasarelas de pago automáticas, autoservicio completo, Bancos, Compras y CxP, CRM, Servicios, Inventarios,
Activos fijos, Presupuesto, Talento Humano y Finanzas gerenciales.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| MVP con CRM en lugar de Contabilidad | Se vende antes, pero deja sin probar el motor contable del que dependen Bancos, Activos, Presupuesto, Finanzas y Nómina. |

## Consecuencias

- El MVP técnico llega al cerrar F3 y el comercial al cerrar F4 ([ADR-010](ADR-010-orden-contabilidad-y-cobro-saas.md)).
- Prueba con un caso real las partes más difíciles (aislamiento, eventos y reglas contables) antes de multiplicar módulos.

## Cómo se verifica

- Criterios de salida de F3 y F4 en el plan: cada factura, nota y pago genera un asiento cuadrado e idempotente; el ciclo
  de suscripción (prueba, activa, morosa, suspendida, reactivada, cancelada) pasa con reloj simulado.
