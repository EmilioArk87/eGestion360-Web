# ADR-027 · Identidad global y cookie con claims (D44 a D50)

- **Estado:** Aceptada en lo decidido (D44 a D50, Emilio, 2026-10-15); los detalles de implementación de abajo los propuso Claude al hacer el código de F1.5 y quedan por confirmar al publicar
- **Fecha:** 2026-10-30
- **Decidió:** Emilio (D44 a D50); Claude (detalles de implementación)
- **Fase:** F1.5
- **Completa:** [ADR-003](ADR-003-identidad-y-membresias.md) (cuenta global con membresías) y [ADR-008](ADR-008-autenticacion-cookie-claims.md) (MFA y bloqueo). Las tablas
  las crean los scripts 038 y 039.

## Contexto

Hasta F1.4 «quién eres» vivía en la sesión del servidor: 10 claves que el inicio de sesión escribía y 140 comprobaciones que las leían. La
sesión está en memoria, así que cada reinicio del sitio cerraba la sesión de todos; no había bloqueo por intentos ni segundo factor; una
persona con trabajo en dos tenants necesitaba dos usuarios (Emilio tenía tres: `admin`, `egaray` y `egaray.transgar`); y un cambio de
rol o una baja no se notaban hasta que la persona volviera a entrar.

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| D50 | La identidad sale de la **cookie** (claims), no de la sesión | `AuthHelper` conserva su API pero lee `HttpContext.User`. La cookie (`.eg360.auth`, HttpOnly, SameSite Lax, 30 min con renovación) lleva la cuenta, el nombre con el que entró, el rol de texto, el tenant, la empresa, la membresía y una huella (`sello`). La sesión ya no decide nada: una sesión sin cookie no autentica a nadie. |
| D50 | La sesión queda como **espejo** para el código anterior | `SesionDesdeIdentidadMiddleware` copia la identidad a las claves que leen unas 50 pantallas (`Username` para el «creado por», `UserId`, `Role`, `Email`, `EmpresaId`...) y guarda los módulos y permisos de la empresa. Se rehace sola tras un reinicio y se vacía si la cookie ya no sirve. Nada más escribe esas claves (lo vigila una prueba). |
| D50 | La cookie se **compara con la base** cada 30 s | `ValidadorDeIdentidad` lee cuenta, membresía y acceso (3 consultas pequeñas por persona cada 30 s). Cuenta desactivada, membresía suspendida o acceso inactivo: la cookie se rechaza en la petición siguiente. Rol, rol de empresa o cambio de clave pendiente distinto: la cookie se renueva con lo nuevo, sin cerrar la sesión. |
| D44 | El correo es el identificador; el usuario y los alias de usuario se siguen aceptando | Un alias de correo no inicia sesión. En la auditoría («creado por») queda el nombre con el que se entró (`egaray.transgar`, o el usuario de la cuenta si se entró con el correo). |
| D46 | **5 fallos** bloquean la cuenta **15 minutos** | El bloqueo se levanta solo y un acierto o una clave nueva limpia el contador. Una cuenta bloqueada, una clave equivocada y una cuenta inexistente muestran el **mismo** mensaje: no se revela cuáles cuentas existen. Un código de MFA equivocado cuenta como fallo de clave. |
| D47 | El **operador** debe activar la verificación en dos pasos desde el primer día; el **administrador de empresa** tiene una ventana de gracia | Operador sin MFA: entra solo después de activarla. Administrador de empresa: solo se le avisa hasta la fecha `Identidad:MfaObligatoriaAdminTenantDesde` (vacía: nunca se le exige todavía; se fija al publicar, 14 días después). Cualquiera puede activarla antes (menú del usuario). Quien la tiene activa debe escribir un código en cada inicio de sesión. 10 códigos de recuperación de un solo uso (solo su hash). |
| D49 | **Otp.NET** y **QRCoder** | TOTP RFC 6238 (SHA-1, 6 dígitos, 30 s, ventana de ±1 paso); un código ya usado no sirve otra vez. Probado con los vectores oficiales de la RFC. |
| D48 | Las llaves de Data Protection viven en `saas.llaves_proteccion_datos` | La cookie, la sesión, los tokens antifalsificación y los secretos de MFA siguen válidos tras reiniciar o reciclar el sitio. Sin cifrar mientras la base sea de la aplicación (Somee); en Azure pasan a Key Vault. |
| — | El inicio de sesión tiene **tres pasos** y la identidad solo existe al final | 1) clave → *entrada en curso* en la sesión (10 min, no da acceso a nada); 2) código de MFA si corresponde; 3) elegir dónde trabajar (si solo hay un sitio, entra directo) y recién entonces se emite la cookie. Las páginas de los pasos 2 y 3 desvían al paso que falta: no se puede saltar el segundo factor. |
| — | Un **sitio** es la plataforma (solo operador) o una empresa de un tenant con su rol | La lista se vuelve a leer de la base en cada elección; el formulario nunca decide el tenant (ADR-001). «Cambiar de sitio» con la sesión iniciada revalida y conserva el segundo factor. Los accesos (tabla de tenant) se leen fijando el tenant de cada membresía, una por una. |
| — | **Users sigue siendo donde se escribe** hasta F1.10; las cuentas lo siguen | `SincronizadorDeCuentas` copia a la cuenta lo que cambia en un usuario (clave, estado, correo, empresa, rol, persona), al crear, editar, vincular una persona y restablecer o cambiar una clave. Una clave nueva levanta el bloqueo. Así volver al código anterior no pierde claves ni usuarios nuevos. El alias de una cuenta (por ejemplo `egaray`) solo cambia su acceso y la clave, no el correo ni el estado de la cuenta. |

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| D50 | Dejar la identidad en la sesión | Se pierde con cada reinicio y no permite dos instancias. |
| D50 | Cookie sin comparar con la base | Una baja o un cambio de rol no se notarían hasta caducar la cookie. |
| D50 | Comparar con la base en cada petición | Tres consultas por petición para un dato que casi nunca cambia; 30 s da el mismo resultado práctico. |
| D50 | Respaldo en la sesión si no hay cookie | Dos fuentes de verdad: una baja quedaría anulada por una sesión vieja. |
| D50 | Reescribir las 56 pantallas que leen la sesión | Sin ganancia en F1.5; el espejo las deja como están y la sesión desaparece de ellas en F1.6. |
| — | Escribir primero las cuentas y reflejarlas en Users | Las pantallas de administración (usuarios, restablecer clave) escriben en Users; cambiarlas todas es F1.6. |
| D47 | Exigir MFA a todos | Fricción sin ganancia para quien no administra. |

