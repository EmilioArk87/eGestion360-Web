# Verificación y emergencia de F1.7 (planes, derechos de uso y estado del tenant)

Complemento de [`LEEME.md`](LEEME.md) (la salida de F1, scripts 025 a 037). Este archivo cubre el **script 040** (`2 - Script SQL/040_planes_derechos_estado_tenant.sql`), su verificación de solo lectura y el script de emergencia que abre los derechos. El número 040 queda para F1.7 porque sale antes que F1.6.

> Los topes y la lista de módulos de los planes son **provisionales** (decisiones D52 a D56): ningún nombre ni valor se escribe en el código de la aplicación, que solo preguntará por derechos (`modulo.*`, `caracteristica.*`, `limite.*`) del plan vigente de su tenant. Se pueden editar sin publicar nada.

## Qué crea el script 040

Todo en el esquema `saas` (plano de control, **sin RLS**, ADR-013) y con las cuatro columnas de auditoría de `saas.tenants` (`creado_por`, `fecha_creacion`, `modificado_por`, `fecha_modificacion`):

| Objeto | Qué es |
|---|---|
| `saas.planes` | Catálogo de planes: `codigo` único (minúsculas), `nombre`, `descripcion`, `activo`. |
| `saas.plan_versiones` | Versiones de cada plan, con `numero` (único por plan), `vigente_desde` y `vigente_hasta` (fechas). |
| `saas.derechos` | Catálogo de derechos: `codigo` único (`modulo.flota`, `limite.usuarios`, `caracteristica.xxx`; el código empieza por su `tipo`), `tipo` (`modulo`, `caracteristica` o `limite`) y `descripcion`. |
| `saas.plan_version_derechos` | Qué derechos concede cada versión de plan. `valor_limite` solo existe en los derechos de tipo `limite` (NULL = sin tope). Una fila ausente significa derecho **no concedido**. |
| `saas.tenant_planes` | El plan de cada tenant en el tiempo. Un índice único filtrado (`UX_tenant_planes_vigente`, `WHERE vigente_hasta IS NULL`) impide dos planes vigentes a la vez; el historial queda en las filas cerradas. |
| `saas.tenants.modo_suspension` | `'lectura'` (por defecto: suspender es dejar en solo lectura) o `'bloqueo'`, por tenant (D53). |
| `CK_tenants_interno_activo` | El tenant de tipo `interno` (la propia plataforma, ADR-006) no puede suspenderse ni cancelarse. |

Datos sembrados (se leen de `dbo.modulos` al correr el script, no se escriben a mano):

| Plan | Módulos | `limite.usuarios` | `limite.empresas` | Tenants |
|---|---|---|---|---|
| `interno` | los 8 de `dbo.modulos` | sin tope | sin tope | SIP |
| `demostracion` | `flota`, `catalogos` | 10 | 2 | DEMO |
| `flota` | `flota` | 50 | 5 | TRANSGAR, IISE |

Cada plan tiene su versión 1, vigente desde 2026-10-15. Los tenants se buscan por `saas.tenants.codigo`, nunca por id. En total: 10 derechos (8 módulos y los 2 límites, D54), 3 planes, 3 versiones, 17 derechos de versión y 4 asignaciones (motivo `script:040`).

### Decisiones de diseño donde la especificación no alcanzaba

1. **`plan_version_derechos.tipo_derecho`** (columna extra) repite el tipo del derecho y va en una clave foránea compuesta `(id_derecho, tipo_derecho) → derechos(id_derecho, tipo)`. Así «`valor_limite` solo para los límites» lo exige la base con una restricción (`CK_plan_version_derechos_valor`) sin disparadores, y el tipo de un derecho en uso no se puede cambiar.
2. **Lectura de `dbo.empresa_modulos` tenant por tenant.** Con la política RLS **encendida** (como está producción) esa tabla no devuelve filas a una sesión sin tenant, y «nadie pierde acceso» habría pasado sin mirar nada. El PRECHECK y el POSTCHECK recorren los tenants fijando `SESSION_CONTEXT('id_tenant')` y lo dejan en NULL al terminar (también si hay error). Funciona igual con la política apagada.
3. **El PRECHECK compara contra el contenido sembrado; el POSTCHECK, contra lo que quedó en la base** (por si un plan ya existía y el operador lo había editado). Un tenant que ya tiene plan vigente no se toca ni se vuelve a comprobar.
4. **Todo o nada.** Las 5 tablas, la columna y las 2 restricciones de `saas.tenants` (8 objetos) existen todos o ninguno; a medias, el PRECHECK aborta y los lista. Un tenant que no está en el reparto (SIP, DEMO, TRANSGAR, IISE) y no tiene plan aborta el script: hay que decidirle el plan.
5. **Límites contados:** usuarios activos (`Users.IsActive = 1`) cuya empresa (no eliminada) pertenece al tenant, y empresas no eliminadas del tenant.
6. **Sin `token_concurrencia`** en las tablas nuevas (el estándar lo reserva a tablas transaccionales). Se agrega con un `ALTER` el día que la consola del operador (F1.9) edite planes en concurrencia.
7. **Cambiar el tipo del tenant interno a otro** sigue siendo posible (es la salida explícita para retirarlo); lo que `CK_tenants_interno_activo` impide es suspenderlo o cancelarlo mientras es interno.

