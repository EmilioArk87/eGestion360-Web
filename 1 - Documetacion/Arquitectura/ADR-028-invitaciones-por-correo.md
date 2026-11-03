# ADR-028 · Invitaciones por correo (F1.5)

- **Estado:** Aceptada por Emilio el 2026-11-02, tras probarla de punta a punta en producción (invitación a Iris Z., persona 117, empresa Demo: correo enviado, clave elegida por la propia persona, cuenta activada y entrada con su correo). Propuesta por Claude el 2026-10-31 al hacer el código de F1.5
- **Fecha:** 2026-10-31
- **Decidió:** Claude (implementación). Emilio pidió las invitaciones como parte de F1.5; la tabla `saas.invitaciones` y el estado
  «invitada» de las membresías vienen de [ADR-003](ADR-003-identidad-y-membresias.md) y del script 038.
- **Fase:** F1.5
- **Completa:** [ADR-027](ADR-027-identidad-global-y-cookie-con-claims.md), que dejó las invitaciones fuera.

## Contexto

Hasta ahora, dar acceso a una persona significaba que un administrador escribiera su usuario **y su contraseña** en
`/Admin/Usuarios/Create` y se la pasara por otro medio: el administrador conocía la clave de otra persona. La plataforma ya tiene correo
saliente (restablecer clave) y una tabla de invitaciones sin uso. Falta el servicio y la pantalla.

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| 1 | **Invitar crea el usuario sin activar; aceptar lo activa** | `dbo.Users` sigue siendo donde escriben las pantallas de administración hasta F1.10 (ADR-027). Invitar crea el usuario inactivo, con su empresa, su rol y su persona ya vinculada, y una clave que nadie conoce (BCrypt de 32 bytes al azar). La cuenta, la membresía y el acceso nacen suspendidos por el sincronizador. Aceptar le pone la clave que la persona eligió y lo activa. Así los datos de tenant (vínculo de la persona con la empresa, acceso) se escriben en la petición del administrador, con su autoridad, y no desde una pantalla anónima; el usuario aparece en «Gestión de Usuarios»; y volver al código anterior no pierde a nadie. |
| 2 | **No hay ningún script nuevo** | Se usa `saas.invitaciones` como la dejó el 038 (`id_cuenta` apunta a la cuenta pendiente; `usada_en` marca el uso). La persona y si es administrador se leen del usuario. El estado `invitada` de cuentas y membresías queda sin usar: la cuenta pendiente es `inactiva`. |
| 3 | **El token** | 256 bits al azar en base64url (43 caracteres). Solo se guarda su SHA-256 en hexadecimal (64): quien lea la base no puede armar un enlace. Vigencia `Identidad:DiasDeInvitacion` (7). Se usa una sola vez: aceptar lo reclama con un `UPDATE ... WHERE usada_en IS NULL AND vence_en > ahora`, así que si dos personas abren el enlace a la vez solo una gana. Reenviar cambia el token (el enlace anterior deja de servir) y la vigencia empieza de nuevo. Anular vence la invitación en el acto; la fila queda como rastro. |
| 4 | **A quién se invita** | A una persona **ya registrada** en la empresa (la elige el administrador con la misma búsqueda de «Nuevo usuario») y **sin cuenta**: si el correo ya tiene un usuario, una cuenta, un alias o una invitación pendiente, se rechaza con el motivo. Sumar a una persona que ya tiene cuenta a un segundo tenant necesita el modelo de F1.6 en adelante y queda fuera. |
| 5 | **Con qué rol** | Administrador de empresa (sin rol: tiene todos los permisos) o usuario con un rol activo de **esa** empresa, obligatorio. Nunca `admin`. |
| 6 | **Quién invita** | El administrador general (dentro del tenant de soporte que eligió, como en las demás pantallas de datos de un tenant) y el administrador de empresa para **su** empresa: el formulario no decide la empresa. La lista, reenviar y anular siguen la misma regla. |
| 7 | **El plan manda** | El tope de usuarios activos del plan (D54) se comprueba al invitar y otra vez al aceptar (se puede haber llenado entre tanto). Un tenant que no está activo no envía ni acepta invitaciones. |
| 8 | **La pantalla de aceptar corre sin sesión** | `/AceptarInvitacion` es una ruta sin sesión de `AmbitoDePlataformaPageFilter`. Solo conoce el tenant por el token, y la base no deja cambiarlo con una transacción abierta (`ContextoDeSesionInterceptor`): el servicio **fija el tenant de la invitación antes de abrir la transacción**. La respuesta lleva `Referrer-Policy: no-referrer` y `Cache-Control: no-store`; el formulario repite el token en un campo oculto; ningún registro del servidor menciona token, enlace ni clave. Un enlace que no existe, uno vencido y uno ya usado tienen su propio mensaje; el token no se puede adivinar, así que distinguirlos no es una fuga. |
| 9 | **Nombre de usuario** | Sale de la parte local del correo (letras, dígitos, punto, guion y guion bajo; hasta 40) y se numera si ya existe. La persona entra con su **correo** (D44); el usuario existe porque `Users` lo exige. |
| 10 | **El correo** | Se envía por `IEmailService.SendHtmlEmailAsync` (la misma configuración SMTP del resto) con los nombres escapados. Si no sale, la invitación existe y la pantalla lo avisa: se resuelve con «Reenviar». |

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| 1 | Crear la cuenta solo en `saas` y dejar `Users` sin fila | Las pantallas de administración leen `Users`: nadie podría ver, desactivar ni editar al invitado, y volver al código anterior lo dejaría sin acceso. |
| 2 | Una columna `id_persona` en `saas.invitaciones` y crear el usuario al aceptar | Exige un script (041), cambiar los verificadores de F1.5 y escribir datos de tenant desde una pantalla anónima. |
| 3 | Crear la persona al invitar | Una invitación sin aceptar dejaría una persona y un vínculo «usuario» de más; hoy la persona se registra donde corresponde (Personal, Clientes). |
| 4 | Invitar también a quien ya tiene cuenta | Pide membresías en más de un tenant con `Users` de por medio (hoy una persona con dos tenants es dos usuarios, como `egaray`); eso se resuelve cuando `Users` se retire. |
| 5 | Que el administrador siga escribiendo la clave | Es lo que se quiere dejar de hacer; la pantalla «Nuevo usuario» sigue ahí para quien la necesite. |

