-- =====================================================================
-- SCRIPT COMPLETO DE CONFIGURACIÓN - eGestion360
-- =====================================================================
-- Este script configura completamente la base de datos para el sistema
-- de gestión de emails corporativos y reset de contraseñas
-- =====================================================================

USE [eGestion360];

PRINT '🚀 Iniciando configuración completa de eGestion360...';
PRINT '';

-- =====================================================================
-- 1. VERIFICAR TABLAS EXISTENTES
-- =====================================================================
PRINT '📋 Verificando estado de tablas...';

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    PRINT '❌ Tabla Users no encontrada. Ejecute primero las migraciones básicas.';
    RETURN;
END
ELSE
    PRINT '✅ Tabla Users encontrada';

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PasswordResetCodes')
BEGIN
    PRINT '❌ Tabla PasswordResetCodes no encontrada.';
    RETURN;
END
ELSE
    PRINT '✅ Tabla PasswordResetCodes encontrada';

-- =====================================================================
-- 2. CREAR TABLA EmailConfigurations SI NO EXISTE
-- =====================================================================
PRINT '';
PRINT '📧 Configurando tabla EmailConfigurations...';

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EmailConfigurations')
BEGIN
    PRINT '📝 Creando tabla EmailConfigurations...';
    
    CREATE TABLE EmailConfigurations (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ProfileName NVARCHAR(50) NOT NULL UNIQUE,
        Provider NVARCHAR(20) NOT NULL,
        FromEmail NVARCHAR(100) NOT NULL,
        FromName NVARCHAR(100) NOT NULL,
        SmtpHost NVARCHAR(100) NOT NULL,
        SmtpPort INT NOT NULL,
        UseSsl BIT NOT NULL DEFAULT 1,
        Username NVARCHAR(100) NOT NULL,
        PasswordHash NVARCHAR(500) NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        IsDefault BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CreatedBy NVARCHAR(50) NULL,
        LastTestedAt DATETIME2 NULL,
        TestEmailsSent INT NOT NULL DEFAULT 0
    );

    -- Crear índices para mejor rendimiento
    CREATE INDEX IX_EmailConfigurations_IsActive_IsDefault 
        ON EmailConfigurations(IsActive, IsDefault);
    
    CREATE INDEX IX_EmailConfigurations_Provider 
        ON EmailConfigurations(Provider);

    PRINT '✅ Tabla EmailConfigurations creada exitosamente';
END
ELSE
    PRINT '✅ Tabla EmailConfigurations ya existe';

-- =====================================================================
-- 3. CREAR PROCEDIMIENTOS ALMACENADOS
-- =====================================================================
PRINT '';
PRINT '⚙️ Creando procedimientos almacenados...';

