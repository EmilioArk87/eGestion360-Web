-- ================================================================
-- SCRIPT: Crear tabla de configuración SMTP empresarial  
-- PARA: DBeaver + SQL Server
-- PROPÓSITO: Almacenar configuración de email con contraseñas
--           encriptadas y gestión centralizada
-- AUTOR: Sistema eGestion360
-- FECHA: 2026-04-12
-- ================================================================
-- INSTRUCCIONES PARA DBEAVER:
-- 1. Ejecutar BLOQUE 1: Creación de tabla (líneas 15-55)
-- 2. Ejecutar BLOQUE 2: Creación de índices (líneas 57-85) 
-- 3. Ejecutar BLOQUE 3: Procedimientos (líneas 87-185)
-- 4. Ejecutar BLOQUE 4: Datos iniciales (líneas 187-225)
-- 5. Ejecutar BLOQUE 5: Trigger (líneas 227-245)
-- 6. Ejecutar BLOQUE 6: Verificación final (líneas 247-265)
-- ================================================================

-- ================================================================
-- BLOQUE 1: CREACIÓN DE TABLA PRINCIPAL
-- EJECUTAR: Seleccionar desde aquí hasta "-- FIN BLOQUE 1"
-- ================================================================

-- Verificar si la tabla ya existe
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES 
               WHERE TABLE_SCHEMA = 'dbo' 
               AND TABLE_NAME = 'EmailConfiguration')
BEGIN
    PRINT '📧 Creando tabla EmailConfiguration...';

    -- CREAR TABLA DE CONFIGURACIÓN EMAIL
    CREATE TABLE EmailConfiguration (
        Id              INT IDENTITY(1,1)   NOT NULL,
        ProfileName     NVARCHAR(50)        NOT NULL,   -- Nombre del perfil (ej: "Principal", "Notificaciones")
        Provider        NVARCHAR(20)        NOT NULL,   -- "SMTP", "Gmail", "Outlook", "SendGrid"
        FromEmail       NVARCHAR(100)       NOT NULL,   -- Email remitente
        FromName        NVARCHAR(100)       NOT NULL,   -- Nombre del remitente  
        SmtpHost        NVARCHAR(100)       NOT NULL,   -- Servidor SMTP
        SmtpPort        INT                 NOT NULL DEFAULT 587, -- Puerto SMTP
        UseSsl          BIT                 NOT NULL DEFAULT 1,    -- Usar SSL/TLS
        Username        NVARCHAR(100)       NOT NULL,   -- Usuario SMTP
        PasswordHash    NVARCHAR(500)       NOT NULL,   -- Contraseña encriptada
        IsActive        BIT                 NOT NULL DEFAULT 1,    -- Configuración activa
        IsDefault       BIT                 NOT NULL DEFAULT 0,    -- Configuración por defecto
        CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy       NVARCHAR(50)        NULL,       -- Usuario que creó
        LastTestedAt    DATETIME2           NULL,       -- Última prueba exitosa
        TestEmailsSent  INT                 NOT NULL DEFAULT 0,    -- Contador de emails enviados
        
        -- CONSTRAINTS
        CONSTRAINT PK_EmailConfiguration PRIMARY KEY (Id),
        CONSTRAINT UK_EmailConfiguration_ProfileName 
            UNIQUE (ProfileName),
        CONSTRAINT CK_EmailConfiguration_Provider 
            CHECK (Provider IN ('SMTP', 'Gmail', 'Outlook', 'SendGrid', 'Office365')),
        CONSTRAINT CK_EmailConfiguration_Email 
            CHECK (FromEmail LIKE '%_@_%_.__%'),
        CONSTRAINT CK_EmailConfiguration_SmtpPort 
            CHECK (SmtpPort BETWEEN 25 AND 65535),
        CONSTRAINT CK_EmailConfiguration_OnlyOneDefault
            CHECK (IsDefault = 0 OR IsActive = 1) -- Solo activos pueden ser default
    );

    PRINT '✅ Tabla EmailConfiguration creada exitosamente';
END
ELSE
BEGIN
    PRINT '⚠️ La tabla EmailConfiguration ya existe';
END;

-- FIN BLOQUE 1
-- ================================================================
-- BLOQUE 2: CREACIÓN DE ÍNDICES
-- EJECUTAR: Seleccionar desde aquí hasta "-- FIN BLOQUE 2"
-- ================================================================

-- Crear índices para optimizar consultas
PRINT '📊 Creando índices...';

