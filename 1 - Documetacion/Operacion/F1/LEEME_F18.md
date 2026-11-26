# Verificación y operación de F1.8a (auditoría de seguridad y de plataforma)

Complemento de [`LEEME.md`](LEEME.md). Este archivo cubre el **script 045** (`2 - Script SQL/045_auditoria_seguridad.sql`), su verificación de solo lectura, el orden de publicación y cómo consultar la auditoría mientras no existan las pantallas (F1.8c). Las decisiones están en [ADR-030](../../Arquitectura/ADR-030-auditoria-de-seguridad.md) (D70 a D76, aceptadas el 2026-11-18).

> **Estado:** el script 045 está **aplicado en producción desde el 2026-11-19** (con la aprobación de `/alerta-bd`): PRECHECK correcto, 2 tablas, 5 predicados y 2 columnas nuevas, ninguna fila existente cambió, y `verificar_despues_045.sql` dio 14 de 14 OK. El código de F1.8a se publicó el mismo día. F1.8b (solo código) va en el kit de `dev@b50a167`, con los catálogos de Psicología; F1.8c (pantallas y script 046) está en el código, con el 046 sin aplicar (sección al final).

## Qué crea el script 045

| Objeto | Qué es |
|---|---|
| `dbo.auditoria_seguridad` | Tabla de **tenant**: lo que ocurre dentro de un tenant (hoy `contexto.elegido`; desde F1.8b también los cambios de administración y el soporte). `id_tenant` obligatorio, clave foránea a `saas.tenants` y 5 predicados en `seg.pol_tenant` (1 filtro y 4 bloqueos, con `seg.fn_tenant_actual`, como las demás tablas de tenant). |
| `saas.auditoria_plataforma` | Plano de control, **sin RLS** (ADR-013): entradas, fallos, bloqueos, segundo factor, claves e invitaciones aceptadas, y las acciones del operador. `id_tenant` es el tenant sobre el que se actuó, puede faltar y no filtra nada. |
| `TR_auditoria_seguridad_inmutable`, `TR_auditoria_plataforma_inmutable` | Disparadores `INSTEAD OF UPDATE, DELETE`: ninguna fila se modifica ni se borra, igual que `TR_bitacora_cambios_inmutable`. |
| `dbo.bitacora_cambios.id_cuenta`, `.id_correlacion` | Dos columnas **nulas**: quién hizo el cambio (cuenta global) y con qué petición. Las filas anteriores quedan sin ellas. |

Las dos tablas tienen las mismas 15 columnas (`id_evento`, `fecha_hora`, `tipo`, `resultado`, `id_cuenta`, `usuario`, `es_operador`, `id_tenant`, `id_empresa`, `objeto_tipo`, `objeto_id`, `detalle`, `ip`, `agente`, `id_correlacion`), 3 restricciones CHECK (resultado, forma `grupo.accion` del tipo y detalle JSON) y sus índices de consulta (4 en la de tenant, 5 en la de plataforma). **No llevan clave foránea a cuentas ni a empresas a propósito**: la auditoría nunca debe bloquear ni verse afectada por cambios en otras tablas.

El script **solo agrega**: no cambia ninguna fila ni objeto existente (salvo ampliar `seg.pol_tenant` y `dbo.bitacora_cambios`). Es idempotente y funciona con la política encendida o apagada, y la deja como estaba.

## Archivos

| Archivo | Para qué sirve |
|---|---|
| `2 - Script SQL/045_auditoria_seguridad.sql` | El cambio: PRECHECK, CAMBIO y POSTCHECK en una transacción, idempotente, con ROLLBACK comentado. **Aplicado en producción el 2026-11-19.** |
| `verificar_despues_045.sql` | Comprobación de solo lectura del estado posterior a 045. 14 comprobaciones (`OK` o `FALLA` más la fila `RESUMEN`) y una fila informativa con las filas reales de cada tabla. |
| `LEEME_F18.md` | Este archivo. |

