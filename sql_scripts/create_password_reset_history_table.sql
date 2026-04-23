-- =======================================================
-- SCRIPT: Crear tabla de historial de códigos de reseteo
-- PROPÓSITO: Tabla para almacenar códigos de verificación 
--           para restablecimiento de contraseñas
-- AUTOR: Sistema eGestion360
-- FECHA: 2026-02-22
-- =======================================================

-- Verificar si la tabla ya existe
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES 
               WHERE TABLE_SCHEMA = 'dbo' 
               AND TABLE_NAME = 'PasswordResetCodes')
BEGIN
    PRINT '📋 Creando tabla PasswordResetCodes...';

    -- CREAR TABLA PRINCIPAL
    CREATE TABLE PasswordResetCodes (
        Id              INT IDENTITY(1,1)   NOT NULL,
        UserId          INT                 NOT NULL,
        Code            NVARCHAR(6)         NOT NULL,   -- Código de 6 dígitos
        Email           NVARCHAR(100)       NOT NULL,   -- Email del usuario
        CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
        ExpiresAt       DATETIME2           NOT NULL,   -- Expiración (CreatedAt + 15 min)
        IsUsed          BIT                 NOT NULL DEFAULT 0,  -- 0=No usado, 1=Usado
        UsedAt          DATETIME2           NULL,       -- Fecha cuando se usó
        IpAddress       NVARCHAR(100)       NULL,       -- IP de auditoría
        
        -- CONSTRAINTS
        CONSTRAINT PK_PasswordResetCodes PRIMARY KEY (Id),
        CONSTRAINT FK_PasswordResetCodes_Users 
            FOREIGN KEY (UserId) REFERENCES Users(Id) 
            ON DELETE CASCADE,
        CONSTRAINT CK_PasswordResetCodes_Code 
            CHECK (Code LIKE '[0-9][0-9][0-9][0-9][0-9][0-9]'), -- Solo 6 dígitos
        CONSTRAINT CK_PasswordResetCodes_Email 
            CHECK (Email LIKE '%_@_%_.__%'),  -- Formato básico email
        CONSTRAINT CK_PasswordResetCodes_Expiry 
            CHECK (ExpiresAt > CreatedAt),    -- Expiración posterior a creación
        CONSTRAINT CK_PasswordResetCodes_UsedLogic 
            CHECK ((IsUsed = 0 AND UsedAt IS NULL) OR (IsUsed = 1 AND UsedAt IS NOT NULL))
    );

    PRINT '✅ Tabla PasswordResetCodes creada exitosamente';
END
ELSE
BEGIN
    PRINT '⚠️ La tabla PasswordResetCodes ya existe';
END
GO

-- =======================================================
-- CREAR ÍNDICES PARA PERFORMANCE
-- =======================================================

PRINT '📊 Creando índices...';

