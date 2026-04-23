-- =========================================================================
-- CONSULTAS PARA ESTADÍSTICAS Y VALIDACIÓN DE EMAILS
-- PROPÓSITO: Herramientas SQL para validar el funcionamiento del sistema de emails
-- FECHA: 2026-04-12
-- =========================================================================

-- =============================================
-- 1. RESUMEN GENERAL DEL SISTEMA DE EMAIL
-- =============================================

PRINT '📊 RESUMEN GENERAL DEL SISTEMA DE EMAIL';
PRINT '======================================';

SELECT 
    '📧 CONFIGURACIONES' AS Seccion,
    COUNT(*) AS Total,
    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS Activas,
    SUM(CASE WHEN IsDefault = 1 THEN 1 ELSE 0 END) AS PorDefecto,
    SUM(CASE WHEN PasswordHash NOT LIKE 'PENDIENTE_%' AND PasswordHash != 'CONFIGURAR_CONTRASEÑA_ENCRIPTADA' THEN 1 ELSE 0 END) AS Encriptadas
FROM EmailConfiguration

UNION ALL

SELECT 
    '🔑 CÓDIGOS RESET' AS Seccion,
    COUNT(*) AS Total,
    SUM(CASE WHEN ExpiryDate > GETUTCDATE() THEN 1 ELSE 0 END) AS Vigentes,
    SUM(CASE WHEN IsUsed = 1 THEN 1 ELSE 0 END) AS Utilizados,
    SUM(CASE WHEN ExpiryDate <= GETUTCDATE() AND IsUsed = 0 THEN 1 ELSE 0 END) AS Expirados
FROM PasswordResetCodes;

-- =============================================
-- 2. CONFIGURACIONES DETALLADAS
-- =============================================

PRINT '';
PRINT '🔧 CONFIGURACIONES DE EMAIL DETALLADAS';
PRINT '=====================================';

SELECT 
    Id,
    ProfileName AS [Perfil],
    FromEmail AS [Email],
    SmtpHost + ':' + CAST(SmtpPort AS NVARCHAR(10)) AS [Servidor SMTP],
    CASE 
        WHEN UseSsl = 1 THEN '🔒 SSL/TLS'
        ELSE '⚠️ Sin SSL'
    END AS [Seguridad],
    CASE 
        WHEN IsActive = 1 AND IsDefault = 1 THEN '⭐ Activa (Por Defecto)'
        WHEN IsActive = 1 THEN '✅ Activa'
        ELSE '❌ Inactiva'
    END AS [Estado],
    CASE 
        WHEN PasswordHash LIKE 'PENDIENTE_ENCRIPTAR:%' THEN '🔄 Pendiente Encriptar'
        WHEN PasswordHash = 'CONFIGURAR_CONTRASEÑA_ENCRIPTADA' THEN '⚠️ Sin Configurar'
        WHEN LEN(PasswordHash) > 50 THEN '🔒 Encriptada'
        ELSE '❓ Estado Desconocido'
    END AS [Estado Contraseña],
    TestEmailsSent AS [Emails Enviados],
    CASE 
        WHEN LastTestedAt IS NULL THEN 'Nunca probado'
        ELSE FORMAT(LastTestedAt, 'dd/MM/yyyy HH:mm')
    END AS [Última Prueba],
    DATEDIFF(DAY, CreatedAt, GETUTCDATE()) AS [Días Desde Creación]
FROM EmailConfiguration
ORDER BY IsDefault DESC, IsActive DESC, TestEmailsSent DESC;

-- =============================================
-- 3. ESTADÍSTICAS DE USO POR CONFIGURACIÓN
-- =============================================

PRINT '';
PRINT '📈 ESTADÍSTICAS DE USO DE EMAIL';
PRINT '==============================';

