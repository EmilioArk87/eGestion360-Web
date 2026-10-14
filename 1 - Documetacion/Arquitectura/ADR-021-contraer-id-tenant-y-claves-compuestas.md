# ADR-021 · Contraer id_tenant y claves foráneas compuestas (D29 a D32)

- **Estado:** Propuesta (implementada en F1.3b; pendiente de confirmación de Emilio)
- **Fecha:** 2026-10-13
- **Decidió:** Claude, al implementar F1.3b del plan de la Fase 1; Emilio las confirma o las cambia
- **Fase:** F1
- **Completa:** [ADR-001](ADR-001-aislamiento-multitenant.md) (capa 7: claves foráneas compuestas),
  [ADR-004](ADR-004-persona-maestra-por-tenant.md) y [ADR-020](ADR-020-filtro-global-de-tenant-en-ef.md).

## Contexto

F1.2 dejó `id_tenant` en las tablas de tenant, nulable y lleno. Falta cerrarlo (ADR-001: expandir → migrar → contraer): que
la columna sea obligatoria y que la base misma impida que una fila apunte a la de otro tenant, aunque la aplicación y el
filtro de EF fallen. Los scripts 031 a 035 lo hacen por grupo (uno por cada script 026 a 030).

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| D29 | Toda clave foránea entre tablas de tenant es compuesta `(id_tenant, …)` | 78 claves, 32 de ellas a `empresas`. Cada una reemplaza a la simple, conserva su regla de borrado (6 en cascada) y apunta a una clave única `(id_tenant, …)` del padre (18). Se crea en el script del grupo que llega después, cuando ya existen las dos tablas contraídas. Las claves repetidas con dos nombres (14: la de EF y la de un script viejo) se fusionan en una. `ON DELETE SET NULL` pasa a `NO ACTION` en 4 claves (conductor y ruta de cargas y odómetro, ruta de vehículos): una clave compuesta pondría también `id_tenant` en nulo. La aplicación no borra esas filas (borrado lógico). |
| D30 | El modelo de EF no cambia | Las relaciones siguen declaradas con claves simples (describen la navegación); la composición la impone la base. Una prueba compara las relaciones de EF entre tablas de tenant con las claves compuestas de los scripts y falla si alguna falta. |
| D31 | Los índices por empresa no se reescriben todavía | Se agregan solo las claves únicas `(id_tenant, …)` que necesitan los padres y se pasa a **por tenant** el índice del documento de identidad (`UX_persona_documentos_identidad`: el mismo documento puede estar en dos tenants, ADR-004). Pasar índices a `id_tenant` primero se decide en F1.4 con los planes de consulta a la vista. |
| D32 | Aplicar tras una semana en producción sin `id_tenant` nulo | Desde `NOT NULL`, quien inserte sin `id_tenant` falla: la versión anterior a F1.2 deja de funcionar. El PRECHECK aborta si hay una fila sin tenant, una fila que apunte a otro tenant o un esquema distinto del esperado. |

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| D29 | Conservar `SET NULL` en las claves compuestas | No existe: pone en nulo todas las columnas de la clave, incluido `id_tenant NOT NULL`. |
| D29 | Dejar las simples y vigilar con disparadores | Un disparador por tabla es código a mantener; la clave compuesta es declarativa y la comprueba el motor. |
| D30 | Remodelar las 56 relaciones de EF con claves alternas compuestas | Cambia el modelo de 40 entidades sin cambiar lo que EF envía a la base; el riesgo no compensa. |
| D31 | Reescribir ahora todos los índices con `id_tenant` primero | Son decenas de índices y se decide mejor con los planes reales de RLS (F1.4). |

## Consecuencias

- Una fila de un tenant no puede apuntar a una de otro: lo rechaza el motor (error de clave foránea), no solo la aplicación.
- Una tabla de tenant nueva nace con `id_tenant NOT NULL`, su clave única `(id_tenant, id)` si otras la referencian y sus claves
  compuestas; `ScriptsContraerTests` y el arnés de aislamiento lo vigilan.
- `PersonaValidacionService` busca el documento duplicado solo dentro del tenant de la persona (o de la empresa si es un alta).
- Revertir: cada script trae su ROLLBACK (claves simples con su regla original, claves únicas fuera, `id_tenant` nulable).

## Cómo se verifica

- Ensayo sobre una restauración de la copia real con 025 a 030: aplicar 031 a 035, repetir, revertir y volver a aplicar, y
  7 abortos (fila sin tenant, 032 sin 031, fila que apunta a otro tenant, falta una clave, fallo a mitad de la transacción,
  insert sin `id_tenant`).
- Recorrido de la aplicación sobre esa base: 19 comprobaciones de aislamiento y altas reales (cargo, empresa y persona con
  documento, vínculo, empleado y bitácora, todos con el tenant correcto).
- `ScriptsContraerTests`: toda relación de EF entre tablas de tenant es clave compuesta en algún script.
