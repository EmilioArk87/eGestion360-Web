-- =====================================================================
-- CONFIGURACIÓN PREVIA PARA egaray@siptecnologia.xyz
-- =====================================================================
-- Este script inserta una configuración de ejemplo para el email
-- de SIP Tecnología que el usuario puede completar fácilmente
-- =====================================================================

USE [eGestion360];

PRINT '📧 Configurando email para SIP Tecnología...';

-- Verificar si ya existe configuración para este email
IF NOT EXISTS (SELECT 1 FROM EmailConfigurations WHERE FromEmail = 'egaray@siptecnologia.xyz')
BEGIN
    PRINT '📝 Insertando configuración para egaray@siptecnologia.xyz...';
    
    INSERT INTO EmailConfigurations (
        ProfileName,
        Provider,
        FromEmail,
        FromName,
        SmtpHost,
        SmtpPort,
        UseSsl,
        Username,
        PasswordHash,
        IsActive,
        IsDefault,
        CreatedAt,
        UpdatedAt,
        CreatedBy,
        TestEmailsSent
    )
    VALUES (
        'SIP Tecnología - eGaray',                       -- Nombre del perfil
        'SMTP',                                          -- Proveedor SMTP personalizado
        'egaray@siptecnologia.xyz',                     -- Email corporativo
        'eGestion360 - SIP Tecnología',                -- Nombre del remitente
        'mail.siptecnologia.xyz',                       -- Servidor SMTP (debe confirmar)
        587,                                             -- Puerto SMTP estándar
        1,                                               -- SSL habilitado
        'egaray@siptecnologia.xyz',                     -- Usuario SMTP
        'CONFIGURAR_CONTRASEÑA_EN_INTERFAZ_WEB',        -- Placeholder para contraseña
        0,                                               -- Inactiva hasta configurar contraseña
        1,                                               -- Establecer como predeterminada
        GETDATE(),                                       -- Fecha creación
        GETDATE(),                                       -- Fecha actualización
        'Setup Script',                                  -- Creado por script
        0                                                -- Emails de prueba enviados
    );

    PRINT '✅ Configuración creada para SIP Tecnología';
    PRINT '';
    PRINT '📋 SIGUIENTES PASOS:';
    PRINT '1. Vaya a: http://localhost:5000/admin/email-config';
    PRINT '2. Edite la configuración "SIP Tecnología - eGaray"';
    PRINT '3. Ingrese la contraseña real del email';
    PRINT '4. Confirme el servidor SMTP correcto';
    PRINT '5. Active la configuración';
    PRINT '6. Pruebe el envío de email';
END
ELSE
BEGIN
    PRINT '⚠️ Ya existe configuración para egaray@siptecnologia.xyz';
    
    -- Mostrar configuración actual
    SELECT 
        ProfileName,
        Provider,
        FromEmail,
        SmtpHost,
        SmtpPort,
        IsActive,
        IsDefault,
        CASE 
            WHEN PasswordHash = 'CONFIGURAR_CONTRASEÑA_EN_INTERFAZ_WEB' 
            THEN '❌ Contraseña sin configurar'
            ELSE '✅ Contraseña configurada'
        END as EstadoContrasena,
        TestEmailsSent as EmailsPrueba
    FROM EmailConfigurations
    WHERE FromEmail = 'egaray@siptecnologia.xyz';
END

PRINT '';
PRINT '=================================================================';
PRINT '📧 INFORMACIÓN PARA SIP TECNOLOGÍA';
PRINT '=================================================================';
PRINT '';
PRINT '🔧 CONFIGURACIÓN SMTP SUGERIDA:';
PRINT '   Email: egaray@siptecnologia.xyz';
PRINT '   Servidor: mail.siptecnologia.xyz (verificar con proveedor)';
PRINT '   Puerto: 587 (STARTTLS) o 465 (SSL)';
PRINT '   Seguridad: SSL/TLS habilitado';
PRINT '   Usuario: egaray@siptecnologia.xyz';
PRINT '';
PRINT '📋 ALTERNATIVAS COMUNES PARA DOMINIOS PERSONALIZADOS:';
PRINT '   - mail.siptecnologia.xyz:587';
PRINT '   - smtp.siptecnologia.xyz:587';  
PRINT '   - smtp.gmail.com:587 (si usa G Suite)';
PRINT '   - smtp-mail.outlook.com:587 (si usa Office 365)';
PRINT '';
PRINT '🔐 CONTRASEÑA:';
PRINT '   - Use la contraseña del email o contraseña de aplicación';
PRINT '   - Para Gmail/G Suite: genere contraseña de aplicación';
PRINT '   - Para Office 365: puede usar contraseña normal o de app';
PRINT '';
PRINT '✅ DESPUÉS DE CONFIGURAR:';
PRINT '   1. Pruebe el envío desde la interfaz web';
PRINT '   2. Vaya a "Olvidé mi contraseña" para probar recuperación';
PRINT '   3. Los emails se enviarán desde egaray@siptecnologia.xyz';
PRINT '';
PRINT '=================================================================';

-- Mostrar estado general del sistema
PRINT '';
PRINT '📊 ESTADO ACTUAL DEL SISTEMA:';

SELECT 
    'EmailConfigurations' as Tabla,
    COUNT(*) as Total,
    COUNT(CASE WHEN IsActive = 1 THEN 1 END) as Activas,
    COUNT(CASE WHEN IsDefault = 1 THEN 1 END) as Predeterminadas
FROM EmailConfigurations;

SELECT 
    'Users' as Tabla,
    COUNT(*) as Total
FROM Users;