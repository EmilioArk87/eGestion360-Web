# Verificación y reversa de F1.6a (catálogo de permisos y roles)

Complemento de [`LEEME.md`](LEEME.md), [`LEEME_F15.md`](LEEME_F15.md) y [`LEEME_F17.md`](LEEME_F17.md). Este archivo cubre el **script 041** (`2 - Script SQL/041_permisos_catalogo_y_roles.sql`) y su verificación de solo lectura (`verificar_despues_041.sql`). Decisiones: [ADR-029](../../Arquitectura/ADR-029-permisos-por-accion.md).

> **Aplicado en producción el 2026-11-09 a las 20:26** (aviso `/alerta-bd` aprobado; hash SHA-256 `60DD0176E41D35B30EC73F81ECA56AB657F9252F8B1F1499B16D0577426C04AD`): PRECHECK correcto, 4 tablas y 5 predicados RLS, 72 permisos, 9 plantillas con 102 permisos, 143 permisos migrados y 4 textos corregidos. `verificar_despues_041.sql` 24/24, `verificar_despues_f15.sql` 59/59, `verificar_despues_f17.sql` 46/46 y la comprobación diaria «TODO BIEN».
>
> **El 041 no cambia lo que ve ni lo que puede hacer nadie.** La aplicación publicada ignora las tablas nuevas; el evaluador de permisos del código nuevo está registrado pero nada lo llama hasta F1.6b. Orden obligatorio: **aplicar el 041 (con `/alerta-bd`) → verificar → recién entonces publicar el código que lee esas tablas.**

## Qué crea el script 041

| Objeto | Qué es |
|---|---|
| `saas.permisos` | Catálogo global (sin RLS): 72 permisos `modulo.recurso.accion`. Cada uno lleva su `ambito` (`plataforma`, `empresa` o `modulo`) y, si es de módulo, la `accion_legada` (ver, crear, editar o eliminar) que lo concedía en el sistema anterior. |
| `saas.roles_plantilla` y `saas.roles_plantilla_permisos` | 9 roles de partida (administrador, operador y consulta de flota, catálogos y facturación) con 102 permisos. Una clave compuesta obliga a que un permiso de plantilla sea del módulo de la plantilla. F1.9 los copia al crear un tenant. |
| `dbo.rol_permisos` | Permisos de cada rol de empresa. **Tabla de tenant**: `id_tenant`, clave compuesta al rol (`FKT_`) y 5 predicados en `seg.pol_tenant` (1 filtro y 4 bloqueos con `seg.fn_tenant_actual`). Un rol nunca recibe permisos de ámbito `plataforma` (`CK_rol_permisos_ambito`). |
| Migración | Cada casilla marcada de `dbo.empresa_rol_permisos` concede todos los permisos de ese módulo con esa casilla. `empresa_rol_permisos` **no se toca** (se conserva hasta F1.10). Un rol que ya tiene filas en `rol_permisos` no se vuelve a migrar. |
| `dbo.modulos` (D65) | Corrige el nombre o la descripción de catálogos, facturación y bancos si conservan el daño de tildes. Los códigos no cambian. |

## Qué se ensayó (copia de producción en el SQL Server local, con 037 a 040 y la política RLS encendida)

- **Aplicar:** `PRECHECK 041 correcto … permisos en el catálogo: 72 … deben tener los roles tras la migración: 143` y `CAMBIO 041 aplicado: 4 tabla(s) y 5 predicado(s) RLS; 72 permiso(s), 9 plantilla(s) con 102 permiso(s), 143 permiso(s) migrados y 4 texto(s) de módulos corregidos`.
- **Repetir:** `Nada que hacer` (idempotente). Tras editar a mano los permisos de un rol, repetir **no** los pisa.
- **Equivalencia:** los 7 pares (rol, módulo) de `empresa_rol_permisos` dan exactamente los permisos esperados (por ejemplo el rol «Consulta» de Transgar, solo ver, recibe 11 de flota; «Operador de Flota», ver/crear/editar, 26; «Administrador», las cuatro casillas, 28).
- **RLS:** una conexión sin tenant ve 0 filas de `dbo.rol_permisos` aunque tiene 143 reales.
- **Reversa:** el bloque ROLLBACK deja 0 tablas y 204 predicados; `empresa_rol_permisos` queda con sus 7 filas; volver a aplicar funciona.
- **Negativos:** el ROLLBACK se detiene si hay permisos de rol que no son del script; el PRECHECK aborta si los objetos existen a medias.
- **Verificadores:** `verificar_despues_041.sql` 24 OK con el 041 y 24 FALLA sin él; `verificar_despues_f15.sql` (59) y `verificar_despues_f17.sql` (46) dan todo OK con y sin el 041.

