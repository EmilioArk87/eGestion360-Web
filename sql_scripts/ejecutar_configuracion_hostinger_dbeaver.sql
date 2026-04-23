-- =========================================================================
-- SCRIPT: Ejecutar configuración de Hostinger.es (OPTIMIZADO PARA DBEAVER)
-- PROPÓSITO: Ejemplo de uso del procedimiento SP_ConfigurarHostingerEmail
-- AUTOR: Sistema eGestion360  
-- FECHA: 2026-04-12
-- =========================================================================
-- INSTRUCCIONES PARA DBEAVER:
-- 1. PERSONALIZAR: Cambiar variables con tus datos reales (líneas 15-25)
-- 2. BLOQUE A: Diagnóstico inicial (líneas 27-45)
-- 3. BLOQUE B: Ejecutar configuración (líneas 47-65) 
-- 4. BLOQUE C: Verificar resultado (líneas 67-fin)
-- =========================================================================

-- ================================================================
-- PERSONALIZACIÓN OBLIGATORIA - CAMBIAR CON TUS DATOS REALES
-- ================================================================

-- ⚠️ IMPORTANTE: Cambia estos valores por los de tu cuenta real de Hostinger
DECLARE @MiEmail            NVARCHAR(100) = 'miempresa@midominio.com';     -- 📧 TU EMAIL DE HOSTINGER
DECLARE @MiContraseña       NVARCHAR(200) = 'mi_contraseña_segura';       -- 🔐 TU CONTRASEÑA REAL
DECLARE @MiNombreEmpresa    NVARCHAR(100) = 'Mi Empresa - Sistema';       -- 🏢 NOMBRE DE TU EMPRESA
DECLARE @NombrePerfil       NVARCHAR(50)  = 'Hostinger Producción';       -- 📋 NOMBRE DEL PERFIL
DECLARE @Puerto             INT = 587;                                     -- 🔧 PUERTO (587 o 465)
DECLARE @ActivarSSL         BIT = 1;                                       -- 🔒 SSL (recomendado: 1)
DECLARE @EsPorDefecto       BIT = 1;                                       -- ⭐ CONFIGURACIÓN POR DEFECTO

-- ================================================================
-- BLOQUE A: DIAGNÓSTICO INICIAL
-- EJECUTAR: Seleccionar desde aquí hasta "-- FIN BLOQUE A"
-- ================================================================

SELECT '📊 CONFIGURACIONES DE EMAIL EXISTENTES:' AS Diagnostico;

SELECT 
    Id,
    ProfileName as [Perfil],
    FromEmail as [Email],
    SmtpHost as [Servidor],
    SmtpPort as [Puerto],
    CASE WHEN IsActive = 1 THEN '✅ Activo' ELSE '❌ Inactivo' END as [Estado],
    CASE WHEN IsDefault = 1 THEN '⭐ Por Defecto' ELSE '' END as [Default],
    FORMAT(CreatedAt, 'dd/MM/yyyy HH:mm') as [Creado]
FROM EmailConfiguration
ORDER BY CreatedAt DESC;

-- FIN BLOQUE A
-- ================================================================
-- BLOQUE B: EJECUTAR CONFIGURACIÓN DE HOSTINGER
-- EJECUTAR: Seleccionar desde aquí hasta "-- FIN BLOQUE B"
-- IMPORTANTE: Asegúrate de personalizar las variables arriba
-- ================================================================

SELECT '🚀 EJECUTANDO CONFIGURACIÓN DE HOSTINGER.ES...' AS Accion;

-- Ejecutar el procedimiento con tus datos personalizados
EXEC SP_ConfigurarHostingerEmail
    @EmailUsuario         = @MiEmail,
    @ContraseñaPlana      = @MiContraseña,
    @NombreRemitente      = @MiNombreEmpresa,
    @NombrePerfil         = @NombrePerfil,
    @EstablecerPorDefecto = @EsPorDefecto,
    @Puerto               = @Puerto,
    @UsarSSL              = @ActivarSSL,
    @CreadoPor            = 'DBeaver-Setup';

-- FIN BLOQUE B
-- ================================================================
-- BLOQUE C: VERIFICAR RESULTADO
-- EJECUTAR: Seleccionar desde aquí hasta el final
-- ================================================================

SELECT '📋 CONFIGURACIÓN DESPUÉS DE LA EJECUCIÓN:' AS Verificacion;

