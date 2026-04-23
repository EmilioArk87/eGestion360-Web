-- ==================================================
-- DIAGNÓSTICO RÁPIDO: Estado del Sistema de Emails
-- FECHA: 2026-04-12
-- PROPÓSITO: Verificar por qué falla el envío de emails
-- ==================================================

PRINT '🔍 DIAGNÓSTICO RÁPIDO - Sistema de Emails';
PRINT '=========================================';

-- 1. ¿Existe la tabla EmailConfiguration?
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmailConfiguration')
BEGIN
    PRINT '✅ Tabla EmailConfiguration existe';
    
    -- 2. ¿Hay configuraciones en la tabla?
    DECLARE @TotalConfigs INT = 0;
    DECLARE @ActiveConfigs INT = 0;
    DECLARE @DefaultConfigs INT = 0;
    
    SELECT @TotalConfigs = COUNT(*) FROM EmailConfiguration;
    SELECT @ActiveConfigs = COUNT(*) FROM EmailConfiguration WHERE IsActive = 1;
    SELECT @DefaultConfigs = COUNT(*) FROM EmailConfiguration WHERE IsDefault = 1 AND IsActive = 1;
    
    PRINT '';
    PRINT '📊 ESTADÍSTICAS DE CONFIGURACIONES:';
    PRINT '   Total configuraciones: ' + CAST(@TotalConfigs AS NVARCHAR(10));
    PRINT '   Configuraciones activas: ' + CAST(@ActiveConfigs AS NVARCHAR(10));
    PRINT '   Configuraciones por defecto: ' + CAST(@DefaultConfigs AS NVARCHAR(10));
    
    -- 3. Mostrar configuraciones existentes
    IF @TotalConfigs > 0
    BEGIN
        PRINT '';
        PRINT '📋 CONFIGURACIONES EXISTENTES:';
        SELECT 
            Id,
            ProfileName AS [Perfil],
            Provider,
            FromEmail AS [Email],
            SmtpHost AS [Servidor],
            SmtpPort AS [Puerto],
            CASE WHEN UseSsl = 1 THEN 'Sí' ELSE 'No' END AS [SSL],
            CASE WHEN IsActive = 1 THEN 'Activa' ELSE 'Inactiva' END AS [Estado],
            CASE WHEN IsDefault = 1 THEN 'Sí' ELSE 'No' END AS [Default],
            CASE 
                WHEN PasswordHash = 'CONFIGURAR_CONTRASEÑA_ENCRIPTADA' THEN '❌ Sin contraseña'
                WHEN PasswordHash LIKE 'PENDIENTE_%' THEN '⏳ Pendiente'
                WHEN LEN(PasswordHash) > 50 THEN '✅ Encriptada'
                ELSE '❓ Desconocido'
            END AS [Contraseña],
            CreatedAt AS [Creada],
            TestEmailsSent AS [Emails Enviados]
        FROM EmailConfiguration 
        ORDER BY IsDefault DESC, IsActive DESC, Id;
    END
    ELSE
    BEGIN
        PRINT '❌ PROBLEMA: No hay configuraciones en la tabla EmailConfiguration';
        PRINT '   SOLUCIÓN: Ejecutar create_email_configuration_table.sql BLOQUE 4 (datos iniciales)';
        PRINT '            o usar el procedimiento SP_ConfigurarHostingerEmail';
    END
    
    -- 4. Verificar si hay configuraciones problemáticas
    IF @ActiveConfigs = 0 AND @TotalConfigs > 0
    BEGIN
        PRINT '';
        PRINT '❌ PROBLEMA CRÍTICO: Hay configuraciones pero ninguna está activa';
        PRINT '   SOLUCIÓN: Activar una configuración con:';
        PRINT '   UPDATE EmailConfiguration SET IsActive = 1, IsDefault = 1 WHERE Id = [ID_DESEADO]';
    END
    
    IF @DefaultConfigs = 0 AND @ActiveConfigs > 0
    BEGIN
        PRINT '';
        PRINT '⚠️ ADVERTENCIA: Hay configuraciones activas pero ninguna es por defecto';
        PRINT '   SOLUCIÓN: Establecer una como default:';
        PRINT '   EXEC sp_SetDefaultEmailConfiguration @Id = [ID_CONFIGURACION_ACTIVA]';
    END
    
    -- 5. Verificar contraseñas encriptadas
    DECLARE @ConfigsSinPassword INT = 0;
    SELECT @ConfigsSinPassword = COUNT(*) 
    FROM EmailConfiguration 
    WHERE IsActive = 1 
      AND (PasswordHash = 'CONFIGURAR_CONTRASEÑA_ENCRIPTADA' 
           OR PasswordHash LIKE 'PENDIENTE_%'
           OR LEN(PasswordHash) < 50);
    
    IF @ConfigsSinPassword > 0
    BEGIN
        PRINT '';
        PRINT '❌ PROBLEMA: Configuraciones activas sin contraseña encriptada: ' + CAST(@ConfigsSinPassword AS NVARCHAR(10));
        PRINT '   SOLUCIÓN: Encriptar contraseñas usando /EncryptPasswords en la web';
        PRINT '            o ejecutar setup_hostinger_email_config.sql';
    END
END
ELSE
BEGIN
    PRINT '❌ PROBLEMA CRÍTICO: La tabla EmailConfiguration no existe';
    PRINT '   SOLUCIÓN: Ejecutar create_email_configuration_table.sql completo';
END

PRINT '';

-- 6. Verificar configuración de appsettings (simulación)
PRINT '⚙️ CONFIGURACIÓN DE FALLBACK (appsettings.json):';
PRINT '   El sistema usa "Simulation" como fallback si no hay configuración en BD';
PRINT '   Para activar email real, necesitas:';
PRINT '   1. Configuración activa en la tabla EmailConfiguration';
PRINT '   2. Contraseña encriptada válida';
PRINT '   3. Servidor SMTP accesible (ej: smtp.hostinger.com)';

PRINT '';
PRINT '🚀 PRÓXIMOS PASOS RECOMENDADOS:';
PRINT '==============================';

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmailConfiguration')
BEGIN
    PRINT '1. ⚡ URGENTE: Ejecutar create_email_configuration_table.sql';
    PRINT '2. 📧 Configurar Hostinger: Ejecutar setup_hostinger_email_config.sql';
    PRINT '3. 🔐 Encriptar contraseñas: Usar /EncryptPasswords en la web';
    PRINT '4. ✅ Validar: Ejecutar validacion_completa_emails.sql';
END
ELSE
BEGIN
    -- Si la tabla existe pero no hay configuraciones activas
    IF NOT EXISTS (SELECT 1 FROM EmailConfiguration WHERE IsActive = 1 AND LEN(PasswordHash) > 50)
    BEGIN
        PRINT '1. 📧 Configurar email: Usar procedimiento SP_ConfigurarHostingerEmail';
        PRINT '2. 🔐 Encriptar contraseñas: /EncryptPasswords o script SQL';
        PRINT '3. ✅ Activar configuración válida';
        PRINT '4. 🧪 Probar envío: /ValidarEmails en la web';
    END
    ELSE
    BEGIN
        PRINT '1. 🧪 Probar configuración: /ValidarEmails en la web';
        PRINT '2. 📊 Revisar logs de la aplicación';
        PRINT '3. ✅ Verificar conectividad SMTP';
    END
END

PRINT '';
PRINT '✅ DIAGNÓSTICO COMPLETADO';
PRINT '========================';