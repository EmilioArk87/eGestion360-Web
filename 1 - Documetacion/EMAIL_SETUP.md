# 📧 Configuración de Email para eGestion360

## 🚀 Opciones de Configuración

### 1. **Gmail** (Recomendado para desarrollo)

**Pasos para configurar Gmail:**

1. **Habilitar verificación en 2 pasos** en tu cuenta Gmail
2. **Generar App Password** (contraseña de aplicación):
   - Ve a Google Account → Security → 2-Step Verification → App passwords
   - Genera una nueva app password para "Mail"
3. **Actualizar appsettings.Development.json:**

```json
{
  "EmailSettings": {
    "Provider": "Gmail",
    "FromEmail": "tu-email@gmail.com",
    "FromName": "eGestion360 Sistema",
    "Username": "tu-email@gmail.com", 
    "Password": "abcd efgh ijkl mnop"  // ⚠️ App Password de 16 caracteres
  }
}
```

### 2. **Outlook/Hotmail**

```json
{
  "EmailSettings": {
    "Provider": "SMTP",
    "FromEmail": "tu-email@outlook.com",
    "FromName": "eGestion360 Sistema", 
    "Username": "tu-email@outlook.com",
    "Password": "tu-contraseña",
    "SmtpHost": "smtp-mail.outlook.com",
    "SmtpPort": "587",
    "UseSsl": "true"
  }
}
```

### 3. **SMTP Personalizado** (Otros proveedores)

```json
{
  "EmailSettings": {
    "Provider": "SMTP",
    "FromEmail": "noreply@tudominio.com",
    "FromName": "eGestion360", 
    "Username": "tu-usuario-smtp",
    "Password": "tu-contraseña-smtp",
    "SmtpHost": "mail.tudominio.com",
    "SmtpPort": "587",
    "UseSsl": "true"
  }
}
```

### 4. **Modo Simulación** (Solo para pruebas)

```json
{
  "EmailSettings": {
    "Provider": "Simulation"
  }
}
```

## ⚠️ Configuración de Seguridad

### Gmail - Generar App Password:

1. **Google Account Settings:** https://myaccount.google.com/
2. **Security → 2-Step Verification**
3. **App passwords → Select app: Mail**
4. **Copy the 16-character password** (formato: `abcd efgh ijkl mnop`)

### Outlook - Habilitar SMTP:

1. **Outlook Settings:** https://outlook.live.com/owa/
2. **Mail → Sync email**
3. **Enable POP and IMAP access**

## 🔧 Configuraciones por Proveedor

| Proveedor | SMTP Host | Puerto | SSL |
|-----------|-----------|--------|-----|
| Gmail | smtp.gmail.com | 587 | ✅ |
| Outlook | smtp-mail.outlook.com | 587 | ✅ |
| Yahoo | smtp.mail.yahoo.com | 587 | ✅ |
| GoDaddy | smtpout.secureserver.net | 587 | ✅ |

## 🧪 Probar la Configuración

1. **Configurar email en appsettings.Development.json**
2. **Reiniciar la aplicación**
3. **Ir a `/ForgotPassword`**
4. **Ingresar tu email**
5. **Verificar que llegue el email real**

## 🔒 Seguridad - Variables de Entorno (Recomendado)

En lugar de poner credenciales en archivos, usa variables de entorno:

```bash
# Windows
set EGESTION360_EMAIL_USERNAME=tu-email@gmail.com
set EGESTION360_EMAIL_PASSWORD=tu-app-password

# Linux/Mac
export EGESTION360_EMAIL_USERNAME=tu-email@gmail.com
export EGESTION360_EMAIL_PASSWORD=tu-app-password
```

Luego en código:
```json
{
  "EmailSettings": {
    "Username": "${EGESTION360_EMAIL_USERNAME}",
    "Password": "${EGESTION360_EMAIL_PASSWORD}"
  }
}
```

## 🆘 Troubleshooting

### Error: "Authentication failed"
- ✅ Verificar App Password (Gmail)
- ✅ Habilitar "Less secure apps" (si aplica)
- ✅ Verificar username/password

### Error: "Connection timeout"
- ✅ Verificar SmtpHost y SmtpPort
- ✅ Verificar firewall
- ✅ Probar con UseSsl = "false" (no recomendado)

### Gmail específico:
- ✅ **2-Step Verification** debe estar habilitada
- ✅ Usar **App Password**, no tu contraseña normal
- ✅ Verificar que la cuenta no esté bloqueada

## 📞 Soporte

Si tienes problemas:
1. Verificar logs en la consola de VS Code
2. Probar primero en modo simulación
3. Verificar configuración paso a paso