-- Índice compuesto para búsquedas de validación (más común)
IF NOT EXISTS (SELECT * FROM sys.indexes 
               WHERE name = 'IX_PasswordResetCodes_Email_Code_IsUsed' 
               AND object_id = OBJECT_ID('PasswordResetCodes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PasswordResetCodes_Email_Code_IsUsed
    ON PasswordResetCodes (Email, Code, IsUsed)
    INCLUDE (UserId, ExpiresAt, CreatedAt);
    
    PRINT '✅ Índice IX_PasswordResetCodes_Email_Code_IsUsed creado';
END

-- Índice para cleanup de códigos expirados
IF NOT EXISTS (SELECT * FROM sys.indexes 
               WHERE name = 'IX_PasswordResetCodes_ExpiresAt' 
               AND object_id = OBJECT_ID('PasswordResetCodes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PasswordResetCodes_ExpiresAt
    ON PasswordResetCodes (ExpiresAt)
    WHERE IsUsed = 0;  -- Índice filtrado solo para no usados
    
    PRINT '✅ Índice IX_PasswordResetCodes_ExpiresAt creado';
END

-- Índice para consultas por usuario
IF NOT EXISTS (SELECT * FROM sys.indexes 
               WHERE name = 'IX_PasswordResetCodes_UserId_CreatedAt' 
               AND object_id = OBJECT_ID('PasswordResetCodes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PasswordResetCodes_UserId_CreatedAt
    ON PasswordResetCodes (UserId, CreatedAt DESC)
    INCLUDE (Code, IsUsed, ExpiresAt);
    
    PRINT '✅ Índice IX_PasswordResetCodes_UserId_CreatedAt creado';
END

GO

-- =======================================================
-- PROCEDIMIENTOS ALMACENADOS OPCIONALES
-- =======================================================

-- Procedimiento para limpiar códigos expirados
IF NOT EXISTS (SELECT * FROM sys.objects 
               WHERE object_id = OBJECT_ID('sp_CleanupExpiredResetCodes') 
               AND type = 'P')
BEGIN
    EXEC('
    CREATE PROCEDURE sp_CleanupExpiredResetCodes
    AS
    BEGIN
        SET NOCOUNT ON;
        
        DECLARE @DeletedCount INT;
        
        -- Eliminar códigos expirados hace más de 24 horas
        DELETE FROM PasswordResetCodes 
        WHERE ExpiresAt < DATEADD(HOUR, -24, GETUTCDATE())
           OR CreatedAt < DATEADD(DAY, -7, GETUTCDATE());
        
        SET @DeletedCount = @@ROWCOUNT;
        
        PRINT ''🧹 Códigos expirados eliminados: '' + CAST(@DeletedCount AS VARCHAR(10));
        
        RETURN @DeletedCount;
    END'
    );
    
    PRINT '✅ Procedimiento sp_CleanupExpiredResetCodes creado';
END
GO

-- Procedimiento para obtener estadísticas
IF NOT EXISTS (SELECT * FROM sys.objects 
               WHERE object_id = OBJECT_ID('sp_GetResetCodesStats') 
               AND type = 'P')
BEGIN
    EXEC('
    CREATE PROCEDURE sp_GetResetCodesStats
    AS
    BEGIN
        SET NOCOUNT ON;
        
        SELECT 
            COUNT(*) as TotalCodes,
            SUM(CASE WHEN IsUsed = 0 AND ExpiresAt > GETUTCDATE() THEN 1 ELSE 0 END) as ActiveCodes,
            SUM(CASE WHEN IsUsed = 0 AND ExpiresAt <= GETUTCDATE() THEN 1 ELSE 0 END) as ExpiredCodes,
            SUM(CASE WHEN IsUsed = 1 THEN 1 ELSE 0 END) as UsedCodes,
            COUNT(DISTINCT UserId) as UniqueUsers,
            MIN(CreatedAt) as OldestCode,
            MAX(CreatedAt) as NewestCode
        FROM PasswordResetCodes;
    END'
    );
    
    PRINT '✅ Procedimiento sp_GetResetCodesStats creado';
END
GO

-- =======================================================
-- DATOS DE PRUEBA (OPCIONAL - COMENTAR EN PRODUCCIÓN)
-- =======================================================

-- Insertar algunos códigos de ejemplo para testing
-- COMENTAR ESTA SECCIÓN EN PRODUCCIÓN

/*
PRINT '🧪 Insertando datos de prueba...';

-- Código activo para admin
DECLARE @AdminUserId INT = (SELECT Id FROM Users WHERE Username = 'admin');

IF @AdminUserId IS NOT NULL
BEGIN
    INSERT INTO PasswordResetCodes (UserId, Code, Email, ExpiresAt, IsUsed, IpAddress)
    VALUES 
        (@AdminUserId, '123456', 'admin@siptech.com', DATEADD(MINUTE, 15, GETUTCDATE()), 0, '192.168.1.100'),
        (@AdminUserId, '654321', 'admin@siptech.com', DATEADD(MINUTE, -30, GETUTCDATE()), 1, '192.168.1.100'); -- Expirado
    
    PRINT '✅ Datos de prueba insertados';
END
*/

-- =======================================================
-- VERIFICACIÓN FINAL
-- =======================================================

PRINT '🔍 Verificando instalación...';

SELECT 
    'PasswordResetCodes' as TableName,
    COUNT(*) as RecordCount,
    MIN(CreatedAt) as OldestRecord,
    MAX(CreatedAt) as NewestRecord
FROM PasswordResetCodes;

PRINT '✅ Script ejecutado completamente';
PRINT '📋 Tabla PasswordResetCodes lista para usar';
PRINT '';
PRINT '⚡ COMANDOS ÚTILES:';
PRINT '   -- Ver estadísticas: EXEC sp_GetResetCodesStats';
PRINT '   -- Limpiar expirados: EXEC sp_CleanupExpiredResetCodes';
PRINT '   -- Ver últimos 10: SELECT TOP 10 * FROM PasswordResetCodes ORDER BY CreatedAt DESC';

GO