## Consecuencias

- Un administrador ya no necesita conocer ni transmitir la clave de nadie.
- El usuario invitado aparece **inactivo** en «Gestión de Usuarios» hasta que acepta. Una invitación anulada o vencida deja ese usuario inactivo: «Reenviar» lo reutiliza (no se crea otro), y editarlo desde «Gestión de Usuarios» sigue siendo posible.
- Invitar a alguien que no está registrado como persona obliga a registrarlo antes.
- Para publicar **no hay cambios de base de datos**: solo el código (sin `/alerta-bd`). Los scripts 038 y 039 ya están aplicados.
- Requiere correo saliente configurado (`EmailConfigurations`), como el restablecimiento de clave.

## Cómo se verifica

- Pruebas: el servicio (invitar, reglas, aceptar, vencer, una sola vez, reenviar, anular, lista, límite del plan, tenant suspendido), las tres pantallas, y arquitectura (solo el servicio escribe invitaciones; el token nunca se guarda ni se registra; el tenant se fija antes de la transacción; la pantalla de aceptar no usa la sesión).
- Recorrido en el navegador sobre una copia con los scripts 025 a 040 y la política RLS encendida (2026-10-31): un administrador de empresa invita desde la pantalla, el correo (simulado) trae el enlace, una clave débil se rechaza sin gastar el enlace, una buena activa la cuenta, la persona entra con su correo y ve su empresa con su nombre, el enlace usado y uno inventado se rechazan con su mensaje. Ese recorrido encontró un defecto que las pruebas con SQLite no podían ver (cambiar de tenant con la transacción abierta) y quedó corregido y vigilado.
