# 📧 Guía Completa: Configuración de Email Hostinger.es

## 🎯 Descripción General

Esta guía te ayudará a configurar el envío de correos electrónicos en **eGestion360** usando los servidores SMTP de **Hostinger.es**. El sistema incluye procedimientos almacenados, servicios automatizados y una interfaz web amigable.

---

## 🚀 Opciones de Configuración

### **Opción 1: Interfaz Web (Recomendado)**
La forma más fácil es usar la página web dedicada:

**URL:** `/ConfigurarHostinger`

**Características:**
- ✅ Interfaz amigable y guiada
- ✅ Validación automática de datos
- ✅ Gestión de configuraciones existentes
- ✅ Encriptación automática de contraseñas
- ✅ Establecimiento de configuración por defecto

### **Opción 2: Procedimiento Almacenado SQL**
Para usuarios avanzados o automatización:

```sql
EXEC SP_ConfigurarHostingerEmail
    @EmailUsuario = 'miempresa@midominio.com',
    @ContraseñaPlana = 'mi_contraseña_segura',
    @NombreRemitente = 'Mi Empresa - Notificaciones',
    @NombrePerfil = 'Hostinger Producción',
    @EstablecerPorDefecto = 1,
    @Puerto = 587,
    @UsarSSL = 1,
    @CreadoPor = 'Admin';
```

### **Opción 3: Programáticamente desde C#**
Para integración en código:

```csharp
var result = await _emailConfigService.ConfigurarHostingerEmailAsync(
    emailUsuario: "miempresa@midominio.com",
    contraseña: "mi_contraseña_segura",
    nombreRemitente: "Mi Empresa - Sistema",
    nombrePerfil: "Hostinger Principal"
);

if (result.Success) 
{
    Console.WriteLine($"Configuración creada con ID: {result.ConfigurationId}");
}
```

---

## ⚙️ Configuraciones SMTP de Hostinger.es

### **Configuración Recomendada (STARTTLS)**
- **Servidor SMTP:** `smtp.hostinger.com`
- **Puerto:** `587`
- **Encriptación:** `STARTTLS`
- **Autenticación:** `Requerida`
- **Usuario:** Tu email completo
- **Contraseña:** Tu contraseña de email

### **Configuración Alternativa (SSL)**
- **Servidor SMTP:** `smtp.hostinger.com`
- **Puerto:** `465`
- **Encriptación:** `SSL/TLS`
- **Autenticación:** `Requerida`

### **Configuración para Desarrollo**
- **Puerto:** `587` (más compatible)
- **SSL:** `Habilitado`
- **Timeout:** `30 segundos`

---

## 📋 Proceso Paso a Paso

### **Paso 1: Preparar los Datos de Hostinger**

1. **Accede a tu panel de Hostinger.es**
2. **Ve a la sección de Email**
3. **Anota los siguientes datos:**
   - Email completo: `usuario@tudominio.com`
   - Contraseña del email
   - Dominio verificado

### **Paso 2: Ejecutar la Configuración**

#### **Opción A: Via Web (Recomendado)**
1. Ve a `https://tu-aplicacion.com/ConfigurarHostinger`
2. Completa el formulario con tus datos
3. Haz clic en "Configurar Hostinger Email"
4. Verifica el mensaje de éxito

#### **Opción B: Via SQL**
1. Ejecuta el script `setup_hostinger_email_config.sql`
2. Modifica el script `ejecutar_configuracion_hostinger.sql`
3. Ejecuta la configuración personalizada

### **Paso 3: Encriptar Contraseñas**
⚠️ **IMPORTANTE:** Después de crear la configuración:

1. Ve a `/EncryptPasswords`
2. Busca las contraseñas marcadas como "PENDIENTE_ENCRIPTAR"
3. Haz clic en "Encriptar Todas las Contraseñas"
4. Verifica que el estado cambie a "Encriptada"

### **Paso 4: Probar la Configuración**
1. Ve a la sección de pruebas de email
2. Envía un email de prueba
3. Verifica la recepción
4. Prueba el reset de contraseñas

---

## 🔧 Configuraciones Avanzadas

### **Múltiples Perfiles de Email**
Puedes configurar varios perfiles para diferentes propósitos:

```sql
-- Perfil para notificaciones del sistema
EXEC SP_ConfigurarHostingerEmail
    @EmailUsuario = 'notificaciones@empresa.com',
    @NombrePerfil = 'Hostinger Notificaciones',
    @EstablecerPorDefecto = 0;

-- Perfil para marketing
EXEC SP_ConfigurarHostingerEmail
    @EmailUsuario = 'marketing@empresa.com',
    @NombrePerfil = 'Hostinger Marketing',
    @EstablecerPorDefecto = 0;
```

### **Configuración de Respaldo**
Configura múltiples servidores para redundancia:

```sql
-- Configuración principal
EXEC SP_ConfigurarHostingerEmail
    @EmailUsuario = 'principal@empresa.com',
    @NombrePerfil = 'Hostinger Principal',
    @EstablecerPorDefecto = 1;

-- Configuración de respaldo
EXEC SP_ConfigurarHostingerEmail
    @EmailUsuario = 'respaldo@empresa.com',
    @NombrePerfil = 'Hostinger Respaldo',
    @EstablecerPorDefecto = 0;
```