## Cuándo correr la verificación

Con el ejecutor de siempre (ver `LEEME.md`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\ejecutar_verificacion.ps1 -Archivo verificar_despues_045.sql -Destino Produccion
```

| Momento | Se espera |
|---|---|
| Justo **después** de aplicar 045 | 14 de 14 OK (producción, 2026-11-19) |
| **Antes** de aplicar 045 | FALLA en casi todo (no existen las tablas): sirve para confirmar que 045 aún no se aplicó |
| Cualquier día después | 14 de 14 OK. Con filas reales en la tabla de tenant, la fila 360 confirma que la política las oculta a una conexión sin tenant (prueba concluyente de la RLS) |

La **comprobación diaria** (`comprobacion_diaria.ps1`) corre este verificador sola en cuanto existe `dbo.auditoria_seguridad`, y no hace falta tocarla.

### Efecto sobre los verificadores anteriores

`verificar_despues_f15.sql` y `verificar_despues_f17.sql` aceptan las cifras con o sin el 045 (suman +1 tabla en `dbo`, +1 columna `id_tenant`, +5 predicados, +2 claves foráneas en total, +2 hacia `saas.tenants`, +1 tabla en `saas`), como hicieron con el 041 y el 042. Los verificadores históricos de la salida de F1 (`ventana_1` a `ventana_3`) no se modifican.

## Orden de publicación

1. `/alerta-bd` y aprobación expresa de Emilio. **Hecho el 2026-11-19.**
2. Aplicar el 045 con el ejecutor de siempre, **antes** de publicar el código. **Hecho el 2026-11-19.**
3. `verificar_despues_045.sql` (14 OK) y la comprobación diaria. **Hecho el 2026-11-19: 14 de 14.**
4. Publicar el código de F1.8a. **Hecho el 2026-11-19.**

**El 045 va ANTES del código**: el interceptor de la bitácora escribe `id_cuenta` e `id_correlacion` en cada cambio de personas, de modo que sin las columnas ese guardado falla. Al revés (045 aplicado con el código anterior) no pasa nada: el código anterior ignora las tablas y las columnas nuevas.

Los eventos de entrada en sí son de **mejor esfuerzo** (D73): si la tabla no existiera o la escritura fallara, la persona entra igual y el error queda en el log (`Auditoría: no se pudo registrar el evento …`). Después de publicar, ese mensaje no debe aparecer.

## Qué se registra hoy (F1.8a)

| Tipo | Cuándo | Va a |
|---|---|---|
| `sesion.entrada` | Entrada correcta; y cada intento fallido (clave incorrecta, cuenta bloqueada, cuenta inactiva) | Plataforma |
| `sesion.salida` | Cierre de sesión | Plataforma |
| `sesion.bloqueo` | Una cuenta queda bloqueada por fallos seguidos | Plataforma |
| `mfa.alta`, `mfa.verificada`, `mfa.recuperacion`, `mfa.desactivada` | Alta, código correcto o equivocado, uso de un código de recuperación (con cuántos quedan) y desactivación | Plataforma |
| `clave.cambiada`, `clave.restablecida` | Cambio de clave en la pantalla y restablecimiento con código | Plataforma |
| `invitacion.aceptada` | Una persona acepta su invitación | Plataforma |
| `contexto.elegido` | Entrada a un sitio o cambio de sitio | Tenant (o plataforma si el operador entra sin empresa) |

Los intentos con **una cuenta que no existe** dejan una sola fila por dirección y por hora, con el nombre enmascarado (`ma***@e***.hn`) y, en la siguiente fila, cuántos se omitieron; un tope por hora protege la tabla de un ataque desde muchas direcciones. **Nunca** se guardan contraseñas ni su hash, códigos de segundo factor o de recuperación, tokens de invitación, cadenas de conexión ni documentos completos (D72).

## Consultar la auditoría (solo lectura)

Hasta que lleguen las pantallas de F1.8c, la consulta es por SQL. La tabla de plataforma se lee sin más; la de tenant solo la devuelve una conexión **con** el tenant fijado (la RLS le oculta todo a una conexión sin tenant, que es lo que debe pasar).

```sql
-- Últimos 50 eventos de cuentas y del operador
SELECT TOP 50 fecha_hora, tipo, resultado, usuario, id_cuenta, id_tenant, ip, detalle
FROM saas.auditoria_plataforma ORDER BY id_evento DESC;

-- Fallos de entrada de las últimas 24 horas, por dirección
SELECT ip, COUNT(*) AS filas, MAX(fecha_hora) AS ultimo
FROM saas.auditoria_plataforma
WHERE tipo = 'sesion.entrada' AND resultado = 'fallo' AND fecha_hora > DATEADD(HOUR, -24, SYSUTCDATETIME())
GROUP BY ip ORDER BY filas DESC;

-- Todo lo de una petición (el mismo valor de X-Correlation-Id que sale en los logs)
SELECT fecha_hora, tipo, resultado, usuario FROM saas.auditoria_plataforma WHERE id_correlacion = '...';
```

Para saber cuántas filas hay sin pasar por la RLS, `verificar_despues_045.sql` informa las filas reales de cada tabla (`sys.partitions`).

## Cómo se ensayó

Sobre copias locales de producción (servidor `localhost,1433`, bases `egestion_*`): aplicar el 045, repetirlo (idempotencia: «Nada que hacer»), y una batería funcional con dos tenants: insertar en el tenant propio, que la política rechace insertar en otro, que cada tenant vea solo lo suyo y una conexión sin tenant no vea nada, que `UPDATE` y `DELETE` fallen en las dos tablas, que se rechacen un resultado inválido, un tipo sin forma `grupo.accion`, un detalle que no es JSON y un tenant inexistente, y que la bitácora acepte las columnas nuevas. También las rutas del ROLLBACK (se detiene con filas en las tablas o con datos en las columnas nuevas, y limpio las quita).

El verificador `verificar_despues_045.sql` dio 14 de 14 OK en la copia de ensayo. En la copia de desarrollo (identidad global, planes y permisos, 038 a 041, con el 045 puesto), `verificar_despues_f17.sql` da 46 OK y `verificar_despues_041.sql` 24 OK: sus cifras de objetos aceptan el 045. `verificar_despues_f15.sql` da 53 OK y 6 FALLA, todas sobre los **datos** de las cuentas de esa copia (cuentas activas e inactivas, claves, membresías), ninguna sobre cifras de objetos. El 2026-11-19 se aplicó en producción tras la línea base de la comprobación diaria; después de aplicar, la comprobación diaria dio lo mismo que antes (solo la alerta conocida de las claves de `verificar_despues_f15.sql`) y los verificadores de F1.7 y F1.6 siguieron en OK.

## F1.8b: administración, soporte y acciones del operador

**Solo código: no hay script ni `/alerta-bd`.** Usa las tablas del 045, que ya están aplicadas. Se publica con el kit de siempre y su reversa (`REVERTIR.bat`) no toca la base: las filas ya escritas se conservan y el código anterior las ignora.

Qué deja ahora una fila (ver [ADR-030](../../Arquitectura/ADR-030-auditoria-de-seguridad.md), sección F1.8b):

| Grupo | Tipos | Tabla |
|---|---|---|
| Administración | `usuario.creado`, `usuario.editado`, `usuario.eliminado`, `rol.creado`, `rol.editado`, `rol.eliminado`, `rol.permisos`, `modulo.cambiado`, `acceso.cambiado`, `empresa.creada`, `empresa.editada`, `empresa.eliminada` | Tenant; y plataforma si lo hace el personal de la plataforma o no tiene tenant |
| Invitaciones y accesos denegados | `invitacion.enviada`, `invitacion.anulada`, `acceso.denegado` | Tenant (plataforma si no hay tenant) |
| Soporte | `soporte.entrada` (estricta), `soporte.salida` | Las dos |
| Plataforma | `correo.configurado`, `tasas.configurada`, `tasas.ejecutada`, `tasas.oficial_aprobada`, `tasas.oficial_rechazada`, `cola.reintento`, `cola.procesado_manual` | Plataforma |

Los cambios de administración quedan **en la misma transacción** que el cambio: si la fila de auditoría no se puede dejar, el cambio no se guarda. La entrada al soporte de un tenant también: si no se puede dejar el rastro, no se entra.

### Consultas de ejemplo (solo lectura)

```sql
-- Qué cambió el personal de la plataforma en un tenant, lo más reciente primero
SELECT TOP 50 fecha_hora, tipo, usuario, objeto_tipo, objeto_id, detalle
FROM saas.auditoria_plataforma WHERE id_tenant = 5 AND es_operador = 1 ORDER BY id_evento DESC;

-- Cambios de permisos de roles (lo concedido y lo retirado, por código)
SELECT fecha_hora, usuario, objeto_id AS id_rol, detalle FROM saas.auditoria_plataforma WHERE tipo = 'rol.permisos' ORDER BY id_evento DESC;
```

La auditoría de un tenant (`dbo.auditoria_seguridad`) solo la devuelve una conexión con ese tenant fijado, como el resto de las tablas de tenant.

### Qué verificar después de publicar

1. La comprobación diaria igual que antes (solo la alerta conocida de las claves).
2. Con una sesión de administrador, cambiar el rol de un usuario de prueba y ver su fila `usuario.editado` (con el antes y el después y sin la clave).
3. Con el operador, entrar a un tenant desde `/Admin/Soporte` y ver `soporte.entrada` en las dos tablas.

### Límites

Solo ve lo que se guarda con EF Core; un script SQL debe dejar su propia fila. `acceso.denegado` cubre las negaciones del filtro de permisos, no las de módulo o derechos del tenant. Las pantallas para consultar todo esto llegan en F1.8c.

## Límites conocidos

- `TRUNCATE TABLE` no dispara el disparador; solo quien puede modificar el esquema puede hacerlo (igual que en `dbo.bitacora_cambios`). En el hosting nuevo la aplicación entrará con un usuario sin ese permiso (ADR-016).
- La dirección de red es la de la conexión directa (Somee). Detrás de un proxy (Azure) habrá que configurar los encabezados reenviados en el paso de migración.
- Las dos tablas contienen datos personales (dirección, nombre) y no se pueden borrar; la conservación es de 24 meses sin purga automática (D75).
- Los cambios de administración, el soporte y las acciones del operador se auditan desde F1.8b; las pantallas para consultarlo son las de F1.8c (sección de abajo).
- El script es ASCII (sin tildes ni eñes), como los demás de migración.

## F1.8c: pantallas de auditoría y script 046

Dos pantallas de **solo lectura**, con exportación a Excel y una hoja para imprimir o guardar como PDF (decisiones del 2026-11-21 en [ADR-030](../../Arquitectura/ADR-030-auditoria-de-seguridad.md), sección F1.8c):

| Pantalla | Ruta | Quién la abre |
|---|---|---|
| Auditoría | `/Admin/Auditoria` | El administrador de la empresa, solo su tenant (no se puede dar a un rol), y el operador dentro del tenant que eligió en Soporte |
| Auditoría de la plataforma | `/Admin/AuditoriaPlataforma` | El personal de la plataforma |

El correo sale enmascarado en pantalla y en lo exportado. Cada exportación deja su fila (`auditoria.exportada` o `auditoria.plataforma_exportada`).

### Script 046 (con `/alerta-bd`)

`2 - Script SQL/046_permisos_auditoria.sql` agrega a `saas.permisos` `administracion.auditoria.ver` (empresa, orden 92) y `plataforma.auditoria.ver` (plataforma, orden 93). No toca roles ni plantillas. Se verifica con `verificar_despues_041.sql`, que acepta las cifras con o sin el 046:

| Comprobación | Sin el 046 | Con el 046 |
|---|---|---|
| 210 Permisos en el catálogo | 91 | 93 |
| 230 Por módulo: plataforma / administración / flota / catálogos / facturación | 17 / 9 / 28 / 8 / 10 | 18 / 10 / 28 / 8 / 10 |
| 240 Por ámbito: plataforma / empresa / módulo | 17 / 9 / 65 | 18 / 10 / 65 |

### Orden de publicación

1. Ensayar el 046 en la copia local de producción: aplicar, repetir («Nada que hacer») y revertir. **Hecho el 2026-11-23** en una copia con el catálogo de producción (91 permisos): aplicar, repetir, ROLLBACK, PRECHECK que falla, reaplicar, ROLLBACK detenido por la FK y verificadores 24 de 24 y 14 de 14. El ensayo encontró un `PRINT` con subconsulta en el PRECHECK (SQL Server no lo admite) y se corrigió; una prueba lo vigila en los scripts desde el 040.
2. `/alerta-bd` y aprobación expresa de Emilio; aplicar el 046 con el ejecutor de siempre. **Hecho el 2026-11-23** (4 lotes sin error; el catálogo pasó de 91 a 93 permisos, ids 92 y 93, `creado_por` = `script:046`).
3. `verificar_despues_041.sql` con las cifras de la columna «Con el 046» y la comprobación diaria. **Verificador hecho el 2026-11-23: 24 de 24 OK** (93 permisos; plataforma 18, administración 10, flota 28, catálogos 8, facturación 10, psicología 19). La comprobación diaria se corrió ese día antes y después de publicar: solo las 4 fallas ya conocidas de `verificar_despues_f15.sql` (la membresía 1 de SIP está suspendida desde el 2026-11-20).
4. Publicar el código de F1.8c. **Hecho el 2026-11-23 a las 21:05** desde `dev` `2edb621` (unos 5 minutos de mantenimiento). Sin sesión, las cuatro rutas de auditoría responden 302 al Login. Ningún rol de ningún tenant tenía un permiso de auditoría, ni antes ni después de publicar.

**El 046 va ANTES del código**: el evaluador lee el catálogo de la base, así que sin las filas nadie abre las pantallas nuevas (ni el operador). Al revés (046 aplicado con el código anterior) hay una ventana: la pantalla de roles del código anterior arma su lista con el catálogo de la base y ofrecería `administracion.auditoria.ver` como casilla de rol; el código nuevo no la ofrece ni la concede a un rol, pero una fila marcada en ese lapso quedaría inerte y la clave foránea frenaría el ROLLBACK. Por eso se aplica el 046 y se publica en la misma sesión, y después se comprueba en solo lectura (tenant por tenant, por la RLS) que ningún rol tiene el permiso.

### Qué verificar después de publicar

1. Como administrador de una empresa, abrir «Auditoría» desde el menú: se ven los eventos de su tenant de los últimos 7 días.
2. Exportar a Excel y abrir el archivo; luego «Imprimir o guardar como PDF». Cada uno deja su fila `auditoria.exportada`.
3. Como operador, abrir «Auditoría de la plataforma» y filtrar por un tenant.
4. En «Roles y Permisos», el permiso de auditoría no aparece para marcar.

**Resultado de la prueba con sesión (lectura del 2026-11-24 03:44 UTC):** el punto 2 quedó registrado en el tenant de CAPAE (filas `auditoria.exportada` de PDF y de Excel, ids 16 a 18, con el rango por defecto de 7 días). El punto 3 está **sin verificar**: no hay ninguna fila `auditoria.plataforma_exportada`. El punto 4 no deja rastro en la base y no está confirmado.

