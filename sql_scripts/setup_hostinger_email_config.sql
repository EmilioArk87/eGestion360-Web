-- =========================================================================
-- SCRIPT: Configuración SMTP para Hostinger.es (OPTIMIZADO PARA DBEAVER)
-- PROPÓSITO: Crear procedimiento almacenado para configurar email con Hostinger
-- AUTOR: Sistema eGestion360
-- FECHA: 2026-04-12
-- =========================================================================
-- INSTRUCCIONES PARA DBEAVER:
-- 1. BLOQUE A: Eliminar procedimiento anterior (líneas 15-25)
-- 2. BLOQUE B: Crear procedimiento principal (líneas 27-150) 
-- 3. BLOQUE C: Permisos y documentación (líneas 152-fin)
-- =========================================================================

-- ================================================================
-- BLOQUE A: LIMPIAR PROCEDIMIENTO ANTERIOR
-- EJECUTAR: Seleccionar solo este bloque
-- ================================================================
-- Verificar y eliminar procedimiento anterior si existe
IF OBJECT_ID('dbo.SP_ConfigurarHostingerEmail', 'P') IS NOT NULL
BEGIN
    DROP PROCEDURE dbo.SP_ConfigurarHostingerEmail;
    SELECT '🗑️ Procedimiento anterior eliminado' AS Resultado;
END
ELSE
BEGIN
    SELECT 'ℹ️ No hay procedimiento anterior que eliminar' AS Resultado;
END;

-- FIN BLOQUE A
-- ================================================================
-- BLOQUE B: CREAR PROCEDIMIENTO PRINCIPAL
-- EJECUTAR: Seleccionar desde aquí hasta "-- FIN BLOQUE B"
-- IMPORTANTE: Ejecutar todo este bloque de una vez
-- ================================================================