## Archivos

| Archivo | Para qué sirve |
|---|---|
| `2 - Script SQL/040_planes_derechos_estado_tenant.sql` | El cambio: PRECHECK, CAMBIO y POSTCHECK en una transacción, idempotente, con ROLLBACK comentado. **Aplicado en producción el 2026-10-24** (46 de 46 OK con `verificar_despues_f17.sql`). |
| `verificar_despues_f17.sql` | Comprobación de solo lectura del estado posterior a 040. 46 comprobaciones (`OK` o `FALLA` más la fila `RESUMEN`), mismo formato que los demás. |
| `2 - Script SQL/emergencia_derechos_abrir.sql` | Emergencia: deja a todos los tenants activos y con el plan `interno` vigente. |
| `LEEME_F17.md` | Este archivo. |

## Cuándo correr la verificación

Con el ejecutor de siempre (ver `LEEME.md`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\ejecutar_verificacion.ps1 -Archivo verificar_despues_f17.sql -Destino Produccion
```

| Momento | Se espera |
|---|---|
| Justo **después** de aplicar 040 | todo OK (46) |
| Después de cualquier edición de planes, límites o asignaciones | las filas «invariantes» siguen OK; las de «valores sembrados» (planes, módulos y topes de cada plan, reparto de tenants) dejan de coincidir si se editó lo sembrado: es lo esperado, se ajusta el número y se anota |
| **Antes** de aplicar 040 | FALLA en casi todo (no existen las tablas): sirve para confirmar que 040 aún no se aplicó |
| Después de correr `emergencia_derechos_abrir.sql` | FALLA solo en «Reparto de 040» (los tenants quedan todos en `interno`); lo demás sigue OK |

Duración medida en LocalDB: unos 3 s (la primera ejecución tras restaurar una base puede tardar más). Contra Somee se suma la latencia de red.

### Límite conocido: la cobertura de módulos con la política RLS encendida

La fila «Modulos activos de empresas fuera del plan de su tenant» lee `dbo.empresa_modulos`. El ejecutor **no fija tenant en la sesión** (es lo que prueba `verificar_despues_ventana_3.sql`), así que con la política encendida esa tabla aparece vacía y la fila no puede encontrar nada. Para que no pase por alto, su texto dice cuántas filas pudo ver:

- `(filas visibles de empresa_modulos: 6 de 6)` → la comprobación vale (política apagada, o ensayo en LocalDB).
- `(filas visibles de empresa_modulos: 0 de 6; NO VERIFICABLE desde esta conexion sin tenant, ver el PRECHECK de 040)` → la fila sale `OK` pero **no miró nada**; la cobertura la comprobaron el PRECHECK y el POSTCHECK del script 040, que leen tenant por tenant. Las filas de usuarios y empresas por tenant sí se ven siempre (`empresas` no filtra la lectura y `Users` no lleva RLS).

## Efecto sobre los verificadores históricos

No se modificaron. Después de 040 cambian tres números que ellos fijan, y **es lo esperado** (para entonces corresponde `verificar_despues_f17.sql`):

| Archivo | Fila | Esperaba | Después de 040 |
|---|---|---|---|
| `verificar_despues_ventana_2.sql` | Objetos propios del esquema saas (solo la tabla tenants) | 1 | 6 |
| `verificar_despues_ventana_2.sql` | Claves foráneas hacia saas.tenants (`FK_<tabla>_tenants`) | 41 | 42 (`FK_tenant_planes_tenants`) |
| `verificar_despues_ventana_2.sql` | Claves foráneas en total (125 + 41 + 78 − 92) | 152 | 157 |

`verificar_despues_ventana_3.sql` (política encendida) no depende de esos números: ver los resultados de abajo. Si hubo que correr `emergencia_rls_apagar.sql` **después** de 040, la comprobación de que la política quedó apagada es la fila «Política APAGADA» de `verificar_despues_ventana_2.sql` (las otras tres filas de arriba seguirán en FALLA por diseño) o la fila «Politica seg.pol_tenant intacta» de `verificar_despues_f17.sql`, que indica si está encendida o apagada.

## El script de emergencia

`2 - Script SQL/emergencia_derechos_abrir.sql` se corre **solo si Emilio lo pide en el chat**, de inmediato, si un dato mal sembrado o editado deja a alguien sin acceso a lo que ya usaba. No espera `/alerta-bd` (como `emergencia_rls_apagar.sql`) y no borra nada. Hace, en este orden y de forma idempotente:

1. Deja **todos** los tenants en estado `activo` (el estado y el motivo anteriores quedan escritos en `motivo_estado`; no toca `modo_suspension`). Esto incluye al tenant IISE, que 025 dejó suspendido, y a los cancelados: después hay que suspender de nuevo los que correspondan.
2. Agrega al plan `interno` los derechos que le falten (un `modulo.<codigo>` por cada fila de `dbo.modulos`, `limite.usuarios` y `limite.empresas`), le quita los topes a sus límites y lo reactiva si estaba inactivo.
3. **Cierra** (`vigente_hasta`) la asignación vigente de los tenants que no están en `interno` y abre una nueva con ese plan. Cada asignación anterior queda como fila cerrada con su motivo.

Si 040 no está aplicado, solo hace el paso 1. Para deshacerlo: reasignar a mano los planes y suspender de nuevo los tenants que correspondan.

## Cómo se ensayó

Sobre la copia real de producción restaurada en LocalDB (025 a 036, política **apagada**) y la misma con 037 (política **encendida**, que es como está producción hoy). Cada estado: restaurar, aplicar 040, verificar, repetir 040 (idempotencia) y ejecutar la sección ROLLBACK copiada a un archivo temporal.

Resultados del ensayo del 2026-10-18 (LocalDB, sobre la copia de producción tras el script 036):

- **Orden de aplicación:** 040 antes y después de 038 y 039 (F1.5). En los dos órdenes, y con la política encendida o apagada, los scripts se aplican sin errores en menos de un segundo cada uno y los verificadores dan todo OK donde corresponde (46 de 46 para `verificar_despues_f17.sql`, con la política encendida). Por eso el PRECHECK de 040 acepta 199 o 204 predicados y el verificador suma las cifras de F1.5 si ya están.
- **Idempotencia:** repetir 040 dice «Nada que hacer» y no cambia nada.
- **ROLLBACK:** deja la base como estaba (56 tablas en `dbo`, 1 en `saas`, 152 claves foráneas, 199 predicados, 13 columnas en `saas.tenants`).
- **El PRECHECK aborta sin cambiar nada** si Transgar tuviera activo el módulo `ventas`, que su plan no incluye (probado con la política encendida: lo detecta porque lee `empresa_modulos` tenant por tenant).
- **El tenant interno no se puede suspender:** `CK_tenants_interno_activo` rechaza el cambio.
- **Emergencia:** con Transgar suspendido, en bloqueo y sin plan vigente, `emergencia_derechos_abrir.sql` lo deja activo con el plan `interno` (y reactiva también a IISE, como se explica arriba).

No se ensayaron aún todas las ediciones a mano del verificador. Contra producción no se ha ejecutado nada.

## El código de F1.7 y el orden de publicación

El código que usa estas tablas está en `Services/Derechos/` (decisiones en [ADR-026](../../Arquitectura/ADR-026-derechos-de-uso-y-estado-del-tenant.md)): `DerechosDeUso` (lee plan, derechos, estado y módulos activos; recuerda 30 s), `EstadoDelTenantMiddleware` (bloquea suspendidos y cancelados; el administrador general nunca), `DerechoDeModuloPageFilter` (cada carpeta de módulo exige su derecho) y la página `/SinAcceso`. `Empresas/Create` y `Admin/Usuarios/Create` aplican `limite.empresas` y `limite.usuarios`; `/Admin/Soporte` muestra plan y modo de cada tenant.

**El script 040 se aplica ANTES de publicar este código**: la aplicación lee `saas.planes`, `saas.tenant_planes` y `saas.tenants.modo_suspension`, y sin ellas la aplicación falla en cuanto lee los derechos del tenant (casi cada petición de un usuario de tenant). Al revés (040 aplicado con el código anterior) no pasa nada: el código anterior ignora las tablas y la columna nuevas. Pasos: `/alerta-bd` → 040 → `verificar_despues_f17.sql` (46 OK) → publicar → mirar el log (`no tiene un plan vigente` no debe aparecer).

Recorrido de la aplicación del 2026-10-18 sobre una copia con 025 a 037 + 040 y la política encendida (servidor local `localhost,1433`): los cinco comportamientos de ADR-026, más que el administrador general entra a un tenant en bloqueo y que `/Admin/Soporte` muestra el plan. Lo único que no se probó es la publicación misma.

## Límites y requisitos

- **Nivel de compatibilidad ≥ 140** (`STRING_AGG`), igual que los demás.
- **Permisos**: crear tablas en `saas`, alterar `saas.tenants` y leer `sys.*`, `dbo.modulos`, `dbo.empresas`, `dbo.Users` y `dbo.empresa_modulos`. El usuario de la aplicación, dueño de la base, los tiene.
- **Opciones de sesión**: `saas.tenants` y `saas.tenant_planes` tienen índices filtrados, así que quien escriba en ellas necesita `QUOTED_IDENTIFIER ON` y las demás opciones de siempre (las conexiones de SqlClient y de EF ya las traen; el script y la emergencia las fijan al principio).
- **Las listas están dentro de los `.sql`** (nombres de claves, índices y restricciones, módulos y topes sembrados, reparto de tenants). `ScriptsPlanesTests` vigila que el script, el verificador y el arnés (`ClasificacionDeTablas`: las 5 tablas son de plataforma) digan lo mismo.
- Los `.sql` son ASCII (sin tildes ni eñes), como los scripts de migración.
