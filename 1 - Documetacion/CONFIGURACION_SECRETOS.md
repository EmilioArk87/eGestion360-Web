# Configuración de secretos por variables de entorno

Los secretos ya **no** viven en `appsettings.json`. La aplicación los toma de variables de
entorno, que ASP.NET Core lee automáticamente y que tienen prioridad sobre los archivos
`appsettings*.json`.

El separador de niveles es **doble guion bajo** (`__`), porque `:` no es válido en nombres de
variables en todos los sistemas. Es decir, `Encryption:Key` en JSON se llama `Encryption__Key`
como variable de entorno.

## Variables requeridas

| Variable | Reemplaza a | Notas |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | Cadena completa de SQL Server (`eBD_SPD` en somee.com). |
| `EmailSettings__Password` | `EmailSettings:Password` | Contraseña SMTP de Hostinger. |
| `Encryption__Key` | `Encryption:Key` | Clave AES-256. **Ver la advertencia de abajo.** |
| `Encryption__IV` | `Encryption:IV` | IV de 16 bytes. **Ver la advertencia de abajo.** |

El resto de la configuración (`EmailSettings:SmtpHost`, `KpiSync:*`, `Logging`, etc.) no es
secreta y sigue en `appsettings.json`.

## `Seguridad:ForzarHttps`

No es un secreto, pero sí depende del entorno. Controla `UseHttpsRedirection()` y `UseHsts()`:

- **`true`** (valor por defecto en `appsettings.json`): comportamiento normal, todo HTTP
  redirige a HTTPS.
- **`false`**: lo que hay hoy en producción. Somee **no tiene TLS activo** en
  `siptecnologia.somee.com` —el puerto 443 acepta la conexión pero el handshake falla—, así
  que forzar HTTPS devuelve 307 hacia una URL muerta y deja el portal inaccesible.

Cuando Somee tenga certificado, poner el flag en `true` en el `appsettings.Production.json`
del servidor y reiniciar. No hace falta recompilar ni volver a desplegar.

Si falta la cadena de conexión o las claves de cifrado, la aplicación **no arranca** y dice cuál
falta. Es deliberado: antes existía un valor por defecto que dejaba la app funcionando con la
clave equivocada.

> ⚠️ **La clave de cifrado no se puede cambiar a voluntad.** `EncryptionService` cifra con
> AES-256 las contraseñas SMTP guardadas en la base. Si `Encryption__Key` o `Encryption__IV`
> no son **exactamente** las que se usaron al cifrarlas, `Decrypt()` devuelve basura y esas
> contraseñas se vuelven irrecuperables. Para rotarlas hay que descifrar con la clave vieja y
> volver a cifrar con la nueva, en ese orden.

## Desarrollo local (Windows / PowerShell)

Definir las variables a nivel de usuario, una sola vez. Se toman en la siguiente sesión de
terminal (hay que reabrir la consola o el editor):

```powershell
[Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', '<cadena>', 'User')
[Environment]::SetEnvironmentVariable('EmailSettings__Password', '<contraseña>', 'User')
[Environment]::SetEnvironmentVariable('Encryption__Key', '<clave>', 'User')
[Environment]::SetEnvironmentVariable('Encryption__IV', '<iv>', 'User')
```

Para verificar que quedaron cargadas:

```powershell
Get-ChildItem Env: | Where-Object Name -match 'ConnectionStrings__|EmailSettings__|Encryption__'
```

No conviene guardar estos valores en un archivo dentro del repositorio: la carpeta está
sincronizada con OneDrive, así que cualquier archivo con secretos termina replicado en la nube.

## Producción (Somee / IIS)

En IIS las variables se declaran en el `web.config`, dentro del nodo `aspNetCore` que genera
el publish:

```xml
<aspNetCore processPath="dotnet" arguments=".\eGestion360Web.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ConnectionStrings__DefaultConnection" value="<cadena>" />
    <environmentVariable name="EmailSettings__Password" value="<contraseña>" />
    <environmentVariable name="Encryption__Key" value="<clave>" />
    <environmentVariable name="Encryption__IV" value="<iv>" />
  </environmentVariables>
</aspNetCore>
```

Ese `web.config` se edita **en el servidor**, nunca en el repositorio. Ojo con los despliegues:
cada `dotnet publish` genera un `web.config` limpio, así que al subir por FTP hay que **excluirlo**
para no pisar el que tiene las variables, o volver a agregarlas después de subir.

Alternativa si resulta más cómodo: subir una sola vez por FTP un `appsettings.Production.json`
con esos cuatro valores. El servidor lo carga automáticamente porque, sin `ASPNETCORE_ENVIRONMENT`
definida, el entorno es `Production`. Ese archivo está en `.gitignore` y no se sube al repo.

## Pendiente: rotar las credenciales

Las credenciales estuvieron versionadas en `appsettings.json`, así que **siguen visibles en el
historial de git**. Sacarlas del archivo actual no las borra de los commits anteriores. Para
cerrar el tema del todo hay que cambiarlas en origen:

- contraseña de `acc_datos` en el panel de somee.com;
- contraseña de la casilla `egaray@siptecnologia.xyz` en Hostinger;
- `Encryption__Key` / `Encryption__IV` sólo con el procedimiento de recifrado descrito arriba.

## Casilla de notificaciones (2026-08-30)

El correo del sistema pasó a la casilla de Somee. Los valores no secretos ya están en
`appsettings.json`:

| Dato | Valor |
|---|---|
| Remitente / usuario SMTP | `notificaciones@siptecnologia.somee.com` |
| Servidor | `mail.siptecnologia.somee.com` |
| Puerto | `465` (SSL implícito; `587` STARTTLS como alternativa) |

La contraseña va en `EmailSettings__Password` y, cifrada con AES, en la columna `PasswordHash`
de `dbo.EmailConfigurations` (ver `2 - Script SQL/011_configurar_correo_notificaciones.sql`).

`Encryption__Key` y `Encryption__IV` **no existían** hasta esa fecha: se generaron entonces y
quedaron en los *user-secrets* del proyecto para desarrollo local. Como no había ninguna
contraseña cifrada en la base todavía, generarlas no rompió nada; a partir de ahora sí aplica
la advertencia de recifrado de más arriba. Para producción hay que copiar esos dos valores al
`web.config` (o al `appsettings.Production.json`) del servidor.
