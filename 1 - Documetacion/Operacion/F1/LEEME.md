# Verificaciones de solo lectura para la salida de F1 a producción

Comprobaciones que se corren **antes y después de cada ventana** de la salida de F1 (scripts 025 a 037). Todas son de **solo lectura**: devuelven una fila por comprobación con `OK` o `FALLA` y terminan con una fila `RESUMEN`. Si hay una sola `FALLA`, no se sigue con el paso siguiente hasta entenderla.

> Estas comprobaciones **no sustituyen** al PRECHECK/POSTCHECK de cada script SQL: los complementan desde afuera, con otra consulta, sobre el estado final de la base y con las cifras del plan (41 tablas, 18 claves únicas, 78 claves compuestas, 199 predicados).

## Archivos

| Archivo | Para qué sirve |
|---|---|
| `verificar_antes_ventana_1.sql` | Estado previo a la ventana 1: 0 esquemas `saas`/`seg`, 0 columnas `id_tenant`, 0 políticas, 56 tablas en `dbo`, 125 claves foráneas, las 4 empresas que espera el script 025 y las filas clave que no deben variar (empresas 4, Users 7, personas 97). 11 comprobaciones. |
| `verificar_despues_ventana_1.sql` | Tras 025 a 030: los 4 tenants (SIP interno, DEMO demo, IISE suspendido, TRANSGAR cliente) y que cada empresa cuelga del suyo; `id_tenant` (nulable), `FK_<tabla>_tenants` e `IX_<tabla>_tenant` en las 41 tablas; **una fila por tabla** con las filas sin tenant y las que tienen un tenant distinto del de su empresa (FALLA si hay alguna). 65 comprobaciones. Es también la vigilancia diaria de la semana de espera. |
| `verificar_despues_ventana_2.sql` | Tras 031 a 036: `id_tenant` NOT NULL en las 39 tablas no mixtas y nulable en las 2 mixtas, 18 claves únicas, 78 claves compuestas `FKT_`, ninguna clave simple remanente entre tablas de tenant, y la política `seg.pol_tenant` con 199 predicados y `is_enabled = 0`. 32 comprobaciones. |
| `verificar_despues_ventana_3.sql` | Tras 037: política encendida con 199 predicados; sin contexto de sesión cada una de las 37 tablas de tenant devuelve 0 filas; `empresas` y `domain_events` siguen devolviendo todas sus filas; las mixtas solo muestran las globales. 48 comprobaciones. |
| `ejecutar_verificacion.ps1` | Ejecutor de solo lectura (Windows PowerShell 5.1, `System.Data.SqlClient`). Corre uno de los `.sql` contra producción o contra LocalDB y devuelve el código de salida. |
| `LEEME.md` | Este archivo. |

Cada `.sql` es **del estado exacto de su ventana**, no de "al menos": por ejemplo `verificar_despues_ventana_1.sql` da FALLA después de la ventana 2 (ya no hay `id_tenant` nulable ni faltan las claves `FKT_`). Es a propósito: detecta un script aplicado fuera de orden.

## Cómo correrlo

