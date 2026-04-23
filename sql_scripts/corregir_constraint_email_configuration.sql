-- =========================================================================
-- SCRIPT: Corrección de constraint CK_EmailConfiguration_OnlyOneDefault
-- PROPÓSITO: Solucionar el error de constraint que impide INSERT/UPDATE
-- ERROR: "The INSERT statement conflicted with the CHECK constraint"
-- FECHA: 2026-04-10
-- =========================================================================

USE [eBD_SPD];  -- Usar tu base de datos específica

-- =============================================
-- DIAGNÓSTICO DEL PROBLEMA
-- =============================================

PRINT '📊 DIAGNÓSTICO INICIAL - Configuraciones problemáticas:';
PRINT '========================================================';

-- Mostrar configuraciones que violan el constraint
SELECT 
    Id,
    ProfileName,
    FromEmail,
    IsActive,
    IsDefault,
    CASE 
        WHEN IsActive = 0 AND IsDefault = 1 THEN '❌ VIOLA CONSTRAINT'
        WHEN IsActive = 1 AND IsDefault = 1 THEN '✅ OK - Activa y Por Defecto'
        WHEN IsActive = 0 AND IsDefault = 0 THEN '⚠️ Inactiva'
        WHEN IsActive = 1 AND IsDefault = 0 THEN '✅ OK - Activa'
        ELSE '❓ Estado desconocido'
    END as [Estado],
    CreatedAt
FROM EmailConfiguration
ORDER BY IsDefault DESC, IsActive DESC, Id;

-- Contar configuraciones problemáticas
DECLARE @ConfiguracionesProblematicas INT;
SELECT @ConfiguracionesProblematicas = COUNT(*)
FROM EmailConfiguration
WHERE IsActive = 0 AND IsDefault = 1;

PRINT '';
PRINT '🔍 Configuraciones que VIOLAN el constraint: ' + CAST(@ConfiguracionesProblematicas AS NVARCHAR(10));

-- =============================================
-- CORRECCIÓN AUTOMÁTICA
-- =============================================

IF @ConfiguracionesProblematicas > 0
BEGIN
    PRINT '';
    PRINT '🔧 INICIANDO CORRECCIÓN AUTOMÁTICA...';
    PRINT '====================================';
    
    BEGIN TRANSACTION;
    
    BEGIN TRY
        -- PASO 1: Quitar IsDefault=1 a todas las configuraciones inactivas
        UPDATE EmailConfiguration 
        SET 
            IsDefault = 0,
            UpdatedAt = GETUTCDATE()
        WHERE IsActive = 0 AND IsDefault = 1;
        
        PRINT '✅ Paso 1: Configuraciones inactivas ya no son por defecto';
        
        -- PASO 2: Verificar si hay alguna configuración activa y por defecto
        DECLARE @ConfigActivasPorDefecto INT;
        SELECT @ConfigActivasPorDefecto = COUNT(*)
        FROM EmailConfiguration
        WHERE IsActive = 1 AND IsDefault = 1;
        
        -- PASO 3: Si no hay ninguna activa por defecto, establecer la primera activa como por defecto
        IF @ConfigActivasPorDefecto = 0
        BEGIN
            DECLARE @PrimeraActivaId INT;
            SELECT TOP 1 @PrimeraActivaId = Id
            FROM EmailConfiguration
            WHERE IsActive = 1
            ORDER BY Id ASC;
            
            IF @PrimeraActivaId IS NOT NULL
            BEGIN
                UPDATE EmailConfiguration 
                SET 
                    IsDefault = 1,
                    UpdatedAt = GETUTCDATE()
                WHERE Id = @PrimeraActivaId;
                
                PRINT '✅ Paso 2: Configuración ID ' + CAST(@PrimeraActivaId AS NVARCHAR(10)) + ' establecida como por defecto';
            END
            ELSE
            BEGIN
                PRINT '⚠️ Paso 2: No hay configuraciones activas para establecer como por defecto';
            END
        END
        ELSE
        BEGIN
            PRINT '✅ Paso 2: Ya existe una configuración activa por defecto';
        END
        
        COMMIT TRANSACTION;
        
        PRINT '✅ CORRECCIÓN COMPLETADA EXITOSAMENTE';
        
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        
        PRINT '❌ ERROR DURANTE LA CORRECCIÓN:';
        PRINT ERROR_MESSAGE();
        
    END CATCH
END
ELSE
BEGIN
    PRINT '';
    PRINT '✅ No hay configuraciones problemáticas que corregir';
END

-- =============================================
-- VERIFICACIÓN POST-CORRECCIÓN
-- =============================================

PRINT '';
PRINT '📊 ESTADO DESPUÉS DE LA CORRECCIÓN:';
PRINT '===================================';