SELECT 
    Id,
    ProfileName as [Perfil],
    FromEmail as [Email],
    FromName as [Nombre Remitente],
    SmtpHost as [Servidor SMTP],
    SmtpPort as [Puerto],
    CASE WHEN UseSsl = 1 THEN '🔒 SSL' ELSE '⚠️ Sin SSL' END as [SSL],
    Username as [Usuario],
    CASE 
        WHEN PasswordHash LIKE 'PENDIENTE_ENCRIPTAR:%' THEN '🔄 Pendiente Encriptar'
        WHEN LEN(PasswordHash) > 50 THEN '🔒 Encriptada'
        ELSE '⚠️ Sin Encriptar'
    END as [Estado Contraseña],
    CASE WHEN IsActive = 1 THEN '✅ Activa' ELSE '❌ Inactiva' END as [Activa],
    CASE WHEN IsDefault = 1 THEN '⭐ Por Defecto' ELSE '' END as [Por Defecto],
    FORMAT(CreatedAt, 'dd/MM/yyyy HH:mm') as [Fecha Creación],
    CreatedBy as [Creado Por]
FROM EmailConfiguration
WHERE SmtpHost = 'smtp.hostinger.com'
   OR SmtpHost LIKE '%hostinger%'
ORDER BY CreatedAt DESC;

-- =============================================
-- RESUMEN Y PRÓXIMOS PASOS
-- =============================================

SELECT '✅ CONFIGURACIÓN COMPLETADA' AS Estado;

-- Contar configuraciones de Hostinger creadas
DECLARE @HostingerConfigs INT;
SELECT @HostingerConfigs = COUNT(*) 
FROM EmailConfiguration 
WHERE SmtpHost LIKE '%hostinger%';

IF @HostingerConfigs > 0
BEGIN
    SELECT 'Configuraciones de Hostinger creadas: ' + CAST(@HostingerConfigs AS NVARCHAR(10)) AS Resultado;
    
    SELECT '🔐 PRÓXIMOS PASOS IMPORTANTES:' AS ProximosPasos;
    SELECT '1️⃣ Ir a la página web: /EncryptPasswords' AS Paso1;
    SELECT '2️⃣ Encriptar las contraseñas pendientes' AS Paso2;
    SELECT '3️⃣ Probar el envío: /ValidarEmails' AS Paso3;
    SELECT '4️⃣ Verificar funcionamiento del reset de contraseñas' AS Paso4;
    
    -- Mostrar si hay contraseñas pendientes de encriptar
    DECLARE @ContraseñasPendientes INT;
    SELECT @ContraseñasPendientes = COUNT(*)
    FROM EmailConfiguration
    WHERE PasswordHash LIKE 'PENDIENTE_ENCRIPTAR:%';
    
    IF @ContraseñasPendientes > 0
    BEGIN
        SELECT '⚠️ ATENCIÓN: ' + CAST(@ContraseñasPendientes AS NVARCHAR(10)) + ' contraseñas pendientes de encriptar' AS Advertencia;
    END
    ELSE
    BEGIN
        SELECT '✅ Todas las contraseñas están encriptadas' AS EstadoEncriptacion;
    END
END
ELSE
BEGIN
    SELECT '❌ ERROR: No se crearon configuraciones de Hostinger' AS Error;
    SELECT 'Revisar errores en la ejecución del procedimiento' AS Recomendacion;
END;

-- =============================================
-- CONSULTAS ÚTILES PARA ADMINISTRACIÓN
-- =============================================

SELECT '📚 CONSULTAS ÚTILES PARA EL FUTURO:' AS ConsultasUtiles;

SELECT '
-- Ver todas las configuraciones:
SELECT * FROM EmailConfiguration ORDER BY CreatedAt DESC;

-- Ver solo configuraciones de Hostinger:
SELECT * FROM EmailConfiguration WHERE SmtpHost LIKE ''%hostinger%'';

-- Establecer una configuración como por defecto:
EXEC sp_SetDefaultEmailConfigurationSafe @Id = [ID_CONFIG];

-- Ver estado de contraseñas:
SELECT Id, ProfileName, 
       CASE 
           WHEN PasswordHash LIKE ''PENDIENTE_%'' THEN ''Pendiente''
           WHEN LEN(PasswordHash) > 50 THEN ''Encriptada''
           ELSE ''Sin Encriptar''
       END as EstadoContraseña
FROM EmailConfiguration;
' AS EjemplosSQL;

-- FIN BLOQUE C
-- ================================================================
-- CONFIGURACIÓN DE HOSTINGER COMPLETADA PARA DBEAVER
-- SIGUIENTE PASO: Ir a la aplicación web para encriptar contraseñas
-- ================================================================