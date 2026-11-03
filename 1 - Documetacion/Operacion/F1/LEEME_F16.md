# Verificación y reversa de F1.6a (catálogo de permisos y roles)

Complemento de [`LEEME.md`](LEEME.md), [`LEEME_F15.md`](LEEME_F15.md) y [`LEEME_F17.md`](LEEME_F17.md). Este archivo cubre el **script 041** (`2 - Script SQL/041_permisos_catalogo_y_roles.sql`) y su verificación de solo lectura (`verificar_despues_041.sql`). Decisiones: [ADR-029](../../Arquitectura/ADR-029-permisos-por-accion.md).

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