-- Índice para búsqueda rápida de configuración activa
IF NOT EXISTS (SELECT * FROM sys.indexes 
               WHERE name = 'IX_EmailConfiguration_Active_Default' 
               AND object_id = OBJECT_ID('EmailConfiguration'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_EmailConfiguration_Active_Default
    ON EmailConfiguration (IsActive, IsDefault)
    INCLUDE (ProfileName, Provider, FromEmail, SmtpHost, SmtpPort, UseSsl);
    
    PRINT '✅ Índice IX_EmailConfiguration_Active_Default creado';
END

-- Índice por proveedor
IF NOT EXISTS (SELECT * FROM sys.indexes 
               WHERE name = 'IX_EmailConfiguration_Provider' 
               AND object_id = OBJECT_ID('EmailConfiguration'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_EmailConfiguration_Provider
    ON EmailConfiguration (Provider, IsActive)
    INCLUDE (ProfileName, FromEmail);
    
    PRINT '✅ Índice IX_EmailConfiguration_Provider creado';
END;

-- FIN BLOQUE 2
-- ================================================================
-- BLOQUE 3: PROCEDIMIENTOS ALMACENADOS
-- EJECUTAR: Cada procedimiento por separado en DBeaver
-- ================================================================

-- PROCEDIMIENTO 1: Obtener configuración activa
-- EJECUTAR: Solo este bloque
IF NOT EXISTS (SELECT * FROM sys.objects 
               WHERE object_id = OBJECT_ID('sp_GetActiveEmailConfiguration') 
               AND type = 'P')
BEGIN
    EXEC('
    CREATE PROCEDURE sp_GetActiveEmailConfiguration
        @ProfileName NVARCHAR(50) = NULL
    AS
    BEGIN
        SET NOCOUNT ON;
        
        IF @ProfileName IS NOT NULL
        BEGIN
            -- Buscar perfil específico
            SELECT * FROM EmailConfiguration 
            WHERE ProfileName = @ProfileName AND IsActive = 1;
        END
        ELSE
        BEGIN
            -- Buscar configuración por defecto
            SELECT TOP 1 * FROM EmailConfiguration 
            WHERE IsActive = 1 
            ORDER BY IsDefault DESC, Id ASC;
        END
    END'
    );
    
    PRINT '✅ Procedimiento sp_GetActiveEmailConfiguration creado';
END;

-- FIN PROCEDIMIENTO 1
-- ----------------------------------------
-- PROCEDIMIENTO 2: Establecer configuración por defecto
-- EJECUTAR: Solo este bloque

-- Procedimiento para establecer configuración por defecto
IF NOT EXISTS (SELECT * FROM sys.objects 
               WHERE object_id = OBJECT_ID('sp_SetDefaultEmailConfiguration') 
               AND type = 'P')
BEGIN
    EXEC('
    CREATE PROCEDURE sp_SetDefaultEmailConfiguration
        @Id INT
    AS
    BEGIN
        SET NOCOUNT ON;
        
        BEGIN TRANSACTION;
        
        BEGIN TRY
            -- Quitar default a todas las configuraciones
            UPDATE EmailConfiguration SET IsDefault = 0;
            
            -- Establecer nueva por defecto
            UPDATE EmailConfiguration 
            SET IsDefault = 1, UpdatedAt = GETUTCDATE()
            WHERE Id = @Id AND IsActive = 1;
            
            IF @@ROWCOUNT = 0
            BEGIN
                THROW 50001, ''Configuración no encontrada o inactiva'', 1;
            END
            
            COMMIT TRANSACTION;
            
            PRINT ''✅ Configuración por defecto actualizada'';
        END TRY
        BEGIN CATCH
            ROLLBACK TRANSACTION;
            THROW;
        END CATCH
    END'
    );
    
    PRINT '✅ Procedimiento sp_SetDefaultEmailConfiguration creado';
END;

-- FIN PROCEDIMIENTO 2
-- ----------------------------------------
-- PROCEDIMIENTO 3: Actualizar estadísticas de prueba
-- EJECUTAR: Solo este bloque

-- Procedimiento para actualizar estadísticas de prueba
IF NOT EXISTS (SELECT * FROM sys.objects 
               WHERE object_id = OBJECT_ID('sp_UpdateEmailTestStats') 
               AND type = 'P')
BEGIN
    EXEC('
    CREATE PROCEDURE sp_UpdateEmailTestStats
        @Id INT,
        @Success BIT
    AS
    BEGIN
        SET NOCOUNT ON;
        
        UPDATE EmailConfiguration 
        SET 
            TestEmailsSent = TestEmailsSent + 1,
            LastTestedAt = CASE WHEN @Success = 1 THEN GETUTCDATE() ELSE LastTestedAt END,
            UpdatedAt = GETUTCDATE()
        WHERE Id = @Id;
    END'
    );
    
    PRINT '✅ Procedimiento sp_UpdateEmailTestStats creado';
END;

-- FIN BLOQUE 3 (PROCEDIMIENTOS)
-- ================================================================
-- BLOQUE 4: DATOS INICIALES DE EJEMPLO
-- EJECUTAR: Seleccionar desde aquí hasta "-- FIN BLOQUE 4"
-- ================================================================

-- Insertar datos de ejemplo (opcional)
PRINT '🏢 Insertando configuración de ejemplo...';

-- Solo insertar si NO existe ninguna configuración
IF NOT EXISTS (SELECT 1 FROM EmailConfiguration)
BEGIN
    INSERT INTO EmailConfiguration 
    (ProfileName, Provider, FromEmail, FromName, SmtpHost, SmtpPort, UseSsl, Username, PasswordHash, IsActive, IsDefault, CreatedBy)
    VALUES 
    (
        'Principal',                                    -- ProfileName
        'SMTP',                                         -- Provider
        'noreply@tuempresa.com',                       -- FromEmail
        'eGestion360 Sistema',                         -- FromName
        'mail.tuempresa.com',                          -- SmtpHost 
        587,                                           -- SmtpPort
        1,                                             -- UseSsl
        'noreply@tuempresa.com',                       -- Username
        'CONFIGURAR_CONTRASEÑA_ENCRIPTADA',            -- PasswordHash (temporal)
        0,                                             -- IsActive (inactivo hasta configurar)
        0,                                             -- IsDefault (CORREGIDO: inactivo no puede ser default)
        'Sistema'                                      -- CreatedBy
    ),
    (
        'Notificaciones',                              -- ProfileName
        'SMTP',                                        -- Provider
        'notificaciones@tuempresa.com',               -- FromEmail
        'eGestion360 Notificaciones',                 -- FromName
        'mail.tuempresa.com',                          -- SmtpHost
        587,                                           -- SmtpPort
        1,                                             -- UseSsl
        'notificaciones@tuempresa.com',               -- Username
        'CONFIGURAR_CONTRASEÑA_ENCRIPTADA',            -- PasswordHash (temporal)
        0,                                             -- IsActive (inactivo hasta configurar)
        0,                                             -- IsDefault
        'Sistema'                                      -- CreatedBy
    );
    
    PRINT '✅ Configuraciones de ejemplo insertadas (inactivas por defecto)';
END
ELSE
BEGIN
    PRINT 'ℹ️ Ya existen configuraciones, saltando inserción de ejemplo';
END;

-- FIN BLOQUE 4
-- ================================================================
-- BLOQUE 5: TRIGGER DE ACTUALIZACIÓN
-- EJECUTAR: Seleccionar desde aquí hasta "-- FIN BLOQUE 5"
-- ================================================================

-- Crear trigger para UpdatedAt automático

IF NOT EXISTS (SELECT * FROM sys.triggers WHERE name = 'TR_EmailConfiguration_UpdatedAt')
BEGIN
    EXEC('
    CREATE TRIGGER TR_EmailConfiguration_UpdatedAt
    ON EmailConfiguration
    AFTER UPDATE
    AS
    BEGIN
        SET NOCOUNT ON;
        
        UPDATE EmailConfiguration 
        SET UpdatedAt = GETUTCDATE()
        FROM EmailConfiguration ec
        INNER JOIN inserted i ON ec.Id = i.Id;
    END'
    );
    
    PRINT '✅ Trigger TR_EmailConfiguration_UpdatedAt creado';
END;

-- FIN BLOQUE 5
-- ================================================================
-- BLOQUE 6: VERIFICACIÓN FINAL
-- EJECUTAR: Seleccionar desde aquí hasta el final
-- ================================================================

-- Verificar que todo se creó correctamente
PRINT '🔍 Verificando instalación...';

SELECT 
    'EmailConfiguration' as TableName,
    COUNT(*) as RecordCount,
    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) as ActiveConfigurations,
    SUM(CASE WHEN IsDefault = 1 THEN 1 ELSE 0 END) as DefaultConfigurations,
    MIN(CreatedAt) as OldestRecord,
    MAX(CreatedAt) as NewestRecord
FROM EmailConfiguration;

PRINT '✅ Script de EmailConfiguration ejecutado completamente';
PRINT '';
PRINT '📧 DATOS NECESARIOS PARA CONFIGURAR:';
PRINT '   1. Servidor SMTP de tu empresa (ej: mail.tuempresa.com)';
PRINT '   2. Puerto SMTP (normalmente 587 o 25)';
PRINT '   3. Email empresarial para envíos (ej: noreply@tuempresa.com)';
PRINT '   4. Usuario y contraseña SMTP';
PRINT '   5. Tipo de seguridad (TLS recomendado)';
PRINT '';
PRINT '⚡ COMANDOS ÚTILES:';
PRINT '   -- Ver configuraciones: SELECT * FROM EmailConfiguration';
PRINT '   -- Configuración activa: EXEC sp_GetActiveEmailConfiguration';
PRINT '   -- Cambiar default: EXEC sp_SetDefaultEmailConfiguration @Id = 1';

-- FIN BLOQUE 6
-- ================================================================
-- SCRIPT COMPLETADO PARA DBEAVER
-- ================================================================