-- =================================================================
-- CONFIGURACIÓN RÁPIDA DE HOSTINGER PARA RECUPERACIÓN DE CONTRASEÑA
-- FECHA: 2026-04-12  
-- PROPÓSITO: Configurar email de Hostinger para que funcione el reset
-- =================================================================

PRINT '📧 CONFIGURANDO EMAIL DE HOSTINGER PARA RECUPERACIÓN';
PRINT '===================================================';

-- PASO 1: Limpiar configuraciones problemáticas
UPDATE EmailConfiguration 
SET IsActive = 0, IsDefault = 0, UpdatedAt = GETUTCDATE()
WHERE IsActive = 0 AND IsDefault = 1;

PRINT '🧹 Configuraciones problemáticas limpiadas';

-- PASO 2: Insertar/Actualizar configuración de Hostinger
-- Cambiar estos valores por los de tu cuenta real de Hostinger:
DECLARE @EmailHostinger NVARCHAR(100) = 'tu-email@tu-dominio.com';  -- 🔴 CAMBIAR POR TU EMAIL
DECLARE @PasswordTemporal NVARCHAR(200) = 'PENDIENTE_ENCRIPTAR_HOSTINGER_2026';

-- Verificar si ya existe una configuración de Hostinger
IF EXISTS (SELECT 1 FROM EmailConfiguration WHERE SmtpHost = 'smtp.hostinger.com')
BEGIN
    PRINT '✏️ Actualizando configuración de Hostinger existente...';
    
    UPDATE EmailConfiguration 
    SET 
        FromEmail = @EmailHostinger,
        FromName = 'eGestion360 - Recuperación de Contraseña',
        Username = @EmailHostinger,
        PasswordHash = @PasswordTemporal,
        IsActive = 1,
        IsDefault = 1,
        UpdatedAt = GETUTCDATE()
    WHERE SmtpHost = 'smtp.hostinger.com';
    
    PRINT '✅ Configuración de Hostinger actualizada';
END
ELSE
BEGIN
    PRINT '➕ Creando nueva configuración de Hostinger...';
    
    INSERT INTO EmailConfiguration (
        ProfileName, Provider, FromEmail, FromName, SmtpHost, SmtpPort, 
        UseSsl, Username, PasswordHash, IsActive, IsDefault, CreatedBy
    ) VALUES (
        'Hostinger Principal',           -- ProfileName
        'SMTP',                         -- Provider  
        @EmailHostinger,                -- FromEmail 🔴 CAMBIAR
        'eGestion360 - Recuperación',   -- FromName
        'smtp.hostinger.com',           -- SmtpHost
        587,                            -- SmtpPort
        1,                              -- UseSsl
        @EmailHostinger,                -- Username 🔴 CAMBIAR  
        @PasswordTemporal,              -- PasswordHash (temporal)
        1,                              -- IsActive
        1,                              -- IsDefault
        'Sistema Auto-Config'           -- CreatedBy
    );
    
    PRINT '✅ Nueva configuración de Hostinger creada';
END

-- PASO 3: Verificar resultado
PRINT '';
PRINT '📊 CONFIGURACIÓN ACTUAL:';
SELECT 
    Id,
    ProfileName AS [Perfil],
    FromEmail AS [Email],
    SmtpHost AS [Servidor],
    SmtpPort AS [Puerto],
    CASE WHEN IsActive = 1 THEN '✅ Activa' ELSE '❌ Inactiva' END AS [Estado],
    CASE WHEN IsDefault = 1 THEN '⭐ Default' ELSE '' END AS [Default],
    CASE 
        WHEN PasswordHash LIKE 'PENDIENTE_%' THEN '⏳ Encriptar Pendiente'
        WHEN LEN(PasswordHash) > 50 THEN '✅ Encriptada'
        ELSE '❌ Sin Configurar'
    END AS [Contraseña]
FROM EmailConfiguration 
WHERE IsActive = 1
ORDER BY IsDefault DESC;

PRINT '';
PRINT '🔥 PASOS FINALES REQUERIDOS:';
PRINT '============================';
PRINT '1. 🔴 CAMBIAR el email en este script por tu email real de Hostinger';
PRINT '2. 🔐 Ir a /EncryptPasswords en la web para encriptar la contraseña';
PRINT '3. 🧪 Probar en /ValidarEmails';
PRINT '4. ✅ Intentar recuperación de contraseña nuevamente';
PRINT '';
PRINT '📋 DATOS DE HOSTINGER TÍPICOS:';
PRINT '   Servidor: smtp.hostinger.com';
PRINT '   Puerto: 587';
PRINT '   SSL: Sí';
PRINT '   Email: tu-cuenta@tu-dominio.com';
PRINT '   Contraseña: La de tu panel de Hostinger';

PRINT '';
PRINT '✅ CONFIGURACIÓN DE HOSTINGER COMPLETADA';
PRINT '=======================================';