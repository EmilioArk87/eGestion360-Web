# ADR-008 · Autenticación por cookie con claims y políticas de autorización

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D8 del plan)
- **Fase:** F1 (el retiro del login con texto plano va en F0.7)

## Contexto

- Hoy la autenticación es una sesión propia: `Pages/Login.cshtml.cs` guarda `UserId`, rol, empresa, módulos y permisos
  (JSON) en `Session`, y `Services/AuthHelper.cs` los lee. No se usa la autenticación de ASP.NET Core.
- Los módulos y permisos se copian a la sesión al iniciar: un cambio de rol, una desactivación o un vencimiento de módulo
  no se aplican hasta el siguiente login (`empresa_modulos.fecha_vencimiento` no se valida).
- El login acepta contraseñas guardadas en texto plano y las convierte a BCrypt al vuelo. No hay bloqueo por intentos
  fallidos ni MFA.
- La autorización se escribe a mano en cada página (80 de 100 páginas llevan su guarda), más dos filtros globales por
  prefijo de ruta.
- La sesión en memoria impide correr dos instancias de la aplicación.

## Decisión

- **ASP.NET Core Cookie Authentication** con claims: cuenta, tenant, empresa y un sello de versión de permisos. Sobre
  tablas propias del esquema (no ASP.NET Core Identity completo).
- **Autorización por políticas:** requisitos por carpeta (por ejemplo, `/Contabilidad` exige el derecho de uso
  `contabilidad`) y por acción (`contabilidad.asiento.mayorizar`), aplicados por convención de Razor Pages en lugar de
  guardas manuales en cada página. El menú se arma con la misma evaluación.
- **Permisos evaluados en cada petición**, con caché por tenant y usuario invalidada por el sello de versión.
- **Bloqueo por intentos fallidos** y **MFA TOTP** obligatorio para administradores de tenant y operadores de la
  plataforma.
- **Llaves de Data Protection persistidas** fuera del proceso, para que las cookies sigan siendo válidas tras un
  reinicio y entre instancias.
- El login con contraseñas en texto plano se retira antes, en F0.7.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Seguir con la sesión propia | Permisos desactualizados hasta el siguiente login, no escala a varias instancias y obliga a guardas manuales. |
| ASP.NET Core Identity completo | Agrega sus tablas y convenciones (`AspNet*`) que no encajan con el esquema snake_case en español. Se reevalúa si hace falta login externo (Google, Microsoft). |

## Consecuencias

- `AuthHelper` se reemplaza por servicios de autorización; las páginas dejan de llevar guardas manuales.
- Los usuarios inician sesión de nuevo una vez al pasar al esquema nuevo.
- Hace falta almacenamiento para las llaves de Data Protection en el hosting.

## Cómo se verifica

- Un cambio de rol se aplica en la petición siguiente sin cerrar sesión.
- Una página nueva dentro de una carpeta protegida queda protegida sin escribir código de autorización.
- Tras N intentos fallidos la cuenta se bloquea temporalmente; un administrador sin MFA no puede entrar.
