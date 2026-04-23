# 📧 Guía Completa: Validar Envío de Correos Electrónicos

## 🎯 Métodos de Validación Disponibles

### 🌐 **1. Interfaz Web de Validación** (Recomendado)
**URL:** `/ValidarEmails`

**Características:**
- ✅ **Prueba Simple:** Envío de email básico 
- ✅ **Prueba Completa:** Múltiples tipos de email (reset, confirmación, HTML)
- ✅ **Validación de Configuraciones:** Probar cada configuración individualmente
- ✅ **Dashboard en Tiempo Real:** Estado actual del sistema
- ✅ **Historial de Pruebas:** Resultados recientes de envíos

**Cómo Usar:**
1. Ve a `https://tu-aplicacion.com/ValidarEmails`
2. Introduce tu email personal en "Email de Destino"
3. **Prueba Simple:** Envía un email básico
4. **Prueba Completa:** Envía 4 tipos diferentes de email
5. Revisa tu bandeja de entrada y carpeta de spam

---

### 📊 **2. Consultas SQL de Estadísticas**
**Archivo:** `sql_scripts/consultas_email_stats.sql`

**Información que Proporciona:**
- 📈 Resumen general del sistema
- 🔧 Estado de todas las configuraciones
- 📨 Estadísticas de emails enviados
- 🔑 Análisis de códigos de reset
- 🚨 Diagnóstico de problemas
- 💡 Recomendaciones de mantenimiento

**Ejecución:**
```sql
-- En SQL Server Management Studio
-- Ejecutar: sql_scripts/consultas_email_stats.sql
```

---

### 🔍 **3. Logs de Aplicación**

#### **Logs en Visual Studio / Terminal:**
```
Información: Código de reset enviado exitosamente a usuario@ejemplo.com
Error: Error enviando código de reset a usuario@ejemplo.com
Información: Email enviado exitosamente usando configuración Hostinger Principal
```

#### **Buscar Logs Específicos:**
```csharp
// En el código, los logs están categorizados por:
_logger.LogInformation("Email enviado exitosamente usando configuración {ProfileName}", config.ProfileName);
_logger.LogError("Error enviando email con configuración de BD para {ProfileName}", config.ProfileName);
```

---

### 🔧 **4. Validación Manual por Pasos**

#### **Paso 1: Verificar Configuración Base**
```sql
-- Verificar que hay configuración activa
SELECT 
    Id, ProfileName, IsActive, IsDefault, 
    SmtpHost, SmtpPort, UseSsl,
    CASE 
        WHEN PasswordHash LIKE 'PENDIENTE_%' THEN 'Sin Encriptar'
        WHEN LEN(PasswordHash) > 50 THEN 'Encriptada'
        ELSE 'Problema'
    END as EstadoContraseña
FROM EmailConfiguration 
WHERE IsActive = 1;
```

#### **Paso 2: Verificar Conectividad SMTP**
```powershell
# Probar conectividad al servidor
Test-NetConnection -ComputerName smtp.hostinger.com -Port 587

# Resultado esperado: TcpTestSucceeded: True
```

#### **Paso 3: Verificar Contraseñas**
```sql
-- Ver contraseñas sin encriptar
SELECT Id, ProfileName, PasswordHash 
FROM EmailConfiguration 
WHERE PasswordHash LIKE 'PENDIENTE_%' 
   OR PasswordHash = 'CONFIGURAR_CONTRASEÑA_ENCRIPTADA';
```
**Si hay resultados:** Ve a `/EncryptPasswords` y encrípta todas.

#### **Paso 4: Prueba de Envío Directo**
```csharp
// En una página o controlador personalizado
var result = await _emailService.SendTestEmailAsync(
    "tu-email@ejemplo.com", 
    "Prueba Manual", 
    "Este es un mensaje de prueba manual"
);

if (result)
    Console.WriteLine("✅ Email enviado exitosamente");
else
    Console.WriteLine("❌ Error enviando email");
```

---

### 📨 **5. Validación Desde el Destinatario**

#### **Verificaciones en el Email Recibido:**
- ✅ **Bandeja de Entrada:** ¿Llegó el email?
- ✅ **Carpeta de Spam:** ¿Fue marcado como spam?
- ✅ **Formato HTML:** ¿Se ve correctamente el HTML?
- ✅ **Remitente:** ¿Aparece el nombre correcto?
- ✅ **Enlaces:** ¿Los enlaces funcionan? (si los hay)

#### **Señales de Problemas:**
- ❌ **No llega ningún email:** Problema de configuración o credenciales
- ❌ **Llega a spam:** Configurar SPF/DKIM en DNS del dominio
- ❌ **HTML mal formateado:** Problema en la plantilla
- ❌ **Remitente incorrecto:** Verificar FromName/FromEmail

---

## 🛠️ **Herramientas de Diagnóstico**

### **Errores Comunes y Soluciones:**

#### **1. "Autenticación fallida"**
```
Error: AuthenticationException: Authentication failed
```
**Verificar:**
- ✅ Credenciales en Hostinger (usuario/contraseña)
- ✅ Contraseña encriptada correctamente
- ✅ Usuario exacto (normalmente = email completo)