SELECT 
    Id,
    ProfileName,
    FromEmail,
    IsActive,
    IsDefault,
    CASE 
        WHEN IsActive = 0 AND IsDefault = 1 THEN '❌ TODAVÍA VIOLA CONSTRAINT'
        WHEN IsActive = 1 AND IsDefault = 1 THEN '✅ OK - Activa y Por Defecto'
        WHEN IsActive = 0 AND IsDefault = 0 THEN '⚠️ Inactiva'
        WHEN IsActive = 1 AND IsDefault = 0 THEN '✅ OK - Activa'
        ELSE '❓ Estado desconocido'
    END as [Estado],
    UpdatedAt
FROM EmailConfiguration
ORDER BY IsDefault DESC, IsActive DESC, Id;

-- Verificar que no hay violaciones
DECLARE @ViolacionesRestantes INT;
SELECT @ViolacionesRestantes = COUNT(*)
FROM EmailConfiguration
WHERE IsActive = 0 AND IsDefault = 1;

PRINT '';
IF @ViolacionesRestantes = 0
BEGIN
    PRINT '🎉 ¡ÉXITO! No hay violaciones del constraint';
    PRINT '✅ Ahora puedes insertar/actualizar configuraciones sin problemas';
END
ELSE
BEGIN
    PRINT '⚠️ ADVERTENCIA: Todavía hay ' + CAST(@ViolacionesRestantes AS NVARCHAR(10)) + ' violaciones';
    PRINT '❌ Revisa manualmente las configuraciones problemáticas';
END

-- =============================================
-- RECOMENDACIONES PARA EL FUTURO
-- =============================================

PRINT '';
PRINT '💡 RECOMENDACIONES PARA EVITAR ESTE PROBLEMA:';
PRINT '=============================================';
PRINT '1️⃣ Solo establece IsDefault=1 en configuraciones ACTIVAS (IsActive=1)';
PRINT '2️⃣ Cuando desactives una configuración, quita IsDefault automáticamente';
PRINT '3️⃣ Usa el procedimiento SP_SetDefaultEmailConfiguration para cambios';
PRINT '4️⃣ Siempre valida que haya al menos una configuración activa';
PRINT '';

-- =============================================
-- PROCEDIMIENTO MEJORADO PARA ESTABLECER DEFAULT
-- =============================================

PRINT '⚡ CREANDO PROCEDIMIENTO MEJORADO...';

-- Eliminar procedimiento existente si hay problemas
IF OBJECT_ID('sp_SetDefaultEmailConfigurationSafe', 'P') IS NOT NULL
    DROP PROCEDURE sp_SetDefaultEmailConfigurationSafe;

EXEC('
CREATE PROCEDURE sp_SetDefaultEmailConfigurationSafe
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @IsActive BIT;
    DECLARE @CurrentProfileName NVARCHAR(50);
    
    -- Verificar que la configuración existe y está activa
    SELECT @IsActive = IsActive, @CurrentProfileName = ProfileName
    FROM EmailConfiguration 
    WHERE Id = @Id;
    
    IF @IsActive IS NULL
    BEGIN
        THROW 50001, ''Configuración no encontrada'', 1;
        RETURN;
    END
    
    IF @IsActive = 0
    BEGIN
        THROW 50002, ''No se puede establecer como por defecto una configuración inactiva'', 1;
        RETURN;
    END
    
    BEGIN TRANSACTION;
    
    BEGIN TRY
        -- Quitar default a todas las configuraciones
        UPDATE EmailConfiguration SET IsDefault = 0, UpdatedAt = GETUTCDATE();
        
        -- Establecer nueva por defecto (solo si está activa)
        UPDATE EmailConfiguration 
        SET IsDefault = 1, UpdatedAt = GETUTCDATE()
        WHERE Id = @Id AND IsActive = 1;
        
        COMMIT TRANSACTION;
        
        PRINT ''✅ Configuración "'' + @CurrentProfileName + ''" establecida como por defecto'';
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
');

PRINT '✅ Procedimiento sp_SetDefaultEmailConfigurationSafe creado';

-- =============================================
-- COMANDOS ÚTILES PARA EL FUTURO
-- =============================================

PRINT '';
PRINT '📋 COMANDOS ÚTILES PARA ADMINISTRAR CONFIGURACIONES:';
PRINT '==================================================';
PRINT '-- Ver estado actual:';
PRINT 'SELECT Id, ProfileName, IsActive, IsDefault FROM EmailConfiguration;';
PRINT '';
PRINT '-- Establecer por defecto de forma segura:';
PRINT 'EXEC sp_SetDefaultEmailConfigurationSafe @Id = [ID_CONFIGURACION];';
PRINT '';
PRINT '-- Activar una configuración:';
PRINT 'UPDATE EmailConfiguration SET IsActive = 1, UpdatedAt = GETUTCDATE() WHERE Id = [ID];';
PRINT '';
PRINT '-- Desactivar y quitar como por defecto:';
PRINT 'UPDATE EmailConfiguration SET IsActive = 0, IsDefault = 0, UpdatedAt = GETUTCDATE() WHERE Id = [ID];';