SELECT 
    ProfileName AS [Configuración],
    TestEmailsSent AS [Emails de Prueba],
    CASE 
        WHEN TestEmailsSent = 0 THEN 'Sin usar'
        WHEN TestEmailsSent < 5 THEN 'Uso bajo'
        WHEN TestEmailsSent < 20 THEN 'Uso moderado'
        ELSE 'Uso alto'
    END AS [Nivel de Uso],
    CASE 
        WHEN LastTestedAt IS NULL THEN 'Nunca'
        WHEN LastTestedAt > DATEADD(DAY, -1, GETUTCDATE()) THEN 'Reciente (< 24h)'
        WHEN LastTestedAt > DATEADD(DAY, -7, GETUTCDATE()) THEN 'Esta semana'
        WHEN LastTestedAt > DATEADD(DAY, -30, GETUTCDATE()) THEN 'Este mes'
        ELSE 'Hace más de un mes'
    END AS [Actividad Reciente],
    FORMAT(LastTestedAt, 'dd/MM/yyyy HH:mm') AS [Última Prueba Exitosa]
FROM EmailConfiguration
WHERE IsActive = 1
ORDER BY TestEmailsSent DESC, LastTestedAt DESC;

-- =============================================
-- 4. CÓDIGOS DE RESET - ANÁLISIS DE SEGURIDAD
-- =============================================

PRINT '';
PRINT '🔑 ANÁLISIS DE CÓDIGOS DE RESET';
PRINT '==============================';

SELECT 
    'Códigos Generados Hoy' AS Metrica,
    COUNT(*) AS Valor
FROM PasswordResetCodes
WHERE CreatedAt >= CAST(GETUTCDATE() AS DATE)

UNION ALL

SELECT 
    'Códigos Vigentes',
    COUNT(*)
FROM PasswordResetCodes
WHERE ExpiryDate > GETUTCDATE() AND IsUsed = 0

UNION ALL

SELECT 
    'Códigos Expirados Sin Usar',
    COUNT(*)
FROM PasswordResetCodes
WHERE ExpiryDate <= GETUTCDATE() AND IsUsed = 0

UNION ALL

SELECT 
    'Códigos Utilizados Exitosamente',
    COUNT(*)
FROM PasswordResetCodes
WHERE IsUsed = 1

UNION ALL

SELECT 
    'Promedio Tiempo Uso (minutos)',
    AVG(DATEDIFF(MINUTE, CreatedAt, UsedAt))
FROM PasswordResetCodes
WHERE IsUsed = 1 AND UsedAt IS NOT NULL;

-- =============================================
-- 5. ÚLTIMOS CÓDIGOS DE RESET GENERADOS
-- =============================================

PRINT '';
PRINT '📋 ÚLTIMOS CÓDIGOS DE RESET (10 más recientes)';
PRINT '=============================================';

SELECT TOP 10
    Username AS [Usuario],
    Code AS [Código],
    FORMAT(CreatedAt, 'dd/MM/yyyy HH:mm:ss') AS [Generado],
    FORMAT(ExpiryDate, 'dd/MM/yyyy HH:mm:ss') AS [Expira],
    CASE 
        WHEN IsUsed = 1 THEN '✅ Usado'
        WHEN ExpiryDate > GETUTCDATE() THEN '⏳ Vigente'
        ELSE '❌ Expirado'
    END AS [Estado],
    CASE 
        WHEN IsUsed = 1 AND UsedAt IS NOT NULL THEN FORMAT(UsedAt, 'dd/MM/yyyy HH:mm:ss')
        ELSE 'No usado'
    END AS [Fecha Uso],
    IP AS [IP Origen]
FROM PasswordResetCodes
ORDER BY CreatedAt DESC;

-- =============================================
-- 6. DIAGNÓSTICO DE PROBLEMAS COMUNES
-- =============================================

PRINT '';
PRINT '🚨 DIAGNÓSTICO DE PROBLEMAS POTENCIALES';
PRINT '======================================';

-- Configuraciones problemáticas
SELECT 
    'PROBLEMAS ENCONTRADOS' AS Categoria,
    Problema,
    Cantidad
