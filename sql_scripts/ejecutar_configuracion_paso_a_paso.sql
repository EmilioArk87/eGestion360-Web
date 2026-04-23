-- ================================================================
-- CONFIGURACIÓN COMPLETA DE EMAIL EN BASE DE DATOS
-- EJECUTAR PASO A PASO EN DBEAVER
-- ================================================================

-- ================================================================
-- PASO 1: CREAR TABLA EMAIL CONFIGURATION
-- COPIAR Y EJECUTAR ESTE BLOQUE COMPLETO
-- ================================================================

-- Verificar si la tabla ya existe
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES 
               WHERE TABLE_SCHEMA = 'dbo' 
               AND TABLE_NAME = 'EmailConfiguration')
BEGIN
    -- CREAR TABLA DE CONFIGURACIÓN EMAIL
    CREATE TABLE EmailConfiguration (
        Id              INT IDENTITY(1,1)   NOT NULL,
        ProfileName     NVARCHAR(50)        NOT NULL,   -- Nombre del perfil
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
        CONSTRAINT UK_EmailConfiguration_ProfileName UNIQUE (ProfileName),
        CONSTRAINT CK_EmailConfiguration_Provider 
            CHECK (Provider IN ('SMTP', 'Gmail', 'Outlook', 'SendGrid', 'Office365')),
        CONSTRAINT CK_EmailConfiguration_Email 
            CHECK (FromEmail LIKE '%_@_%_.__%'),
        CONSTRAINT CK_EmailConfiguration_SmtpPort 
            CHECK (SmtpPort BETWEEN 25 AND 65535),
        CONSTRAINT CK_EmailConfiguration_OnlyOneDefault
            CHECK (IsDefault = 0 OR IsActive = 1)
    );
    
    SELECT '✅ Tabla EmailConfiguration creada exitosamente' AS Resultado;
END
ELSE
BEGIN
    SELECT '⚠️ La tabla EmailConfiguration ya existe' AS Resultado;
END;

-- ================================================================
-- PASO 2: CREAR ÍNDICES PARA OPTIMIZAR CONSULTAS
-- COPIAR Y EJECUTAR ESTE BLOQUE
-- ================================================================

-- Índice para búsqueda rápida de configuración activa
IF NOT EXISTS (SELECT * FROM sys.indexes 
               WHERE name = 'IX_EmailConfiguration_Active_Default' 
               AND object_id = OBJECT_ID('EmailConfiguration'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_EmailConfiguration_Active_Default
    ON EmailConfiguration (IsActive, IsDefault)
    INCLUDE (ProfileName, Provider, FromEmail, SmtpHost, SmtpPort, UseSsl);
    
    SELECT '✅ Índice IX_EmailConfiguration_Active_Default creado' AS Resultado;
END;

-- Índice por proveedor
IF NOT EXISTS (SELECT * FROM sys.indexes 
               WHERE name = 'IX_EmailConfiguration_Provider' 
               AND object_id = OBJECT_ID('EmailConfiguration'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_EmailConfiguration_Provider
    ON EmailConfiguration (Provider, IsActive)
    INCLUDE (ProfileName, FromEmail);
    
    SELECT '✅ Índice IX_EmailConfiguration_Provider creado' AS Resultado;
END;