Desde esta carpeta (`1 - Documetacion\Operacion\F1`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\ejecutar_verificacion.ps1 -Archivo verificar_antes_ventana_1.sql -Destino Produccion
```

- `-Archivo`: ruta o nombre del `.sql` (si es solo el nombre, se busca en esta carpeta).
- `-Destino Produccion`: la cadena de conexión sale de `dotnet user-secrets list --project <repo>` (solo se toma la línea `ConnectionStrings:DefaultConnection`, en memoria). **Nunca se imprime la cadena ni el resto de la lista**; en pantalla solo aparece el nombre de la base.
- `-Destino LocalDb -Base <nombre>`: servidor `(localdb)\MSSQLLocalDB` con autenticación de Windows. `-Base` es obligatorio con LocalDb y no se admite con Produccion. Sirve para ensayar sobre la copia restaurada antes del día 0.

Código de salida: `0` todo OK · `1` hay al menos una FALLA · `2` el ejecutor no pudo correr (SQL rechazado, sin conexión, error de SQL, sin fila RESUMEN; también los errores de parámetros que detecta el propio script).

### Qué correr en cada momento

| Momento | Archivo | Se espera |
|---|---|---|
| Ventana 1: sitio ya en mantenimiento, **antes** del primer script (025) | `verificar_antes_ventana_1.sql` | todo OK (11) |
| Ventana 1: **después** de 025 a 030 | `verificar_despues_ventana_1.sql` | todo OK (65) |
| Días 1 a 7, una vez al día, y al empezar la ventana 2 (después de exportar el respaldo nuevo) | `verificar_despues_ventana_1.sql` | todo OK; cualquier `N sin tenant` distinto de 0 es motivo para **no** contraer |
| Ventana 2: **después** de 031 a 035 y 036 | `verificar_despues_ventana_2.sql` | todo OK (32) |
| Ventana 3: **antes** de 037 (confirmar que la política sigue apagada) | `verificar_despues_ventana_2.sql` | todo OK |
| Ventana 3: **después** de 037 | `verificar_despues_ventana_3.sql` | todo OK (48) |
| Si hubo que correr `emergencia_rls_apagar.sql` | `verificar_despues_ventana_2.sql` | todo OK otra vez (política apagada) |

Si se corre `verificar_despues_ventana_2.sql` después de 035 pero **antes** de 036, fallan solo las 10 comprobaciones de la política, que es lo correcto.

Duración medida en LocalDB: 0,2 s (antes), 2 s (ventana 1), 2 s (ventana 2), 1 s (ventana 3). Contra Somee se suma la latencia de red.

## Garantías de solo lectura

- Los `.sql` solo tienen `SELECT`, `WITH` y `DECLARE` de variables escalares: ni `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `ALTER`, `CREATE`, `DROP`, `TRUNCATE`, `EXEC`/`EXECUTE`, `GRANT`, `REVOKE`, `DENY` ni tablas temporales.
- El ejecutor **rechaza** cualquier SQL que contenga esas palabras fuera de comentarios, cadenas `'...'` y nombres entre `[corchetes]`/`"comillas"`; además `BACKUP`, `RESTORE`, `DBCC`, `KILL`, `SHUTDOWN`, `RECONFIGURE`, `BULK`, `OPENROWSET`, `OPENQUERY`, `OPENDATASOURCE`, `OPENXML`, `sp_*`, `xp_*` y `SELECT ... INTO` una tabla que no sea `#temporal`. Si rechaza, no ejecuta nada y sale con código 2.
- Corre todos los lotes en **una sola conexión** y dentro de una **transacción que siempre se revierte** (red de seguridad extra).
- **No fija `SESSION_CONTEXT`**: la conexión no tiene tenant. Eso es lo que usa `verificar_despues_ventana_3.sql` como prueba de que la política está activa.

## Resultados de las pruebas (LocalDB, 2026-10-14)

Estados probados, cada uno en una base de LocalDB creada para la prueba y borrada al terminar:

- **A**: la copia real previa a F1 (`egestion_copia_real`, sin ningún script).
- **B**: A + 025 a 030.
- **B+**: B con 4 filas dañadas a mano (1 de `cargos` y 1 de `persona_documentos` con `id_tenant` nulo, 1 de `vehiculos` con el tenant de otra empresa, 1 de `tasas_cambio` con empresa y sin tenant).
- **C0**: A + 025 a 035 (`ensayo_f14_base.bak`), sin 036.
- **C**: C0 + 036 (política apagada). **C'**: lo mismo, pero aplicando 025 a 036 uno por uno desde A (da exactamente igual que C).
- **D**: C + 037 (política encendida).

Cada celda es `OK / FALLA` (sin contar la fila RESUMEN). **Negrita** = estado al que corresponde el archivo (código de salida 0); todo lo demás sale con código 1.

| Archivo (comprobaciones) | A | B | B+ | C0 | C | C' | D |
|---|---|---|---|---|---|---|---|
| `verificar_antes_ventana_1.sql` (11) | **11 / 0** | 7 / 4 | 7 / 4 | 7 / 4 | 6 / 5 | 6 / 5 | 5 / 6 |
| `verificar_despues_ventana_1.sql` (65) | 21 / 44 | **65 / 0** | 61 / 4 | 61 / 4 | 59 / 6 | 59 / 6 | 59 / 6 |
| `verificar_despues_ventana_2.sql` (32) | 4 / 28 | 13 / 19 | 13 / 19 | 22 / 10 | **32 / 0** | **32 / 0** | 31 / 1 |
| `verificar_despues_ventana_3.sql` (48) | 21 / 27 | 21 / 27 | 21 / 27 | 21 / 27 | 23 / 25 | 23 / 25 | **48 / 0** |

Lo que muestran las FALLA fuera de su estado:

- En **B+** `verificar_despues_ventana_1.sql` marca exactamente las 4 filas dañadas: `cargos` y `persona_documentos` ("1 sin tenant"), `vehiculos` ("1 incoherentes") y `tasas_cambio` ("1 sin tenant").
- En **D**, `verificar_despues_ventana_2.sql` falla solo en "Política APAGADA (is_enabled = 0)": es la única diferencia entre la ventana 2 y la 3.
- En **C0** `verificar_despues_ventana_2.sql` falla solo en las 10 filas de la política (esquema `seg`, funciones, predicados): las claves y las columnas de 031 a 035 están bien.
- En **C**, `verificar_despues_ventana_3.sql` falla en la política apagada y en las tablas con filas que siguen visibles (23 con datos); las tablas vacías dan OK porque no prueban nada, y por eso existe la fila de cobertura.
- Alteración a mano de C' (se quita `FKT_cargos_empresas_id_empresa` y se agrega una clave simple `cargos -> empresas`): `verificar_despues_ventana_2.sql` da 3 FALLA (77 claves compuestas, 77 con `id_tenant` primero y 1 clave simple remanente, que nombra).

Pruebas del ejecutor (`ejecutar_verificacion.ps1`):

| Caso | Resultado |
|---|---|
| SQL con `UPDATE`, `EXEC`, `sp_executesql` suelto, `SELECT ... INTO dbo.x`, `DROP` tras un comentario `--`, `DELETE` tras un comentario `/* */`, `insert` en minúsculas, `ALTER` tras una cadena, `CREATE` tras un `[delete]` | rechazado, código 2, no ejecuta nada |
| SQL que solo nombra esas palabras dentro de comentarios y cadenas, o columnas como `create_date` | aceptado |
| `-Destino LocalDb` sin `-Base`; `Produccion` con `-Base`; destino inválido; archivo inexistente; base inexistente; error de SQL; SQL sin fila RESUMEN | código 2 con mensaje |
| `-Destino Produccion` con un `dotnet` de mentira que devuelve una cadena hacia LocalDB y otra línea de "secreto" | funciona; ni la cadena, ni el servidor, ni el otro secreto aparecen en la salida; sin la línea esperada da error claro |

La ruta `Produccion` se probó **sin tocar producción** (ver arriba); ningún archivo se ejecutó contra eBD_SPD.

## Discrepancias entre el plan y lo que hay en los scripts

Los números del plan cuadran, pero hay cuatro matices que conviene saber (los `.sql` usan los números reales):

1. **"18 claves únicas" son 19 índices únicos que empiezan por `id_tenant`.** 031 a 035 crean 18 (`UX_<tabla>_tenant_...`, sin filtro); además 032 reconstruye `UX_persona_documentos_identidad` (con filtro `eliminado = 0`) para que empiece por `id_tenant`. Se comprueban las 18 por nombre, el total sin filtro y, aparte, la de identidad.
2. **Claves foráneas: 125 → 166 → 152, no 125 + 78.** Después de 025 a 030 hay 166 (125 + 41 hacia `saas.tenants`). 031 a 035 reemplazan **92** claves simples entre tablas de tenant por **78** compuestas: 14 eran duplicados (EF y el esquema manual declararon la misma relación, por ejemplo `FK_vehiculos_ruta` y `FK_vehiculos_rutas_id_ruta`). Total final: 125 + 41 + 78 − 92 = **152**. Cambian a "sin acción" 4 borrados "poner en nulo" y se conservan los 6 en cascada.
3. **`id_tenant` aparece en 42 columnas de la base, no 41:** las 41 tablas de `dbo` más `saas.tenants.id_tenant`. El estado previo "0 columnas" sí es 0. Las comprobaciones cuentan por separado las de `dbo` (41).
4. **El estado "después de la ventana 1" es exacto.** Eso incluye `id_tenant` nulable en las 41 tablas, 166 claves foráneas y el esquema `seg` vacío. Esas filas darán FALLA el día que se aplique 031 en adelante; no son un error: para entonces corresponde `verificar_despues_ventana_2.sql`.

Además, en los datos de hoy: las 16 filas de `tasas_cambio` son globales y `sen_boletines` tiene 0 filas, así que las dos mixtas **no distinguen** política apagada de encendida. Lo que prueba que la política está activa son las tablas de tenant con filas (23 en la copia: `odometro_diario`, `cargas_combustible`, `personas`, etc.) y `is_enabled = 1`.

## Límites y requisitos

- **Nivel de compatibilidad ≥ 140** (usan `STRING_AGG`, igual que el resultado final del script 025) y las funciones JSON. SQL Server 2022 de Somee lo cumple; si la base tuviera un nivel menor, 025 también fallaría.
- **Permisos**: ver el catálogo (`sys.security_predicates`, `sys.partitions`, etc.) y leer las tablas. El usuario de la aplicación, dueño de la base, los tiene.
- **Cuentas por tabla sin nombrar `id_tenant`**: en `verificar_despues_ventana_1.sql` y `_3.sql` las cuentas de filas leen cada fila como JSON (`FOR JSON`) y buscan la propiedad `id_tenant`. Así el archivo da FALLA, en vez de un error de compilación, si se corre antes de que la columna exista. Cuesta unos segundos con las tablas de hoy (8 066 filas la mayor); si alguna tabla creciera a cientos de miles de filas, conviene reemplazar esas cuentas por `WHERE id_tenant IS NULL` directo.
- **Valores que cambian con el uso**: `verificar_antes_ventana_1.sql` fija empresas 4, Users 7, personas 97 (las `DECLARE` del principio). Si hubo un alta legítima desde el ensayo, se ajusta el número y se anota. Los otros tres archivos no dependen de cifras de uso.
- **`domain_events` en la ventana 3**: con el sitio en línea el despachador puede cambiar la tabla entre las dos lecturas; si solo esa fila da FALLA, se vuelve a correr.
- **Las listas de tablas están dentro de los `.sql`** (41 tablas y 18 nombres de claves únicas, tomadas de `ClasificacionDeTablas.cs` y de los scripts 031 a 035). Si una tabla nueva de tenant entra a F1, hay que agregarla aquí también; ninguna prueba automática lo vigila todavía.
- Los `.sql` son ASCII (sin tildes ni eñes), como los scripts de migración.