## Cómo verificar en producción (solo lectura)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "1 - Documetacion\Operacion\F1\ejecutar_verificacion.ps1" -Archivo verificar_despues_041.sql -Destino Produccion
```

Debe terminar con `24 comprobaciones: 24 OK, 0 FALLA`. Las filas marcadas «valores sembrados» comprueban lo que sembró el 041 (72 permisos, 9 plantillas, 143 permisos migrados, 7 filas legadas); si alguien edita el catálogo o los roles después, esas filas dejarán de coincidir y es lo esperado. La comprobación diaria (`comprobacion_diaria.ps1`) corre este verificador sola cuando `dbo.rol_permisos` existe.

## Si hay que volver atrás

El bloque ROLLBACK al final del script quita los 5 predicados y las 4 tablas. Hay que pasarlo por `/alerta-bd` como cualquier cambio. No devuelve el daño de tildes de `dbo.modulos` (era un error, no un dato). Si ya hay permisos de rol editados a mano, se detiene para no perderlos.

## Hallazgos del ensayo que importan para F1.6d (revisión de los 7 roles)

1. **Transgar:** los usuarios del rol «Administrador» (`es_admin = 1`) tienen `rol_legado = empresa_user`. Hoy no administran usuarios, roles ni invitaciones; el evaluador decide por el texto del rol (como `AuthHelper`) para no darles esos poderes por sorpresa. Hay que decidir si pasan a `empresa_admin` o si el rol deja de ser `es_admin`.
2. El rol «Flota» de SIP (id 2) no tiene casillas; tras migrar sigue sin permisos.
3. Las 7 filas de casillas (no 6) son: Demo 2, la empresa 3 dos y Transgar tres.

## F1.6b · El filtro de permisos en modo Registrar

**No cambia lo que ve ni lo que puede hacer nadie, y no toca la base** (ni script ni `/alerta-bd`). Cada pantalla declara el permiso que pide ([anexo del ADR-029](../../Arquitectura/ADR-029-anexo-permisos-por-pagina.md)) y el filtro `PermisosPageFilter` anota, sin bloquear, a quién se le negaría.

| Qué | Cómo |
|---|---|
| Interruptor | `Permisos:Modo` en `appsettings.json` (`"Registrar"`). Por variable de entorno: `Permisos__Modo`. Solo la palabra `Aplicar` bloquea; cualquier otro valor, o ninguno, deja `Registrar`. **No se pasa a `Aplicar` antes de F1.6c.** |
| Qué se anota | Una línea Warning por cada petición que se negaría: `Permisos [registro]: se negaría GET /Flota/Catalogos/Vehiculos/Delete (OnGetAsync) por falta de flota.vehiculos.eliminar; cuenta 6, rol empresa_user/6, tenant 4.` Una pantalla que no declaró su permiso sale una sola vez con nivel Error. |
| Dónde se lee | En el log de consola de la aplicación. **En Somee no se guarda** (el ejemplo del `web.config` del servidor trae `stdoutLogEnabled="false"`; falta confirmarlo). Por eso el efecto real se calcula antes de publicar, con la simulación de abajo. |
| Reversa | Publicar el paquete anterior. No hay cambios de base de datos. Sin publicar nada, el modo `Registrar` ya no bloquea a nadie. |

### Simulación sobre producción (solo lectura, 2026-11-09)

Para cada acceso activo se calcularon los permisos que da el evaluador (con los módulos que el plan concede y la empresa tiene activos) y cuántas de las 86 pantallas con permiso quedarían anotadas. **Es lo que se vería en el log si esa persona abriera todas las pantallas; hoy nadie queda bloqueado.**

| Acceso | Rol | Permisos efectivos | Pantallas anotadas (de 86) |
|---|---|---|---|
| `admin` (operador) en el contexto de plataforma | `admin` | todos (72) | 0 |
| `admin` dentro de la empresa 1 (SIP), como `empresa_user` | «Flota» (sin permisos) | 0 | 86 |
| `admin` dentro de la empresa 4 (Transgar), como `empresa_user` | «Administrador» (`es_admin`) | 28 | 46 |
| `cliente_demo` (Demo) | «Flota» | 31 | 40 |

Las cuentas `transgar.admin` y `transgar.flota` están inactivas (membresía suspendida), así que no aparecen. Lo que se anota en Transgar y Demo son sobre todo las pantallas de administración de la empresa (usuarios, roles, invitaciones), de plataforma, de facturación y de catálogos: justo las que hoy esas cuentas no usan.

### Lo que esta simulación deja a la vista para la revisión de roles (antes de F1.6d)

1. **El operador dentro de una empresa se evalúa con el rol de ese acceso**, no como operador, porque hoy `IsAdmin` solo lo reconoce en el contexto de plataforma. Con Flota en modo `Aplicar` perdería, dentro de SIP, todo lo de Flota (el rol «Flota» de SIP no tiene permisos). Decidir si el operador conserva todos los permisos en cualquier contexto.
2. **«Administrador» de Transgar** (`es_admin`, entra como `empresa_user`): conserva los 28 permisos de Flota pero ninguno de administración, como hoy.
3. **«Flota» de SIP** sigue sin permisos.

### Cómo se probó

- 48 pruebas nuevas (suite 1302 de 1302): atributos, el filtro en los dos modos, el contexto de la petición y una matriz de cinco perfiles por las 110 páginas con el evaluador de verdad.
- En una copia de producción en el SQL Server local, con 037 a 041 y la RLS encendida: la aplicación arranca con el filtro; en modo `Registrar` el «Operador de Flota» abre todas sus pantallas y quedan anotadas solo las que su rol no concede; en modo `Aplicar` esas mismas responden 403 con el texto «Tu rol no incluye el permiso para: …» y las demás abren. La copia se borró al terminar.

## F1.6c · Plataforma y administración en modo Aplicar

**No toca la base** (ni script ni `/alerta-bd`) y no cambia quién puede entrar con los permisos de hoy. Las carpetas `/Admin`, `/Empresas`, `/UserManagement`, `/DebugUsers`, `/ResetCodesHistory` y `/ValidarEmails` ya **bloquean**: quien no tiene el permiso de la pantalla recibe la página «Sin permiso» (403) en lugar de volver al menú. Las guardas por rol de esas pantallas se retiraron (ADR-029, sección F1.6c).

| Qué | Cómo |
|---|---|
| Interruptor | `Permisos:AplicarEn` en `appsettings.json`: lista de prefijos que aplican. Si falta, valen las seis carpetas de arriba. `Permisos:Modo = Aplicar` aplica en todo el sistema (se usará al final de F1.6d). |
| Quién entra | Plataforma (`plataforma.*`): solo el operador. Administración (`administracion.*`): el operador y el administrador de empresa (rol `empresa_admin`). Un usuario con rol (`empresa_user`) no entra a ninguna de las dos hasta que se le conceda un permiso `administracion.*`. |
| Reversa | **No se apaga vaciando `AplicarEn`**: las pantallas ya no comprueban el rol y quedarían abiertas. Se publica el paquete anterior (`eGestion360-publish-20261110-f16b`, commit `1305126`). |

### Cómo se probó

- Suite 1323 de 1323 (20 pruebas nuevas): las carpetas de `AplicarEn` y su valor por omisión, una matriz de operador, administrador de empresa y dos roles por todas las pantallas con la **configuración real**, y los casos con nombre (por ejemplo, el administrador de empresa anula una invitación pero no entra a `/Empresas/Create`). Una prueba de arquitectura vigila que no vuelva a quedar una guarda por rol en esas carpetas, y otra que toda pantalla de plataforma o administración esté en una carpeta que aplica.
- En una copia de producción en el SQL Server local (037 a 041, RLS encendida), en modo `Registrar` global: el **operador** entra a Soporte, Empresas, Módulos, Usuarios del sistema, Outbox, Debug y Configuración de correo; el **administrador de empresa** de Transgar abre Nuevo usuario, Roles y permisos e Invitaciones y recibe 403 en Empresas, Módulos y Usuarios del sistema; el **Operador de Flota** (`empresa_user`) recibe 403 en todas las de administración y plataforma, y sus pantallas de flota siguen abriendo. La copia se borró al terminar.
- En producción hoy no hay ningún administrador de empresa activo (simulación de solo lectura del 2026-11-09): los accesos activos son la cuenta del operador (en plataforma y, dentro de SIP y Transgar, como `empresa_user`) y `cliente_demo` (`empresa_user`). Los `empresa_user` ya no entraban a estas pantallas; el operador entra por el permiso.

### La página de acceso (12 de noviembre)

Quien no puede entrar ve ahora una sola página, con el estilo de `app_offline.htm` (fondo oscuro, logo de eGestión360, un candado ámbar, el mensaje y dos botones), en lugar de texto plano. La arma `Services/PaginaDeAcceso.cs` y la usan los tres lugares que responden 403: **sin permiso** (`PermisosPageFilter`), **sin el módulo** (`DerechoDeModuloPageFilter`) y **cuenta suspendida, bloqueada o cancelada** (`/SinAcceso`). Lleva el CSS y el logo adentro, sin imágenes, fuentes ni scripts de afuera, así que se ve igual aunque falle todo lo demás, y codifica todo lo que recibe (por ejemplo el motivo de una suspensión, que escribe un operador). Muestra el permiso que falta y una **referencia** (el identificador de la petición) para que soporte la encuentre en el log. Una llamada de datos (`fetch`) sigue recibiendo el 403 sin página. Los prototipos que se compararon están en el artefacto «Páginas Sin permiso» (se eligió la variante A).
