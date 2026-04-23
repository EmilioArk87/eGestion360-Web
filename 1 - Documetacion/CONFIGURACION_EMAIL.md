# 📧 Configuración de Email Corporativo - eGestion360

## 🎯 Objetivo
Este sistema permite configurar el email corporativo para enviar notificaciones automáticas del sistema, como códigos de recuperación de contraseñas.

## 📋 Datos Necesarios

### 📨 Información Básica
- **Nombre del Perfil**: Un nombre descriptivo para identificar esta configuración
- **Email Corporativo**: La dirección de email de su empresa (ej: `info@suempresa.com`)
- **Nombre del Remitente**: El nombre que aparecerá en los emails (ej: `"Sistema eGestion360 - Su Empresa"`)

### 🌐 Configuración SMTP

#### Para Gmail Empresarial:
- **Servidor SMTP**: `smtp.gmail.com`
- **Puerto**: `587`
- **SSL/TLS**: Habilitado
- **Usuario**: Su email completo
- **Contraseña**: Contraseña de aplicación específica (no la contraseña normal)

**⚠️ Importante para Gmail:**
1. Habilitar autenticación de 2 factores
2. Generar "Contraseña de aplicación" específica
3. Usar la contraseña de aplicación, NO su contraseña personal

#### Para Outlook/Hotmail Empresarial:
- **Servidor SMTP**: `smtp-mail.outlook.com`
- **Puerto**: `587`
- **SSL/TLS**: Habilitado
- **Usuario**: Su email completo
- **Contraseña**: Contraseña de la cuenta o contraseña de aplicación

#### Para SMTP Personalizado:
- **Servidor SMTP**: Proporcione la dirección de su servidor
- **Puerto**: Comúnmente `587` (TLS) o `465` (SSL)
- **SSL/TLS**: Según configuración de su servidor
- **Usuario**: Según su proveedor
- **Contraseña**: Contraseña proporcionada por su proveedor

## 🔧 Configuración en el Sistema

### Paso 1: Acceder a la Configuración
1. Inicie sesión en eGestion360
2. Vaya al Menú Principal
3. En la sección "Administración", haga clic en "Email Corporativo"
4. URL directa: `http://localhost:5000/admin/email-config`

### Paso 2: Crear Nueva Configuración
1. Haga clic en "Nueva Configuración"
2. Complete todos los campos requeridos
3. Seleccione el proveedor adecuado (Gmail, Outlook, SMTP)
4. El sistema autocompletará valores por defecto según el proveedor
5. Ingrese su contraseña (se encriptará automáticamente)
6. Marque "Establecer como predeterminada" si es su configuración principal

### Paso 3: Probar la Configuración
1. Después de guardar, aparecerá en la lista de configuraciones
2. Haga clic en el botón "Probar" (📧)
3. El sistema enviará un email de prueba a la dirección configurada
4. Verifique que recibió el email correctamente

### Paso 4: Activar y Establecer como Predeterminada
1. Si la prueba fue exitosa, active la configuración
2. Establézcala como predeterminada para que el sistema la use
3. Todas las notificaciones automáticas usarán esta configuración

## 🔒 Seguridad

### Encriptación de Contraseñas
- Las contraseñas SMTP se encriptan usando AES-256
- Nunca se almacenan en texto plano
- Se descifran solo durante el envío de emails

### Mejores Prácticas
- Use contraseñas de aplicación específicas, no contraseñas principales
- Active autenticación de 2 factores en su cuenta de email
- Revise regularmente los logs de envío
- Mantenga actualizadas las credenciales

## 🔍 Solución de Problemas

### Error: "Autenticación fallida"
- Verifique usuario y contraseña
- Para Gmail: Use contraseña de aplicación
- Verifique que 2FA esté habilitado (Gmail)

### Error: "No se puede conectar al servidor"
- Verifique servidor SMTP y puerto
- Confirme configuración SSL/TLS
- Verifique conectividad de red

### Error: "Email no se envía"
- Revise configuración activa y predeterminada
- Verifique que la configuración esté habilitada
- Revise logs del sistema

### Emails no llegan al destinatario
- Verifique carpeta de spam
- Confirme dirección de destinatario
- Revise límites de envío del proveedor

## 📊 Monitoreo y Estadísticas

### Panel de Control
- Total de configuraciones
- Configuraciones activas
- Estado del sistema
- Total de emails de prueba enviados

### Auditoría
- Fecha de última prueba
- Número de emails enviados
- Estado de cada configuración

## 🔄 Mantenimiento

### Actualizaciones Regulares
- Renovar contraseñas periódicamente
- Verificar estado de configuraciones
- Probar envío mensualmente

### Respaldos
- Documentar configuraciones importantes
- Mantener credenciales actualizadas
- Tener configuración alternativa

## 📞 Soporte

Si necesita ayuda adicional:
1. Revise los logs del sistema
2. Pruebe con configuración mínima primero
3. Contacte al administrador del sistema
4. Consulte documentación del proveedor SMTP

---

**Desarrollado por:** SIP Tecnología  
**Sistema:** eGestion360  
**Versión:** 1.0  
**Fecha:** Enero 2024