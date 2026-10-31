# F1.5 · Identidad global: scripts 038 y 039

Guía de los scripts y las verificaciones de la etapa F1.5 (cuentas globales, membresías por tenant, MFA e invitaciones; ADR-003,
ADR-008, D19, D22, D24 y D45). Complementa a `LEEME.md`, que describe las verificaciones de la salida de F1 (scripts 025 a 037) y que
sigue valiendo como historial.

## Archivos

| Archivo | Para qué sirve |
|---|---|
| `2 - Script SQL/038_identidad_global.sql` | Crea la estructura: 8 tablas del plano de control en `saas` (`cuentas`, `cuenta_alias`, `membresias`, `cuenta_intentos`, `cuenta_mfa`, `cuenta_mfa_recuperacion`, `invitaciones`, `llaves_proteccion_datos`) y `dbo.accesos_empresa`, una tabla de tenant que entra a la política `seg.pol_tenant` (199 a 204 predicados). Solo agrega: no migra datos ni toca nada existente. |
| `2 - Script SQL/039_migrar_cuentas.sql` | Copia las 8 filas de `dbo.Users` a las tablas nuevas con reglas explícitas por nombre de usuario. No modifica ni borra `dbo.Users` y copia los hashes BCrypt sin imprimirlos. |
| `2 - Script SQL/emergencia_mfa_desactivar.sql` | Desactiva el MFA de UNA cuenta (se edita a mano la variable del correo al inicio; con el valor de ejemplo no hace nada). Solo se ejecuta si Emilio lo pide en el chat. |
| `verificar_despues_038.sql` | Solo lectura. Estado exacto tras 038 con la política ENCENDIDA: **41 OK**. |
| `verificar_despues_f15.sql` | Solo lectura. Estado exacto tras 038 y 039 con la política ENCENDIDA: **59 OK**. |

## Qué hace 039 con las cuentas actuales

- **Una sola cuenta de operador para Emilio (D45):** `admin`, `egaray` y `egaray.transgar` pasan a una cuenta con rol de operador de la
  plataforma, con la clave de `admin`. `egaray` y `egaray.transgar` quedan como alias de usuario para seguir entrando durante la transición, y
  sus correos como alias que no inician sesión. Tiene membresía en SIP (persona 102) y en TRANSGAR (persona 104), con el acceso y el rol que
  cada usuario tenía.
- **`cliente_demo`:** cuenta propia, con membresía en DEMO.
- **Cuentas sin persona o sin empresa (D24):** `nnolasco`, `Moran23113`, `transgar.admin` y `transgar.flota` pasan como cuentas inactivas.
  Las que tenían empresa conservan una membresía suspendida.
- Cifras esperadas: 6 cuentas, 4 alias, 5 membresías y 5 accesos por empresa. El PRECHECK aborta, sin cambiar nada, si los 8 usuarios de origen no
  coinciden con lo medido (nombre, empresa, rol, persona y estado).

## Cuándo correr cada archivo

| Momento | Archivo | Se espera |
|---|---|---|
| Después de aplicar 038 | `verificar_despues_038.sql` | 41 OK |
| Después de aplicar 039 | `verificar_despues_f15.sql` | 59 OK |
| Después de revertir 039 (ROLLBACK comentado al final del script) | `verificar_despues_038.sql` | 41 OK |

Los dos verificadores asumen la política ENCENDIDA, como está producción. Con la política apagada fallan las filas que prueban que
`accesos_empresa` queda oculta sin contexto de sesión, y es lo correcto.

## Orden de aplicación respecto de F1.7

Los scripts 038, 039 y 040 (F1.7, ver `LEEME_F17.md`) se pueden aplicar en cualquier orden: cada PRECHECK y cada verificador toleran la presencia
o ausencia del otro (por ejemplo, 040 espera 199 o 204 predicados según exista `accesos_empresa`, y las cifras de estos verificadores suman las
5 tablas de F1.7 si ya están). Lo que sí importa:

- **038** solo agrega estructura: se puede aplicar en cualquier momento, con el sitio en línea.
- **039** copia las claves de `dbo.Users` en un instante. Hay que aplicarlo con el sitio en mantenimiento y **justo antes de publicar el código de
  F1.5**, para que ninguna clave cambiada en el medio quede desactualizada en `saas.cuentas`.

## El código de F1.5 y el orden de publicación

El código está en `Services/Identidad/` (decisiones en [ADR-027](../../Arquitectura/ADR-027-identidad-global-y-cookie-con-claims.md)): cookie con claims y su validación contra la base cada 30 s, espejo de la sesión para el código anterior, bloqueo por intentos, MFA con TOTP, tres pasos de entrada (`Login`, `Mfa` / `MfaAlta`, `SeleccionarContexto`), llaves de Data Protection en `saas` y `SincronizadorDeCuentas`, que copia a las cuentas lo que cambia en `dbo.Users`.

**Publicación (todos entran de nuevo una vez):**

1. `/alerta-bd` y **038** (cuando se quiera; solo agrega): `verificar_despues_038.sql` → 41 OK.
2. Ventana corta: sitio en mantenimiento (`app_offline.htm`), `/alerta-bd` y **039**, `verificar_despues_f15.sql` → 59 OK.
3. Publicar el código con `Identidad:MfaObligatoriaAdminTenantDesde` en `appsettings.json` puesto a la fecha de publicación + 14 días (`aaaa-mm-dd`), quitar el mantenimiento.
4. Emilio entra con `admin` (o `egaray` / `egaray.transgar`: es la misma cuenta, con la clave de `admin`), activa la verificación en dos pasos con su teléfono y **guarda los 10 códigos de recuperación**. Esa primera entrada la hace Emilio en persona: Claude no ve la clave ni el teléfono.
5. Claude verifica con Emilio los tres sitios (Plataforma, SIP, Transgar), el cierre de sesión y la vigilancia diaria (`comprobacion_diaria.ps1` detecta 038 y 039 y suma las cifras).

El código **no** funciona sin 038 y 039 (las llaves de Data Protection y el inicio de sesión leen sus tablas): no se publica antes. Después de 039, cada usuario que se cree desde las pantallas de administración recibe cuenta, membresía y acceso solo; un usuario creado a mano en la base antes de 039 haría fallar su PRECHECK.

**Si algo sale mal:** el paquete anterior (F1.7) convive con las tablas nuevas (ignora `saas.cuentas`), y como `dbo.Users` siempre estuvo al día, volver atrás no pierde claves ni usuarios. Quien se quede sin MFA: `emergencia_mfa_desactivar.sql`, solo si Emilio lo pide en el chat.

## Reversa

Cada script trae su sección ROLLBACK comentada al final, con una guarda que aborta si ya hay datos que se perderían. Se revierte en orden inverso:
primero 039 y después 038. `dbo.Users` nunca se toca.

## Ensayo

Los tres scripts (038, 039 y 040) se ensayaron en LocalDB sobre una copia de producción tras el script 036, con la política RLS encendida y apagada:
aplicación, verificadores, repetición (idempotencia), ROLLBACK en orden inverso y pruebas negativas. Contra producción no se ha ejecutado nada.