-- Procedimiento para limpiar configuraciones inactivas
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_CleanupEmailConfigurations')
BEGIN
    EXEC('
    CREATE PROCEDURE sp_CleanupEmailConfigurations
        @DaysOld INT = 90
    AS
    BEGIN
        SET NOCOUNT ON;
        
        DELETE FROM EmailConfigurations 
        WHERE IsActive = 0 
            AND IsDefault = 0 
            AND DATEDIFF(day, UpdatedAt, GETDATE()) > @DaysOld;
        
        PRINT ''Configuraciones inactivas limpiadas.'';
    END
    ');
    PRINT '✅ sp_CleanupEmailConfigurations creado';
END

-- Procedimiento para estadísticas del sistema
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetEmailSystemStats')
BEGIN
    EXEC('
    CREATE PROCEDURE sp_GetEmailSystemStats
    AS
    BEGIN
        SET NOCOUNT ON;
        
        SELECT 
            ''EmailConfigurations'' as TableName,
            COUNT(*) as TotalRecords,
            COUNT(CASE WHEN IsActive = 1 THEN 1 END) as ActiveConfigs,
            COUNT(CASE WHEN IsDefault = 1 THEN 1 END) as DefaultConfigs,
            SUM(TestEmailsSent) as TotalTestEmails,
            MAX(LastTestedAt) as LastTest
        FROM EmailConfigurations
        
        UNION ALL
        
        SELECT 
            ''Users'' as TableName,
            COUNT(*) as TotalRecords,
            NULL as ActiveConfigs,
            NULL as DefaultConfigs,
            NULL as TotalTestEmails,
            NULL as LastTest
        FROM Users
        
        UNION ALL
        
        SELECT 
            ''PasswordResetCodes'' as TableName,
            COUNT(*) as TotalRecords,
            COUNT(CASE WHEN IsUsed = 0 AND ExpiresAt > GETDATE() THEN 1 END) as ActiveConfigs,
            NULL as DefaultConfigs,
            NULL as TotalTestEmails,
            NULL as LastTest
        FROM PasswordResetCodes;
    END
    ');
    PRINT '✅ sp_GetEmailSystemStats creado';
END

-- =====================================================================
-- 4. INSERTAR DATOS DE CONFIGURACIÓN INICIAL
-- =====================================================================
PRINT '';
PRINT '🔧 Configurando datos iniciales...';

-- Verificar si existe usuario admin
IF NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'admin')
BEGIN
    PRINT '👤 Creando usuario admin...';
    INSERT INTO Users (Username, Email, PasswordHash, CreatedAt)
    VALUES ('admin', 'admin@empresa.com', '$2a$12$ejemplo.hash.aqui', GETDATE());
    PRINT '✅ Usuario admin creado';
END
ELSE
    PRINT '✅ Usuario admin ya existe';

-- Configuración de email de ejemplo
IF NOT EXISTS (SELECT 1 FROM EmailConfigurations WHERE ProfileName = 'Configuración Inicial')
BEGIN
    PRINT '📧 Creando configuración de email inicial...';
    INSERT INTO EmailConfigurations (
        ProfileName, Provider, FromEmail, FromName,
        SmtpHost, SmtpPort, UseSsl, Username, PasswordHash,
        IsActive, IsDefault, CreatedAt, UpdatedAt, CreatedBy, TestEmailsSent
    )
    VALUES (
        'Configuración Inicial',
        'SMTP',
        'sistema@empresa.com',
        'Sistema eGestion360',
        'smtp.servidor.com',
        587,
        1,
        'sistema@empresa.com',
        'CONFIGURAR_CONTRASEÑA_REAL',
        0,  -- Inactivo hasta configurar
        0,  -- No predeterminado hasta configurar
        GETDATE(),
        GETDATE(),
        'Setup Script',
        0
    );
    PRINT '✅ Configuración de email inicial creada';
END
ELSE
    PRINT '✅ Configuración de email inicial ya existe';

-- =====================================================================
-- 5. VERIFICAR CONFIGURACIÓN FINAL
-- =====================================================================
PRINT '';
PRINT '🔍 Verificación final del sistema...';

DECLARE @UserCount INT, @EmailConfigCount INT, @ResetCodeCount INT;

SELECT @UserCount = COUNT(*) FROM Users;
SELECT @EmailConfigCount = COUNT(*) FROM EmailConfigurations;
SELECT @ResetCodeCount = COUNT(*) FROM PasswordResetCodes;

PRINT '📊 Estado de la base de datos:';
PRINT '   - Usuarios registrados: ' + CAST(@UserCount AS VARCHAR(10));
PRINT '   - Configuraciones email: ' + CAST(@EmailConfigCount AS VARCHAR(10));
PRINT '   - Códigos reset: ' + CAST(@ResetCodeCount AS VARCHAR(10));

-- =====================================================================
-- 6. EJECUTAR ESTADÍSTICAS COMPLETAS
-- =====================================================================
PRINT '';
PRINT '📈 Ejecutando estadísticas del sistema...';
EXEC sp_GetEmailSystemStats;

-- =====================================================================
-- 7. INSTRUCCIONES FINALES
-- =====================================================================
PRINT '';
PRINT '=================================================================';
PRINT '🎉 CONFIGURACIÓN COMPLETADA EXITOSAMENTE';
PRINT '=================================================================';
PRINT '';
PRINT '🚀 PRÓXIMOS PASOS:';
PRINT '';
PRINT '1. 🌐 Inicie la aplicación web:';
PRINT '   dotnet run --project eGestion360Web.csproj';
PRINT '';
PRINT '2. 🔗 Acceda a la configuración de email:';
PRINT '   http://localhost:5000/admin/email-config';
PRINT '';
PRINT '3. 📧 Configure su email corporativo:';
PRINT '   - Ingrese datos reales de su servidor SMTP';
PRINT '   - Pruebe el envío de emails';
PRINT '   - Active la configuración';
PRINT '';
PRINT '4. 🔐 Pruebe el sistema de reset de contraseñas:';
PRINT '   http://localhost:5000/ForgotPassword';
PRINT '';
PRINT '5. 👤 Inicie sesión como administrador:';
PRINT '   Usuario: admin';
PRINT '   Contraseña: demo123';
PRINT '';
PRINT '=================================================================';
PRINT '📚 DOCUMENTACIÓN ADICIONAL:';
PRINT '   - README.md: Información general';
PRINT '   - CONFIGURACION_EMAIL.md: Guía detallada de email';
PRINT '   - CONVERSION.md: Historial de cambios';
PRINT '=================================================================';
PRINT '';

-- =====================================================================
-- 8. VERIFICACIÓN DE INTEGRIDAD FINAL
-- =====================================================================
PRINT '🔒 Verificando integridad de datos...';

-- Verificar constraints
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints 
    WHERE parent_object_id = OBJECT_ID('EmailConfigurations')
)
BEGIN
    PRINT '⚠️ Agregando constraints de validación...';
    
    ALTER TABLE EmailConfigurations
    ADD CONSTRAINT CK_EmailConfigurations_SmtpPort 
        CHECK (SmtpPort > 0 AND SmtpPort <= 65535);
    
    ALTER TABLE EmailConfigurations
    ADD CONSTRAINT CK_EmailConfigurations_Provider 
        CHECK (Provider IN ('Gmail', 'Outlook', 'SMTP'));
END

-- Verificar que solo haya una configuración predeterminada
IF (SELECT COUNT(*) FROM EmailConfigurations WHERE IsDefault = 1 AND IsActive = 1) > 1
BEGIN
    PRINT '⚠️ ADVERTENCIA: Múltiples configuraciones predeterminadas encontradas.';
    PRINT '   Corrija esto en la interfaz web.';
END

PRINT '';
PRINT '✅ Sistema eGestion360 configurado y listo para usar!';
PRINT '';

-- Mostrar configuraciones actuales
SELECT 
    ProfileName,
    Provider,
    FromEmail,
    IsActive,
    IsDefault,
    TestEmailsSent,
    'Configurar en web' as Estado
FROM EmailConfigurations
ORDER BY IsDefault DESC, IsActive DESC, CreatedAt;