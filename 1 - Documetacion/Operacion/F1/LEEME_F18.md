# Verificación y operación de F1.8a (auditoría de seguridad y de plataforma)

Complemento de [`LEEME.md`](LEEME.md). Este archivo cubre el **script 045** (`2 - Script SQL/045_auditoria_seguridad.sql`), su verificación de solo lectura, el orden de publicación y cómo consultar la auditoría mientras no existan las pantallas (F1.8c). Las decisiones están en [ADR-030](../../Arquitectura/ADR-030-auditoria-de-seguridad.md) (D70 a D76, aceptadas el 2026-11-18).

> **Estado:** el script 045 está **sin aplicar en producción**. Se ensayó solo en copias locales. Aplicarlo requiere la aprobación de `/alerta-bd`.

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
| `2 - Script SQL/045_auditoria_seguridad.sql` | El cambio: PRECHECK, CAMBIO y POSTCHECK en una transacción, idempotente, con ROLLBACK comentado. **Sin aplicar en producción.** |
| `verificar_despues_045.sql` | Comprobación de solo lectura del estado posterior a 045. 14 comprobaciones (`OK` o `FALLA` más la fila `RESUMEN`) y una fila informativa con las filas reales de cada tabla. |
| `LEEME_F18.md` | Este archivo. |

## Cuándo correr la verificación

Con el ejecutor de siempre (ver `LEEME.md`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\ejecutar_verificacion.ps1 -Archivo verificar_despues_045.sql -Destino Produccion
```

| Momento | Se espera |
|---|---|
| Justo **después** de aplicar 045 | 14 de 14 OK |
| **Antes** de aplicar 045 | FALLA en casi todo (no existen las tablas): sirve para confirmar que 045 aún no se aplicó |
| Cualquier día después | 14 de 14 OK. Con filas reales en la tabla de tenant, la fila 360 confirma que la política las oculta a una conexión sin tenant (prueba concluyente de la RLS) |

La **comprobación diaria** (`comprobacion_diaria.ps1`) corre este verificador sola en cuanto existe `dbo.auditoria_seguridad`, y no hace falta tocarla.

### Efecto sobre los verificadores anteriores

`verificar_despues_f15.sql` y `verificar_despues_f17.sql` aceptan las cifras con o sin el 045 (suman +1 tabla en `dbo`, +1 columna `id_tenant`, +5 predicados, +2 claves foráneas en total, +2 hacia `saas.tenants`, +1 tabla en `saas`), como hicieron con el 041 y el 042. Los verificadores históricos de la salida de F1 (`ventana_1` a `ventana_3`) no se modifican.

## Orden de publicación

1. `/alerta-bd` y aprobación expresa de Emilio.
2. Aplicar el 045 con el ejecutor de siempre, **antes** de publicar el código.
3. `verificar_despues_045.sql` (14 OK) y la comprobación diaria.
4. Publicar el código de F1.8a.

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

El verificador `verificar_despues_045.sql` dio 14 de 14 OK en la copia de ensayo. En la copia de desarrollo (identidad global, planes y permisos, 038 a 041, con el 045 puesto), `verificar_despues_f17.sql` da 46 OK y `verificar_despues_041.sql` 24 OK: sus cifras de objetos aceptan el 045. `verificar_despues_f15.sql` da 53 OK y 6 FALLA, todas sobre los **datos** de las cuentas de esa copia (cuentas activas e inactivas, claves, membresías), ninguna sobre cifras de objetos. **No se ejecutó nada contra producción.**

## Límites conocidos

- `TRUNCATE TABLE` no dispara el disparador; solo quien puede modificar el esquema puede hacerlo (igual que en `dbo.bitacora_cambios`). En el hosting nuevo la aplicación entrará con un usuario sin ese permiso (ADR-016).
- La dirección de red es la de la conexión directa (Somee). Detrás de un proxy (Azure) habrá que configurar los encabezados reenviados en el paso de migración.
- Las dos tablas contienen datos personales (dirección, nombre) y no se pueden borrar; la conservación es de 24 meses sin purga automática (D75).
- Lo que **todavía no** se audita llega en F1.8b (cambios de usuarios, roles, módulos, accesos y empresas; `rol.permisos`; soporte; acciones del operador) y las pantallas, en F1.8c.
- El script es ASCII (sin tildes ni eñes), como los demás de migración.
