# ADR-007 · Hosting: Somee para desarrollo; migrar antes del primer cliente externo

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D7 del plan)
- **Fase:** comparación en F0 (F0.9); migración antes de F4

## Contexto

Somee (IIS compartido) tiene límites que no sirven para un SaaS con clientes que pagan:

- Descarga la aplicación cuando no hay visitas; el job de tasas de cambio ya necesita un cron de GitHub para despertarla
  ([TASAS_CAMBIO.md](../TASAS_CAMBIO.md)).
- La publicación es manual por FTP y no hay ambiente de staging.
- Verificado el 2026-10-10 con consultas de solo lectura: la base está en modelo de recuperación SIMPLE con un respaldo
  completo por día. No hay recuperación a un punto en el tiempo y se puede perder hasta un día de datos.
- El usuario de la base no puede crear bases nuevas.

## Decisión

- Seguir en Somee para desarrollo y pruebas internas.
- Antes del primer tenant externo, y en todo caso antes de F4, migrar a un hosting con:
  - proceso siempre activo para los jobs (outbox, cobros, morosidad, tasas);
  - recuperación a un punto en el tiempo;
  - ambiente de staging separado de producción;
  - TLS;
  - despliegue automatizado.
- F0.9 entrega una comparación con costos reales. La elección del proveedor se registrará en un ADR nuevo.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Quedarse en Somee | Sin proceso siempre activo, sin recuperación a un punto en el tiempo ni staging, los cobros y la morosidad no son confiables. |
| Migrar ya, en F0 | Adelanta un costo mensual antes de tener clientes externos; la arquitectura no depende del proveedor. |

## Consecuencias

- Hasta la migración, los jobs dependen del cron de GitHub y la pérdida máxima de datos es de un día.
- La arquitectura (cadena de conexión por tenant, autenticación por cookie, llaves de Data Protection persistidas) no
  debe depender de nada propio de Somee.

## Cómo se verifica

- F0.9: documento con opciones, costos mensuales y cumplimiento de cada requisito.
- Antes de F4: restauración de prueba a un punto en el tiempo hecha y documentada en el hosting nuevo.