---

## 🛠️ Solución de Problemas Comunes

### **Error: "Autenticación fallida"**
**Causa:** Credenciales incorrectas
**Solución:**
- Verifica el email y contraseña en el panel de Hostinger
- Asegúrate de que el email está activo
- Verifica que no hay caracteres especiales problemáticos

### **Error: "Conexión rechazada"**
**Causa:** Puerto bloqueado o configuración incorrecta
**Solución:**
- Prueba puerto 587 en lugar de 465
- Verifica que SSL esté habilitado
- Contacta a tu ISP si usa puerto 25

### **Error: "Timeout de conexión"**
**Causa:** Firewall o configuración de red
**Solución:**
- Verifica la conectividad a internet
- Permite el tráfico SMTP en el firewall
- Aumenta el timeout a 60 segundos

### **Emails no enviados**
**Verificaciones:**
1. **Estado de la configuración:** ¿Está activa?
2. **Contraseña:** ¿Está correctamente encriptada?
3. **Configuración por defecto:** ¿Hay una establecida?
4. **Logs del sistema:** Revisar errores detallados

---

## 📚 Consultas Útiles para Administración

### **Ver todas las configuraciones**
```sql
SELECT 
    Id,
    ProfileName as [Perfil],
    FromEmail as [Email],
    SmtpHost as [Servidor],
    SmtpPort as [Puerto],
    IsActive as [Activo],
    IsDefault as [Por Defecto],
    CASE 
        WHEN PasswordHash LIKE 'PENDIENTE_ENCRIPTAR:%' THEN 'Pendiente'
        WHEN LEN(PasswordHash) > 50 THEN 'Encriptada'
        ELSE 'Sin Encriptar'
    END as [Estado Contraseña],
    CreatedAt,
    LastTestedAt
FROM EmailConfiguration 
WHERE SmtpHost LIKE '%hostinger%'
ORDER BY CreatedAt DESC;
```

### **Establecer configuración por defecto**
```sql
-- Quitar por defecto de todas
UPDATE EmailConfiguration SET IsDefault = 0;

-- Establecer nueva por defecto
UPDATE EmailConfiguration 
SET IsDefault = 1, UpdatedAt = GETUTCDATE()
WHERE Id = [ID_DE_LA_CONFIGURACION];
```

### **Desactivar configuración**
```sql
UPDATE EmailConfiguration 
SET IsActive = 0, UpdatedAt = GETUTCDATE()
WHERE Id = [ID_DE_LA_CONFIGURACION];
```

### **Ver estadísticas de uso**
```sql
SELECT 
    ProfileName,
    TestEmailsSent as [Emails de Prueba],
    LastTestedAt as [Última Prueba],
    DATEDIFF(DAY, CreatedAt, GETDATE()) as [Días Desde Creación]
FROM EmailConfiguration 
WHERE SmtpHost LIKE '%hostinger%'
ORDER BY TestEmailsSent DESC;
```

---

## 🔐 Mejores Prácticas de Seguridad

### **Contraseñas**
- ✅ Usa contraseñas fuertes y únicas
- ✅ Cambia las contraseñas regularmente
- ✅ No compartas las credenciales
- ✅ El sistema encripta automáticamente las contraseñas

### **Configuración del Servidor**
- ✅ Siempre usa SSL/TLS (puerto 587 o 465)
- ✅ Evita el puerto 25 en producción
- ✅ Configura timeouts apropiados
- ✅ Monitora los logs de envío

### **Gestión de Perfiles**
- ✅ Usa nombres descriptivos para los perfiles
- ✅ Establece una sola configuración por defecto
- ✅ Desactiva configuraciones no utilizadas
- ✅ Documenta el propósito de cada perfil

---

## 📞 Soporte y Contacto

### **Logs del Sistema**
Los errores se registran automáticamente en:
- Logs de aplicación de .NET
- Tabla de configuraciones de email
- Estadísticas de pruebas de envío

### **Verificación de Estado**
Para verificar el estado del sistema de email:
1. Ve a `/ConfigurarHostinger` para ver configuraciones
2. Ejecuta `/EncryptPasswords` para verificar encriptación
3. Revisa los logs de la aplicación para errores

---

## 📝 Notas de la Versión

### **Características Implementadas:**
- ✅ Procedimiento almacenado automatizado
- ✅ Interfaz web amigable  
- ✅ Encriptación automática de contraseñas
- ✅ Gestión de múltiples perfiles
- ✅ Validaciones de entrada robustas
- ✅ Manejo de errores completo

### **Próximas Mejoras:**
- 🔄 Pruebas automáticas de conectividad
- 🔄 Notificaciones de estado de email
- 🔄 Panel de estadísticas avanzado
- 🔄 Integración con otros proveedores de email

---

**¡Ya tienes tu configuración de Hostinger.es lista para usar! 🚀**