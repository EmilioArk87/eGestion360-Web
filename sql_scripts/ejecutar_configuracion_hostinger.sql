-- =========================================================================
-- SCRIPT: Ejecutar configuración de Hostinger.es
-- PROPÓSITO: Ejemplo de uso del procedimiento SP_ConfigurarHostingerEmail
-- AUTOR: Sistema eGestion360  
-- FECHA: 2026-04-10
-- =========================================================================

-- =============================================
-- PARÁMETROS A CONFIGURAR
-- =============================================
-- ⚠️ IMPORTANTE: Cambia estos valores por los de tu cuenta real

DECLARE @MiEmail            NVARCHAR(100) = 'miempresa@midominio.com';     -- 📧 Tu email de Hostinger
DECLARE @MiContraseña       NVARCHAR(200) = 'mi_contraseña_segura';       -- 🔐 Tu contraseña de email
DECLARE @MiNombreEmpresa    NVARCHAR(100) = 'Mi Empresa - Sistema';       -- 🏢 Nombre de tu empresa
DECLARE @NombrePerfil       NVARCHAR(50)  = 'Hostinger Producción';       -- 📋 Nombre del perfil
DECLARE @Puerto             INT = 587;                                     -- 🔧 Puerto (587 o 465)
DECLARE @ActivarSSL         BIT = 1;                                       -- 🔒 SSL (siempre recomendado)
DECLARE @EsPorDefecto       BIT = 1;                                       -- ⭐ Configuración por defecto

-- =============================================
-- VERIFICAR CONFIGURACIONES EXISTENTES
-- =============================================
PRINT '📊 CONFIGURACIONES DE EMAIL EXISTENTES:';
PRINT '============================================';

SELECT 
    Id,
    ProfileName as [Perfil],
    FromEmail as [Email],
    SmtpHost as [Servidor],
    SmtpPort as [Puerto],
    IsActive as [Activo],
    IsDefault as [Por Defecto],
    CreatedAt as [Creado]
FROM EmailConfiguration
ORDER BY CreatedAt DESC;

PRINT '';

-- =============================================
-- EJECUTAR CONFIGURACIÓN DE HOSTINGER
-- =============================================
PRINT '🚀 EJECUTANDO CONFIGURACIÓN DE HOSTINGER.ES...';
PRINT '===============================================';

EXEC SP_ConfigurarHostingerEmail
    @EmailUsuario         = @MiEmail,
    @ContraseñaPlana      = @MiContraseña,
    @NombreRemitente      = @MiNombreEmpresa,
    @NombrePerfil         = @NombrePerfil,
    @EstablecerPorDefecto = @EsPorDefecto,
    @Puerto               = @Puerto,
    @UsarSSL              = @ActivarSSL,
    @CreadoPor            = 'Script-Setup';

-- =============================================
-- VERIFICAR RESULTADO
-- =============================================
PRINT '';
PRINT '📋 CONFIGURACIÓN DESPUÉS DE LA EJECUCIÓN:';
PRINT '=========================================';

SELECT 
    Id,
    ProfileName as [Perfil],
    FromEmail as [Email],
    FromName as [Nombre Remitente],
    SmtpHost as [Servidor SMTP],
    SmtpPort as [Puerto],
    UseSsl as [SSL],
    Username as [Usuario],
    CASE 
        WHEN PasswordHash LIKE 'PENDIENTE_ENCRIPTAR:%' THEN '🔄 Pendiente Encriptar'
        WHEN LEN(PasswordHash) > 50 THEN '🔒 Encriptada'
        ELSE '⚠️ Sin Encriptar'
    END as [Estado Contraseña],
    IsActive as [Activo],
    IsDefault as [Por Defecto],
    CreatedAt as [Fecha Creación],
    CreatedBy as [Creado Por]
FROM EmailConfiguration
WHERE SmtpHost = 'smtp.hostinger.com'
ORDER BY CreatedAt DESC;

-- =============================================
-- PRÓXIMOS PASOS
-- =============================================
PRINT '';
PRINT '✅ CONFIGURACIÓN COMPLETADA - PRÓXIMOS PASOS:';
PRINT '============================================';
PRINT '1️⃣ Ir a la página web: /EncryptPasswords';
PRINT '2️⃣ Encriptar las contraseñas pendientes';
PRINT '3️⃣ Probar el envío de correos de prueba';
PRINT '4️⃣ Verificar que funcione el reset de contraseñas';
PRINT '';
PRINT '🔗 URLs importantes:';
PRINT '   📧 Encriptar contraseñas: https://tudominio.com/EncryptPasswords';
PRINT '   🧪 Página de pruebas: https://tudominio.com/Admin (si existe)';
PRINT '';

-- =============================================
-- CONSULTAS ÚTILES PARA ADMINISTRACIÓN
-- =============================================
PRINT '📚 CONSULTAS ÚTILES PARA EL FUTURO:';
PRINT '===================================';
PRINT '';
PRINT '-- Ver todas las configuraciones:';
PRINT 'SELECT * FROM EmailConfiguration ORDER BY CreatedAt DESC;';
PRINT '';
PRINT '-- Establecer una configuración como por defecto:';
PRINT 'UPDATE EmailConfiguration SET IsDefault = 0; -- Quitar defecto de todas';
PRINT 'UPDATE EmailConfiguration SET IsDefault = 1 WHERE Id = [ID_CONFIG];';
PRINT '';
PRINT '-- Desactivar una configuración:';
PRINT 'UPDATE EmailConfiguration SET IsActive = 0 WHERE Id = [ID_CONFIG];';
PRINT '';
PRINT '-- Eliminar configuración (cuidado):';
PRINT 'DELETE FROM EmailConfiguration WHERE Id = [ID_CONFIG];';
PRINT '';

-- =============================================
-- CONFIGURACIONES ALTERNATIVAS DE HOSTINGER
-- =============================================
PRINT '⚙️ CONFIGURACIONES ALTERNATIVAS DE HOSTINGER:';  
PRINT '=============================================';
PRINT '🔧 Puerto 465 (SSL): Usar UseSsl=1, Puerto=465';
PRINT '🔧 Puerto 587 (STARTTLS): Usar UseSsl=1, Puerto=587';
PRINT '🔧 Puerto 25: Generalmente bloqueado por ISPs';
PRINT '';
PRINT '📝 Ejemplo para puerto 465:';
PRINT 'EXEC SP_ConfigurarHostingerEmail @Puerto=465, @UsarSSL=1, ...';
PRINT '';