# ADR-005 · Catálogo contable por empresa, creado desde plantilla

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D5 del plan)
- **Fase:** F3

## Contexto

- Cada empresa es una entidad legal con sus propios libros ([ADR-002](ADR-002-jerarquia-organizativa.md)).
- Las sociedades de un mismo grupo pueden necesitar catálogos distintos.
- Los documentos contables apuntan a cuentas concretas: si una fila compartida cambiara, el historial cambiaría de
  significado.

## Decisión

- El catálogo contable pertenece a la **empresa**.
- Se crea copiando una plantilla de la plataforma (por país; Honduras primero) o el catálogo de otra empresa del mismo
  tenant. Desde ese momento es de la empresa.
- Un cambio legal en la plantilla se ofrece a cada empresa como propuesta para aplicar; no se impone.
- La consolidación entre empresas del tenant se hará con un mapeo entre catálogos (F9).
- La plantilla y las reglas contables se validan con un contador y fuentes oficiales antes de activarse, como exige
  [PROMPT_MAESTRO_CONTABILIDAD.md](../PROMPT_MAESTRO_CONTABILIDAD.md).

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Catálogo por tenant | Obliga a todas las sociedades del grupo a usar las mismas cuentas aunque su situación legal sea distinta. |
| Catálogo global referenciado | Un cambio de la plataforma alteraría el significado de documentos ya contabilizados. |

## Consecuencias

- `ct_cuentas` lleva `id_tenant` e `id_empresa`, con FK compuestas.
- Hace falta el proceso de alta de catálogo (copiar plantilla o copiar de otra empresa) en el aprovisionamiento.

## Cómo se verifica

- Dos empresas del mismo tenant pueden tener cuentas distintas con el mismo código sin conflicto.
- Ningún asiento puede usar una cuenta de otra empresa (FK compuesta).
