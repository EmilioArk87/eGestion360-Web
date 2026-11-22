# Configuración de secretos

Las contraseñas y claves ya **no** están en `appsettings.json`. Ese archivo se sube a git, así que
cualquiera con acceso al repositorio podía leerlas.

## Qué hay que configurar

| Clave | Para qué sirve |
|-------|----------------|
| `ConnectionStrings:DefaultConnection` | Cadena de conexión completa a SQL Server, con la contraseña |
| `EmailSettings:Password` | Contraseña de la cuenta SMTP |
| `Encryption:Key` | Clave AES para las contraseñas SMTP guardadas en la tabla `EmailConfiguration` |
| `Encryption:IV` | Vector de inicialización AES (16 caracteres) |

`Encryption:Key` e `Encryption:IV` deben ser **los mismos valores que se usaron al guardar** las
configuraciones de email existentes. Si se cambian, esas contraseñas ya no se pueden descifrar y hay que
volver a cargarlas en `/ConfigurarHostinger`. Si faltan, la aplicación avisa con un error claro al usar el email.

## Opción 1: archivo local `appsettings.Secrets.json` (recomendada)

1. Copie `appsettings.Secrets.example.json` como `appsettings.Secrets.json` en la raíz del proyecto.
2. Complete los valores reales.
3. El archivo está en `.gitignore`, así que no se sube a git, pero sí se incluye al publicar
   (`dotnet publish`), de modo que llega al servidor.

## Opción 2: variables de entorno

Use doble guion bajo en lugar de `:`. Tienen prioridad sobre los archivos.

```
ConnectionStrings__DefaultConnection=...
EmailSettings__Password=...
Encryption__Key=...
Encryption__IV=...
```

## Importante

Las contraseñas anteriores quedaron en el historial de git. Quitarlas del archivo no las borra de ahí,
así que hay que **cambiarlas** en Somee (base de datos) y en Hostinger (correo).

## Páginas de mantenimiento

`/EncryptPasswords`, `/ConfigurarHostinger`, `/ValidarEmails`, `/ResetCodesHistory` y todo `/Admin`
ahora exigen sesión con rol administrador. Las páginas `/ResetAdmin` (dejaba la contraseña del admin en
`admin123` sin pedir sesión) y `/DebugUsers` (mostraba las contraseñas) se eliminaron.
