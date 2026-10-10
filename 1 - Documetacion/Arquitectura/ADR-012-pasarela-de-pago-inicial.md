# ADR-012 · Pago manual en el MVP; pasarelas por puerto y adaptador

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D12 del plan)
- **Fase:** F4; pasarelas reales en el carril paralelo desde F4

## Contexto

- Transferencia y depósito son medios de pago comunes entre empresas en Honduras y permiten cobrar desde el primer
  cliente sin integrar una pasarela.
- Los candidatos locales (PixelPay, BAC Credomatic) ofrecen tokenización o cobro recurrente según fuentes secundarias;
  costos y condiciones se confirman con cada proveedor.
- El núcleo no debe depender de un proveedor.

## Decisión

- En el MVP, solo **pago manual**: el operador registra el pago con su comprobante y lo aprueba.
- Desde F4 existe el puerto `IPasarelaPago` con un adaptador `manual`. Cada pasarela real será otro adaptador; el núcleo
  nunca usa tipos del SDK de un proveedor.
- La primera pasarela real se elige con cotizaciones reales y se registra en un ADR nuevo.
- Reglas para cualquier pasarela:
  - el número de tarjeta nunca toca nuestros servidores (páginas o campos alojados por la pasarela, solo tokens);
  - cada cobro lleva clave de idempotencia;
  - los webhooks se verifican por firma, se guardan con el id del proveedor como único y se procesan por el outbox;
  - conciliación diaria contra el reporte del proveedor;
  - credenciales de la pasarela solo en el almacén de secretos.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Integrar una pasarela en el MVP | Agrega costo fijo, contrato y alcance de PCI antes de tener clientes que lo justifiquen. |

## Consecuencias

- Los primeros cobros requieren trabajo del operador; se acepta por el volumen esperado.

## Cómo se verifica

- Ninguna tabla guarda número de tarjeta ni código de seguridad.
- Reprocesar un webhook o reintentar un cobro no duplica el pago.
