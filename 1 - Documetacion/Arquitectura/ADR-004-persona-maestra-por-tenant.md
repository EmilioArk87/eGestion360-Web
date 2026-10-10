# ADR-004 · Persona maestra acotada al tenant

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D4 del plan)
- **Fase:** F1 (`id_tenant`) y F2 (organizaciones, contactos y roles nuevos)

## Contexto

- La persona maestra (scripts 014 a 019) es global: `personas` no tiene empresa y una persona se vincula con varias
  empresas por `persona_empresa`. El flujo D8 permite vincular a una persona cuyo documento ya existe en otra empresa,
  después de verificar datos.
- En SaaS, entre clientes distintos, ese flujo le revelaría a un cliente que otro cliente tiene registrada a esa persona.
- Verificado el 2026-10-04: 0 personas tienen vínculo con más de una empresa.

## Decisión

- `personas` y `persona_documentos` pertenecen al tenant (`id_tenant`). La unicidad del documento normalizado pasa a ser
  por tenant.
- `persona_empresa` solo vincula personas con empresas del mismo tenant (FK compuesta con `id_tenant`).
- Dentro del tenant se conserva todo el comportamiento actual: verificación D8 entre empresas del tenant, fusión, roles
  de empleado y cliente, bitácora por campo.
- La pantalla «Personas del sistema» del administrador general pasa a ser una herramienta de soporte con acceso
  auditado, o se limita a un tenant.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Global (como hoy) | Cruza la frontera entre clientes y expone datos personales. |
| Por empresa | Perdería la persona compartida entre las empresas de un mismo grupo, que es la razón de la persona maestra. |

## Consecuencias

- Como ninguna persona tiene vínculos en más de una empresa, asignar el tenant no separa a nadie.
- El índice único del documento normalizado, la bitácora y el historial incluyen `id_tenant`.
- La persona sigue siendo el maestro de terceros que usará el CRM: el prospecto será un rol más del vínculo.

## Cómo se verifica

- Un documento registrado en el tenant A no produce ningún aviso, sugerencia ni error distinto al registrarlo en el
  tenant B.
- Las pruebas de personas y vínculos existentes siguen pasando dentro de un tenant.
