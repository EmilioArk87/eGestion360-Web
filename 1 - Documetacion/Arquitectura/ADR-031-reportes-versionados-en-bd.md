# ADR-031 · Reportes versionados en la base de datos por tenant

- **Estado:** Propuesta (pendiente de aprobación de Emilio; RP3 sin PDF nativo ya decidido el 2026-11-22). Fase 1: script 047 aplicado en `eBD_SPD` y código publicado el 2026-11-25 (`dev` `f222fc1`); ese día se verificó en producción con impresiones reales de la constancia de licencia (versión 1, con su fila en `dbo.rep_ejecuciones`).
- **Fecha:** 2026-11-22
- **Decide:** Emilio. Diseño de Claude, a partir de [PLAN_MOTOR_REPORTES_BD.md](../PLAN_MOTOR_REPORTES_BD.md)
- **Fase:** complementaria al roadmap F0–F9 (Fase 1 del plan de reportes: infraestructura y piloto de constancias)
- **Desarrolla:** [ADR-013](ADR-013-esquemas-sql.md) (esquema `dbo` con prefijo), [ADR-015](ADR-015-predicados-rls-solo-por-tenant.md) (tablas mixtas: global o de un tenant) y el patrón de inmutabilidad de `dbo.bitacora_cambios` (script 014) y de las tablas de auditoría (script 045, [ADR-030](ADR-030-auditoria-de-seguridad.md))

> Las decisiones de este ADR se numeran **RP1 a RP9** para no chocar con la numeración D del roadmap principal, que siguen usando otras etapas en paralelo.

## Contexto

Hoy cada salida impresa o exportable vive en el código:

- La **constancia psicológica** (licencia de conducir) tiene sus medidas en milímetros, el CSS, la redacción y la versión (`"1"`) en `Services/Psicologia/PlantillasConstancia.cs` y `Pages/Psicologia/Constancias/Imprimir.cshtml`. Cada ajuste de un milímetro (ya hubo varios el 2026-11-16 y el 2026-11-17) exige compilar y publicar. `psi.impresiones` guarda `plantilla` y `version_plantilla`, pero la versión es un texto escrito a mano y no permite reconstruir cómo se veía una constancia emitida.
- Los **KPI de Flota** se calculan en C# y se exportan con ClosedXML.
- Vienen **facturas con CAI**, notas de crédito y débito, recibos, estados de cuenta y reportes contables.

Se quiere que el diseño, las medidas y los textos de cada reporte se guarden en la base de datos, versionados, con herencia de una plantilla global hacia la personalización de cada tenant, con histórico inmutable y con registro de qué versión se usó en cada impresión.

## Decisión

| # | Decisión | Cómo queda |
|---|----------|------------|
| RP1 | **Datos en código, presentación en BD** | En BD: la plantilla (HTML y CSS con Scriban), la configuración en JSON (medidas en mm, papel, textos legales, columnas de Excel) y los metadatos. En código: las **fuentes de datos** (`IReporteDataSource`, una clase por fuente, p. ej. `psi.constancia`). **Nunca** se guarda ni se ejecuta SQL editable desde la BD |
| RP2 | **Motor de plantillas: Scriban** | Paquete NuGet liviano y con *sandbox*: la plantilla no puede ejecutar C# ni tocar archivos (a diferencia de compilar Razor en tiempo de ejecución). Toda salida de datos de personas pasa por `html.escape` |
| RP3 | **Excel: ClosedXML** (ya instalado); **sin PDF nativo en la Fase 1** (decidido por Emilio el 2026-11-22) | La impresión sigue siendo HTML/CSS con `window.print()`, como hoy. Excel con un esquema declarativo en `configuracion_json` (hojas, columnas, formatos, totales) llega en la Fase 3. El tipo `pdf` queda en el CHECK para un renderizador futuro (p. ej. QuestPDF) |
| RP4 | **Herencia Global → Tenant** con tablas mixtas | `id_tenant` nulo = plantilla global de la plataforma; con valor = personalización o reporte propio del tenant. Al generar se busca `(tenant actual, codigo)` y, si no hay, `(NULL, codigo)`. RLS con `seg.fn_mixta_lectura` / `seg.fn_mixta_escritura` (como `dbo.tasas_cambio`): un tenant ve las globales y las suyas y solo escribe las suyas; las globales solo se escriben con `ambito = 'plataforma'` en el contexto de sesión |
| RP5 | **Versiones inmutables una vez publicadas** | Una versión nace `borrador`, se publica una sola vez (`publicada`) y solo puede pasar a `archivada`. Publicada o archivada no cambia su contenido ni se borra; un borrador sí. Lo garantiza un disparador en la BD, no solo el código |
| RP6 | **Huella del contenido** | `hash_sha256` es una columna **calculada y persistida** en la BD: SHA-256 de `contenido_plantilla + N'|' + ISNULL(configuracion_json, N'')` en UTF-16LE (el `NVARCHAR` de SQL Server), en hexadecimal mayúscula. Nadie puede escribir una huella que no corresponda al contenido; el código puede recalcularla con `Encoding.Unicode` para verificar |
| RP7 | **Una sola versión activa por reporte** | `rep_reportes.id_version_activa` apunta a una versión `publicada` **del mismo reporte**. Revertir es mover ese puntero a una versión publicada anterior (y dejarlo en la bitácora como `REVERTIR`); no se copia contenido. No se puede archivar la versión activa |
| RP8 | **Bitácora y ejecuciones inmutables** | `dbo.rep_reporte_bitacora` (mixta) registra cada acción sobre definiciones y versiones. `dbo.rep_ejecuciones` (de tenant, `seg.fn_tenant_actual`) registra cada generación con la versión exacta (`id_version` y `numero_version`). Las dos rechazan `UPDATE` y `DELETE` con un disparador `INSTEAD OF`, como la auditoría (045). La vista previa de un borrador no deja ejecución |
| RP9 | **Convivencia con Psicología** | `psi.impresiones` no cambia en la Fase 1. Al conectar el motor, la impresión registra además una fila en `dbo.rep_ejecuciones`, y `version_plantilla` pasa a guardar el `numero_version` real de la BD |