**Solución:**
```sql
-- Verificar usuario y reencriptar si necesario
SELECT Username, FromEmail FROM EmailConfiguration WHERE Id = [TU_ID];
-- Si Usuario ≠ FromEmail, corregir
UPDATE EmailConfiguration SET Username = FromEmail WHERE Id = [TU_ID];
```

#### **2. "Conexión rechazada"**
```
Error: SocketException: Connection refused
```
**Verificar:**
- ✅ Puerto (587 o 465)
- ✅ Servidor (smtp.hostinger.com)
- ✅ SSL habilitado
- ✅ Firewall de la red

**Solución:**
```sql
-- Cambiar a puerto alternativo si es necesario
UPDATE EmailConfiguration 
SET SmtpPort = 465, UseSsl = 1 
WHERE SmtpHost LIKE '%hostinger%';
```

#### **3. "Timeout de conexión"**
```
Error: TimeoutException: Connection timed out
```
**Causas:**
- 🔥 Firewall corporativo bloquea SMTP
- 📡 ISP bloquea los puertos de email
- ⏱️ Servidor Hostinger saturado

**Solución:**
```csharp
// Aumentar timeout en código (si es necesario)
using (var client = new SmtpClient())
{
    client.Timeout = 60000; // 60 segundos
    // ... resto del código
}
```

---

## 📊 **Dashboard de Estado**

### **Indicadores Clave:**
```sql
-- KPIs principales
SELECT 
    'Total Configuraciones' as Metrica, COUNT(*) as Valor 
FROM EmailConfiguration
UNION ALL
SELECT 'Configuraciones Activas', COUNT(*) 
FROM EmailConfiguration WHERE IsActive = 1
UNION ALL
SELECT 'Emails Enviados (Total)', SUM(TestEmailsSent) 
FROM EmailConfiguration
UNION ALL
SELECT 'Configuraciones Encriptadas', COUNT(*) 
FROM EmailConfiguration 
WHERE PasswordHash NOT LIKE 'PENDIENTE_%' 
  AND PasswordHash != 'CONFIGURAR_CONTRASEÑA_ENCRIPTADA';
```

### **Estado de Salud del Sistema:**
- 🟢 **Verde (Saludable):** ≥1 configuración activa y encriptada
- 🟡 **Amarillo (Advertencia):** Configuraciones sin encriptar o sin probar
- 🔴 **Rojo (Problema):** Sin configuraciones activas o errores críticos

---

## ⚡ **Flujo de Validación Rápida**

### **Validación en 5 Minutos:**
```bash
# 1. Verificar configuración (30 seg)
URL: /ValidarEmails

# 2. Estado rápido (30 seg)
SQL: SELECT COUNT(*) FROM EmailConfiguration WHERE IsActive = 1;

# 3. Prueba simple (2 min)
- Introducir tu email
- Hacer clic en "Prueba Simple"
- Verificar recepción

# 4. Revisar logs (1 min)
- Ver consola de aplicación
- Buscar errores recientes

# 5. Confirmar resultado (1 min)
- ✅ Email recibido = Sistema funcionando
- ❌ No recibido = Revisar diagnóstico
```

---

## 🔄 **Monitoreo Continuo**

### **Automatización Recomendada:**
```csharp
// Crear una tarea programada que ejecute esto diariamente
public async Task<bool> ValidarSistemEmailDiario()
{
    var config = await _emailConfigService.GetActiveConfigurationAsync();
    if (config == null) return false;
    
    var result = await _emailService.SendTestEmailAsync(
        "admin@tuempresa.com", 
        "Validación Automática Diaria", 
        $"Sistema funcionando correctamente el {DateTime.Now:dd/MM/yyyy}"
    );
    
    _logger.LogInformation("Validación diaria: {Resultado}", result ? "EXITOSA" : "FALLIDA");
    return result;
}
```

### **Alertas Automáticas:**
- 📧 **Email diario:** Estado del sistema
- 🔔 **Slack/Teams:** Si falla validación
- 📊 **Dashboard:** Estado en tiempo real

---

## 📚 **Recursos Adicionales**

### **Páginas de Gestión:**
- 🌐 `/ValidarEmails` - Panel principal de validación
- 🔧 `/ConfigurarHostinger` - Gestión de configuraciones
- 🔐 `/EncryptPasswords` - Encriptación de contraseñas
- 📊 `/DebugUsers` - Información de usuarios (si existe)

### **Scripts SQL:**
- 📊 `consultas_email_stats.sql` - Estadísticas completas
- 🔧 `setup_hostinger_email_config.sql` - Configurar Hostinger
- 🔐 `encrypt_passwords.sql` - Encriptación masiva

### **Logs a Revisar:**
- 📝 Logs de aplicación .NET
- 📧 Estadísticas en tabla EmailConfiguration
- 🔑 Códigos en tabla PasswordResetCodes

---

**¡Tu sistema de validación está completo! 🚀**

Usa `/ValidarEmails` para validación interactiva y las consultas SQL para análisis detallado.