-- =====================================================
-- CREAR PROCEDIMIENTO ALMACENADO PRINCIPAL 
-- =====================================================
CREATE PROCEDURE dbo.SP_ConfigurarHostingerEmail
    @EmailUsuario         NVARCHAR(100),      -- Email de la cuenta de Hostinger
    @ContraseñaPlana      NVARCHAR(200),      -- Contraseña en texto plano (se marcará para encriptar)
    @NombreRemitente      NVARCHAR(100),      -- Nombre que aparecerá como remitente
    @NombrePerfil         NVARCHAR(50) = 'Hostinger Principal', -- Nombre del perfil
    @EstablecerPorDefecto BIT = 1,            -- Si establecer como configuración por defecto
    @Puerto               INT = 587,          -- Puerto SMTP (587 o 465)
    @UsarSSL              BIT = 1,            -- Usar SSL/TLS
    @CreadoPor            NVARCHAR(50) = 'Sistema' -- Quien configura
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @ConfigID INT;
    DECLARE @ConfiguracionesExistentes INT;
    DECLARE @ErrorMessage NVARCHAR(500);
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        -- ===============================================
        -- VALIDACIONES DE ENTRADA
        -- ===============================================
        
        -- Validar que el email tenga formato correcto
        IF @EmailUsuario IS NULL OR @EmailUsuario = '' OR CHARINDEX('@', @EmailUsuario) = 0
        BEGIN
            RAISERROR('❌ Email de usuario requerido y debe tener formato válido', 16, 1);
            RETURN;
        END
        
        -- Validar que la contraseña no esté vacía
        IF @ContraseñaPlana IS NULL OR @ContraseñaPlana = ''
        BEGIN
            RAISERROR('❌ Contraseña requerida', 16, 1);
            RETURN;
        END
        
        -- Validar que el nombre del remitente no esté vacío
        IF @NombreRemitente IS NULL OR @NombreRemitente = ''
        BEGIN
            SET @NombreRemitente = @EmailUsuario; -- Usar email como nombre por defecto
        END
        
        -- Validar puerto
        IF @Puerto NOT IN (25, 465, 587, 2525)
        BEGIN
            PRINT '⚠️ Advertencia: Puerto no estándar. Puertos comunes: 25, 465, 587, 2525';
        END
        
        -- ===============================================
        -- VERIFICAR CONFIGURACIONES EXISTENTES
        -- ===============================================
        
        SELECT @ConfiguracionesExistentes = COUNT(*)
        FROM EmailConfiguration
        WHERE IsActive = 1;
        
        -- Si no hay configuraciones activas, esta será la por defecto
        IF @ConfiguracionesExistentes = 0
        BEGIN
            SET @EstablecerPorDefecto = 1;
            PRINT '📧 Primera configuración activa: se establecerá como por defecto';
        END
        
        -- ===============================================
        -- VALIDAR CONSTRAINT: Solo configuraciones activas pueden ser por defecto
        -- ===============================================
        
        -- IMPORTANTE: El constraint CK_EmailConfiguration_OnlyOneDefault requiere que:
        -- IsDefault = 0 OR IsActive = 1 (solo activos pueden ser default)
        -- Por lo tanto, si establecemos por defecto, debe estar activa
        
        IF @EstablecerPorDefecto = 1
        BEGIN
            -- Si se va a establecer por defecto, debe estar activa
            -- La configuración siempre será activa al crearla con Hostinger
            PRINT '📝 Configuración será activa y por defecto';
        END
        
        -- ===============================================
        -- DESACTIVAR CONFIGURACIÓN POR DEFECTO ANTERIOR
        -- ===============================================
        
        IF @EstablecerPorDefecto = 1
        BEGIN
            UPDATE EmailConfiguration 
            SET IsDefault = 0,
                UpdatedAt = GETUTCDATE()
            WHERE IsDefault = 1;
            
            PRINT '📝 Configuración por defecto anterior desactivada';
        END
        
        -- ===============================================
        -- INSERTAR NUEVA CONFIGURACIÓN HOSTINGER
        -- ===============================================
        
        INSERT INTO EmailConfiguration (
            ProfileName,
            Provider,
            FromEmail,
            FromName,
            SmtpHost,
            SmtpPort,
            UseSsl,
            Username,
            PasswordHash,           -- Se marcará para encriptar después
            IsActive,
            IsDefault,
            CreatedAt,
            UpdatedAt,
            CreatedBy,
            TestEmailsSent
        )
        VALUES (
            @NombrePerfil,
            'SMTP',                 -- Tipo de proveedor
            @EmailUsuario,          -- Email remitente
            @NombreRemitente,       -- Nombre remitente
            'smtp.hostinger.com',   -- Servidor SMTP de Hostinger
            @Puerto,                -- Puerto configurado
            @UsarSSL,               -- SSL habilitado por defecto
            @EmailUsuario,          -- Usuario (mismo que email)
            'PENDIENTE_ENCRIPTAR:' + @ContraseñaPlana, -- Marcador para encriptar después
            1,                      -- IsActive=1 (SIEMPRE ACTIVO para configuraciones válidas)
            @EstablecerPorDefecto,  -- IsDefault (solo si IsActive=1, respetando constraint)
            GETUTCDATE(),           -- Fecha creación
            GETUTCDATE(),           -- Fecha actualización
            @CreadoPor,             -- Creado por
            0                       -- Emails de prueba enviados
        );
        
        SET @ConfigID = SCOPE_IDENTITY();
        
        -- ===============================================
        -- LOGS Y CONFIRMACIÓN
        -- ===============================================
        
        PRINT '✅ ¡Configuración de Hostinger.es creada exitosamente!';
        PRINT '📊 ID de Configuración: ' + CAST(@ConfigID AS NVARCHAR(10));
        PRINT '📧 Email: ' + @EmailUsuario;
        PRINT '🏷️ Perfil: ' + @NombrePerfil;
        PRINT '🔧 Servidor SMTP: smtp.hostinger.com:' + CAST(@Puerto AS NVARCHAR(10));
        PRINT '🔒 SSL Habilitado: ' + CASE WHEN @UsarSSL = 1 THEN 'Sí' ELSE 'No' END;
        PRINT '⭐ Por Defecto: ' + CASE WHEN @EstablecerPorDefecto = 1 THEN 'Sí' ELSE 'No' END;
        
        -- ===============================================
        -- INSTRUCCIONES IMPORTANTES
        -- ===============================================
        
        PRINT '';
        PRINT '🔐 IMPORTANTE - PRÓXIMOS PASOS:';
        PRINT '1️⃣ La contraseña está marcada para encriptación';
        PRINT '2️⃣ Ejecuta la página de EncriptPasswords desde la web';
        PRINT '3️⃣ O usa el servicio EncryptionService desde .NET';
        PRINT '4️⃣ Después prueba el envío desde la aplicación';
        PRINT '';
        PRINT '📋 Script para verificar:';
        PRINT 'SELECT * FROM EmailConfiguration WHERE Id = ' + CAST(@ConfigID AS NVARCHAR(10));
        
        COMMIT TRANSACTION;
        
        -- Retornar el ID de la configuración creada
        SELECT @ConfigID as ConfigurationId, 'SUCCESS' as Status, 'Configuración creada exitosamente' as Message;
        
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        
        SET @ErrorMessage = ERROR_MESSAGE();
        PRINT '❌ ERROR: ' + @ErrorMessage;
        
        -- Retornar error
        SELECT 0 as ConfigurationId, 'ERROR' as Status, @ErrorMessage as Message;
        
        THROW 50001, @ErrorMessage, 1;
    END CATCH
END;

-- FIN BLOQUE B
-- ================================================================
-- BLOQUE C: PERMISOS Y DOCUMENTACIÓN
-- EJECUTAR: Seleccionar desde aquí hasta el final
-- ================================================================

-- Conceder permisos al procedimiento
-- GRANT EXECUTE ON dbo.SP_ConfigurarHostingerEmail TO public;
SELECT '✅ Procedimiento creado exitosamente' AS Resultado;
SELECT '✅ Listo para usar SP_ConfigurarHostingerEmail' AS Estado;

-- =====================================================
-- DOCUMENTACIÓN Y EJEMPLOS DE USO
-- =====================================================
SELECT '📖 EJEMPLO DE USO:' AS Documentacion;
SELECT '
EXEC SP_ConfigurarHostingerEmail
    @EmailUsuario = ''miempresa@midominio.com'',
    @ContraseñaPlana = ''mi_contraseña_segura'',
    @NombreRemitente = ''Mi Empresa - Notificaciones'',
    @NombrePerfil = ''Hostinger Producción'',
    @EstablecerPorDefecto = 1,
    @Puerto = 587,
    @UsarSSL = 1,
    @CreadoPor = ''Admin'';
' AS EjemploCompleto;

-- FIN BLOQUE C
-- ================================================================
-- PROCEDIMIENTO LISTO PARA USAR EN DBEAVER
-- SIGUIENTE PASO: Personalizar ejecutar_configuracion_hostinger.sql
-- ================================================================