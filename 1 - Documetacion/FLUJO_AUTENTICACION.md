# Flujo de Autenticación — eGestion360

## Páginas involucradas

| Página | Ruta URL | Archivo |
|--------|----------|---------|
| Login | `/Login` | `Pages/Login.cshtml.cs` |
| Olvidé mi contraseña | `/ForgotPassword` | `Pages/ForgotPassword.cshtml.cs` |
| Restablecer contraseña | `/ResetPassword` | `Pages/ResetPassword.cshtml.cs` |
| Cambiar contraseña | `/ChangePassword` | `Pages/ChangePassword.cshtml.cs` |
| Menú principal | `/MainMenu` | `Pages/MainMenu.cshtml.cs` |
| Gestión de usuarios | `/UserManagement` | `Pages/UserManagement.cshtml.cs` |
| Historial de resets | `/ResetCodesHistory` | `Pages/ResetCodesHistory.cshtml.cs` |

---

## 1. Flujo de Login

```
Usuario accede a cualquier página protegida
        │
        ▼
¿Tiene sesión activa? (Session["UserId"] != null)
   ├── SÍ → redirige a /MainMenu
   └── NO → muestra formulario de Login
              │
              ▼
       Ingresa usuario/email + contraseña
              │
              ▼
   Busca en tabla `usuarios` donde:
   (Username == input OR Email == input) AND IsActive == true
              │
        ¿Encontrado?
   ├── NO → "Usuario o contraseña incorrectos"
   └── SÍ → verifica contraseña:
              │
        ¿Password empieza con "$2"?
        ├── SÍ (BCrypt) → BCrypt.Verify()
        └── NO (texto plano) → comparación directa
                                + migra automáticamente a BCrypt
              │
        ¿Contraseña válida?
   ├── NO → "Usuario o contraseña incorrectos"
   └── SÍ → guarda en sesión:
              Session["UserId"]   = user.Id
              Session["Username"] = user.Username
              Session["Email"]    = user.Email
              │
              ▼
         Redirige a /MainMenu
```

### Campos del formulario de Login

| Campo | Tipo | Validación |
|-------|------|------------|
| Usuario | Text | Requerido. Acepta `Username` o `Email` |
| Contraseña | Password | Requerida |
| Recordarme | Checkbox | Opcional (visual, sin efecto en sesión actualmente) |

### Comportamiento de sesión

- Sesión almacenada en servidor (ASP.NET Core Session).
- Al cerrar el navegador, la sesión expira según configuración en `Program.cs`.
- Todas las páginas protegidas verifican `Session["UserId"]` via `AuthHelper.IsAuthenticated()`.

### Migración automática de contraseñas

Si la contraseña en BD está en texto plano (legado), el login la convierte automáticamente a hash BCrypt en el mismo request. El usuario no nota ninguna diferencia.

---

## 2. Flujo de Recuperación de Contraseña

```
/ForgotPassword
   Usuario ingresa su email
        │
        ▼
   Genera código de 6 dígitos (PasswordResetService)
   Registra en tabla `password_reset_codes` con:
   - Email, código, IP del cliente, fecha expiración
        │
        ▼
   Envía email con código (EmailService)
        │
   (Por seguridad, siempre muestra "email enviado"
    aunque el email no exista en el sistema)
        │
        ▼
/ResetPassword
   Usuario ingresa: email + código de 6 dígitos + nueva contraseña
        │
        ▼
   Valida código: vigente, no usado, coincide con email
        │
        ▼
   Actualiza contraseña con BCrypt hash
   Marca código como usado
   Envía email de confirmación al usuario
        │
        ▼
   Redirige a /Login
```

### Campos de ResetPassword

| Campo | Validación |
|-------|------------|
| Email | Formato email válido |
| Código de verificación | Exactamente 6 dígitos numéricos |
| Nueva contraseña | 6–100 caracteres |
| Confirmar contraseña | Debe coincidir con nueva contraseña |

---

## 3. Cambio de Contraseña (usuario autenticado)

Accesible desde `/ChangePassword` solo para usuarios con sesión activa.

- Requiere la contraseña actual para confirmar identidad.
- Aplica hash BCrypt a la nueva contraseña.
- No cierra la sesión al completar el cambio.

---

## 4. Gestión de Usuarios (administrador)

### Acceso

`/UserManagement` — Requiere sesión activa. Actualmente no hay diferenciación de roles en código; cualquier usuario autenticado puede acceder.

### Funcionalidades

- Ver lista de usuarios del sistema.
- Activar / desactivar usuarios (`IsActive`).
- Forzar cambio de contraseña en próximo login (`RequirePasswordChange`).
- Reset de contraseña por parte del admin (`/ResetAdmin`).

### Modelo de usuario (`tabla: usuarios`)

| Campo BD | Propiedad C# | Descripción |
|----------|-------------|-------------|
| `id` | `Id` | PK autoincremental |
| `username` | `Username` | Nombre de usuario (único, max 50) |
| `email` | `Email` | Correo electrónico (único, max 100) |
| `password` | `Password` | Hash BCrypt (max 500) o texto plano (legado) |
| `created_at` | `CreatedAt` | Fecha de creación UTC |
| `is_active` | `IsActive` | Si puede iniciar sesión |
| `require_password_change` | `RequirePasswordChange` | Flag para forzar cambio |

> Ver detalle del mapeo de columnas en [USUARIOS_TABLE_MAPPING.md](USUARIOS_TABLE_MAPPING.md)

---

## 5. Historial de Códigos de Reset

`/ResetCodesHistory` — muestra todos los códigos generados con estado (usado, expirado, activo). Útil para auditoría y diagnóstico.

---

## 6. Protección de Páginas

Todas las páginas del módulo Flota y administración verifican autenticación al inicio del handler:

```csharp
if (!AuthHelper.IsAuthenticated(HttpContext))
    return RedirectToPage("/Login");
```

`AuthHelper.IsAuthenticated()` comprueba que `Session["UserId"]` no sea nulo ni vacío.

---

## 7. Servicios Involucrados

| Servicio | Interfaz | Responsabilidad |
|----------|----------|-----------------|
| `PasswordService` | `IPasswordService` | Hash BCrypt y verificación |
| `PasswordResetService` | `IPasswordResetService` | Generar, validar y marcar códigos de reset |
| `EmailService` | `IEmailService` | Envío de correos (código de reset, confirmación) |
| `AuthHelper` | — (clase estática) | Verificar sesión activa en cada página |