FROM (
    SELECT 
        'Configuraciones inactivas marcadas como por defecto' AS Problema,
        COUNT(*) AS Cantidad
    FROM EmailConfiguration 
    WHERE IsActive = 0 AND IsDefault = 1
    
    UNION ALL
    
    SELECT 
        'Configuraciones sin encriptar contraseñas',
        COUNT(*)
    FROM EmailConfiguration 
    WHERE PasswordHash IN ('CONFIGURAR_CONTRASEÑA_ENCRIPTADA', '') OR PasswordHash LIKE 'PENDIENTE_%'
    
    UNION ALL
    
    SELECT 
        'Configuraciones activas nunca probadas',
        COUNT(*)
    FROM EmailConfiguration 
    WHERE IsActive = 1 AND LastTestedAt IS NULL
    
    UNION ALL
    
    SELECT 
        'Códigos de reset expirados sin limpiar (>7 días)',
        COUNT(*)
    FROM PasswordResetCodes 
    WHERE ExpiryDate < DATEADD(DAY, -7, GETUTCDATE())
) AS Problemas
WHERE Cantidad > 0;

-- =============================================
-- 7. RECOMENDACIONES DE MANTENIMIENTO
-- =============================================

PRINT '';
PRINT '💡 RECOMENDACIONES DE MANTENIMIENTO';
PRINT '==================================';

-- Configuraciones que necesitan atención
IF EXISTS (SELECT 1 FROM EmailConfiguration WHERE PasswordHash LIKE 'PENDIENTE_%' OR PasswordHash = 'CONFIGURAR_CONTRASEÑA_ENCRIPTADA')
BEGIN
    PRINT '1. ⚠️ HAY CONFIGURACIONES CON CONTRASEÑAS SIN ENCRIPTAR';
    PRINT '   Solución: Ir a /EncryptPasswords y encriptar todas las contraseñas';
    PRINT '';
END

IF EXISTS (SELECT 1 FROM EmailConfiguration WHERE IsActive = 1 AND LastTestedAt IS NULL)
BEGIN
    PRINT '2. 📧 HAY CONFIGURACIONES ACTIVAS SIN PROBAR';
    PRINT '   Solución: Ir a /ValidarEmails y probar todas las configuraciones';
    PRINT '';
END

IF EXISTS (SELECT 1 FROM PasswordResetCodes WHERE ExpiryDate < DATEADD(DAY, -7, GETUTCDATE()))
BEGIN
    PRINT '3. 🧹 HAY CÓDIGOS ANTIGUOS QUE PUEDEN LIMPIARSE';
    PRINT '   Solución: Ejecutar limpieza de códigos expirados';
    PRINT '';
END

-- =============================================
-- 8. COMANDOS ÚTILES PARA ADMINISTRACIÓN
-- =============================================

PRINT '⚡ COMANDOS ÚTILES PARA ADMINISTRACIÓN';
PRINT '====================================';
PRINT '-- Probar configuración específica:';
PRINT 'EXEC sp_UpdateEmailTestStats @Id = [ID_CONFIGURACION], @Success = 1;';
PRINT '';
PRINT '-- Limpiar códigos expirados:';
PRINT 'DELETE FROM PasswordResetCodes WHERE ExpiryDate < DATEADD(DAY, -7, GETUTCDATE());';
PRINT '';
PRINT '-- Ver configuración por defecto:';
PRINT 'SELECT * FROM EmailConfiguration WHERE IsDefault = 1;';
PRINT '';
PRINT '-- Activar configuración:';
PRINT 'UPDATE EmailConfiguration SET IsActive = 1 WHERE Id = [ID_CONFIGURACION];';
PRINT '';

-- =============================================
-- 9. EXPORTAR DATOS PARA ANÁLISIS
-- =============================================

PRINT '';
PRINT '📊 DATOS PARA ANÁLISIS EXTERNO';
PRINT '=============================';

-- Configuraciones para exportar (sin contraseñas)
SELECT 
    Id,
    ProfileName,
    Provider,
    FromEmail,
    FromName,
    SmtpHost,
    SmtpPort,
    UseSsl,
    Username,
    '***OCULTO***' AS PasswordHash,
    IsActive,
    IsDefault,
    CreatedAt,
    UpdatedAt,
    CreatedBy,
    LastTestedAt,
    TestEmailsSent
FROM EmailConfiguration
ORDER BY CreatedAt DESC;

PRINT '';
PRINT '✅ ANÁLISIS DE EMAILS COMPLETADO';
PRINT '===============================';
PRINT '📧 Usa /ValidarEmails para pruebas interactivas';
PRINT '🔧 Usa /ConfigurarHostinger para gestión de configuraciones';
PRINT '🔐 Usa /EncryptPasswords para seguridad';