### Pendiente de decidir antes de codificar las pantallas

- **Permisos de edición (Fase 2):** ¿el administrador de cada tenant puede clonar y editar sus plantillas (`reportes.plantillas.*`) o solo el operador de plataforma edita versiones por tenant? La RLS de RP4 permite las dos; lo que cambia es el catálogo de permisos y quién ve las pantallas. Si los tenants editan, hay que limitar el HTML que aceptan (sin `<script>` ni manejadores `on*`), porque la plantilla se muestra a otros usuarios del tenant.


## El modelo de datos (script 047)

| Tabla | Tipo de tabla | RLS en `seg.pol_tenant` | Qué guarda |
|---|---|---|---|
| `dbo.rep_reportes` | mixta (`ITenantOpcionalEntity`) | `fn_mixta_lectura` / `fn_mixta_escritura`, 5 predicados | El reporte: código, nombre, módulo, fuente de datos, tipo de salida y la versión activa |
| `dbo.rep_reporte_versiones` | mixta | ídem | Cada versión con su número, estado, plantilla, configuración JSON, huella, motivo y versión de origen (si se clonó) |
| `dbo.rep_reporte_bitacora` | mixta | ídem | Quién hizo qué sobre un reporte o una versión, cuándo, con qué motivo y con qué petición. Inmutable |
| `dbo.rep_ejecuciones` | de tenant (`ITenantEntity`) | `fn_tenant_actual`, 5 predicados | Cada generación o impresión con la versión exacta usada, el registro de origen y los parámetros. Inmutable |

Reglas que valida la BD (disparadores `AFTER` en reportes y versiones):

- Una versión tiene el mismo `id_tenant` que su reporte (una versión global no puede colgar de un reporte de tenant ni al revés).
- Al insertar, una versión nace `borrador` o `publicada` (la semilla global nace publicada); `fecha_publicacion` va si y solo si no es borrador.
- No cambian nunca `id_reporte`, `id_tenant` ni `numero_version`. De una versión publicada o archivada solo cambia el estado de `publicada` a `archivada`. Solo un borrador se borra.
- `id_version_activa` apunta a una versión publicada del mismo reporte, y esa versión no se puede archivar mientras sea la activa.

### Semilla

El script siembra el reporte global `psi_constancia_licencia` con su versión 1 **publicada y activa**, con las mismas medidas, el mismo CSS y la misma redacción que hoy tiene `Imprimir.cshtml` (215 × 275 mm, fecha a 35 mm, cuerpo a 74 mm, pruebas a 204 mm). La semilla se escribe con `ambito = 'plataforma'` en el contexto de sesión solo durante el script, y lo restituye al terminar. Sembrarla no cambia lo que se imprime: la página sigue usando su plantilla en Razor hasta que se conecte el motor.

## Implementación de la Fase 1 (código)

- **Paquete:** `Scriban` 7.6.1.
- **Modelos:** `Models/Reportes/Reportes.cs` (`Reporte`, `ReporteVersion`, `ReporteBitacora`, `ReporteEjecucion` y las constantes de estado, salida y acción) y `Data/Configuracion/ReportesConfiguracion.cs` (índices únicos filtrados, la huella como columna calculada y los disparadores declarados).
- **Servicios:** `Services/Reportes/`. `ScribanReporteRenderer` compila y genera con tope de vueltas, de recursión y de salida, sin `include` y con cultura invariable. `MotorReportesService` (`IMotorReportes`) resuelve tenant → global, guarda la plantilla compilada por `id_version` y arma la fila de ejecución. `ConstanciaPsicologiaDataSource` es la fuente `psi.constancia`: da a los datos que ya lee la pantalla la forma que espera la plantilla. Una interfaz común de fuentes (`IReporteDataSource`) queda para cuando haya una segunda fuente.
- **Constancias:** `Imprimir` usa la versión activa del motor si la hay y, si no (el 047 sin aplicar, el reporte inactivo o una plantilla que falla), la hoja de siempre en Razor. Al registrar, `psi.impresiones.version_plantilla` guarda el número de la versión publicada y `dbo.rep_ejecuciones` recibe su fila en el mismo `SaveChanges`. El orden de publicación es libre: el código funciona con o sin el 047.
- **Verificadores:** `verificar_despues_f15.sql` y `verificar_despues_f17.sql` suman las tablas, claves y predicados del 047 (`con047`) para que la comprobación diaria no dé FALLA cuando se aplique.

## Consecuencias

- **A favor:** un ajuste de medidas o de redacción deja de exigir una publicación; cada impresión queda atada a la versión exacta; un tenant puede tener su propia versión sin tocar la de los demás; el mismo motor sirve para facturas y contabilidad.
- **En contra:** una plantilla mal escrita rompe la impresión en tiempo de ejecución en vez de en compilación (se mitiga con la vista previa antes de publicar y validando que la plantilla compile al guardar el borrador); el HTML editable por tenants exige limitar lo que se acepta (ver pendientes).
- **Límite conocido:** `TRUNCATE TABLE` no dispara los disparadores; solo quien puede modificar el esquema puede hacerlo (igual que en la auditoría, ADR-016).