## Consecuencias

- **Todos entran de nuevo una vez** al publicar; después la sesión sobrevive a reinicios y reciclajes del sitio.
- **Emilio deja de necesitar tres usuarios:** una cuenta, tres sitios (Plataforma, SIP y Transgar). Sus usuarios `egaray` y `egaray.transgar` siguen entrando con la clave de `admin` como alias.
- El script 039 exige que `dbo.Users` tenga **exactamente** las 8 filas medidas: hay que aplicarlo antes de crear un usuario nuevo. Después, cada usuario nuevo recibe cuenta, membresía y acceso al crearse.
- Siguen en Users y llegan a las cuentas por copia: un cambio hecho directamente en la base a `dbo.Users` (sin pasar por las pantallas) no llega a la cuenta.
- **No incluye (queda para después):** invitaciones por correo (las resuelve [ADR-028](ADR-028-invitaciones-por-correo.md)), permisos por acción y retirar los `IsAdmin(` (F1.6), la auditoría de seguridad generalizada (F1.8) y retirar `Users` (F1.10).
- Cuenta sin acceso a MFA (teléfono perdido y sin códigos): `2 - Script SQL/emergencia_mfa_desactivar.sql`, solo si Emilio lo pide en el chat.
- Requisito de publicación: scripts 038 y 039 aplicados antes que el código (sin las tablas, el inicio de sesión y las llaves de Data Protection fallan).

## Cómo se verifica

- Pruebas (`Services/Identidad`, `Paginas/PaginasDeEntradaTests`, `Arquitectura/IdentidadArquitecturaTests`): bloqueo y desbloqueo; alta, código, repetición y códigos de recuperación de MFA; sitios permitidos por cuenta; credenciales; exigencia de MFA por rol y fecha; validación y renovación de la cookie; espejo de la sesión; cada paso de la entrada (no se salta el segundo factor, un sitio ajeno se rechaza, el cambio de sitio conserva el MFA); sincronía con `Users`; llaves en la base; y que ninguna pantalla vuelva a decidir por la sesión.
- Recorrido de la aplicación sobre una copia con los scripts 037 a 040 y la política RLS encendida (2026-10-30): inicio de sesión de un usuario de un solo sitio; el operador con su alias, alta obligatoria de MFA, tres sitios, entrada a Transgar (222 filas de vehículos con la política encendida), plataforma y soporte; la cookie sobrevive a reiniciar el servidor; una cuenta desactivada deja de entrar en 33 s; cinco fallos bloquean y el mensaje no cambia; saltar el MFA vuelve a `/Mfa`; el cambio de clave obligatorio y su copia a `Users`; y un usuario creado desde Admin que entra con su correo.
