-- ======================================================================
-- SCRIPT: Comandos para Encriptar Contraseñas de Email Manualmente  
-- FECHA: 2026-04-12
-- PROPÓSITO: Guía para encriptar contraseñas pendientes en EmailConfiguration
-- IMPORTANTE: No ejecutar directamente, usar páginas web recomendadas
-- ======================================================================

PRINT '🔍 IDENTIFICANDO CONTRASEÑAS PENDIENTES DE ENCRIPTAR';
PRINT '===================================================';

-- Ver contraseñas que necesitan encriptación
SELECT 
    Id,
    ProfileName AS [Perfil],
    FromEmail AS [Email],
    PasswordHash AS [Estado Contraseña],
    CASE 
        WHEN PasswordHash LIKE 'PENDIENTE_ENCRIPTAR:%' THEN '⏳ Necesita Encriptar'
        WHEN PasswordHash = 'CONFIGURAR_CONTRASEÑA_ENCRIPTADA' THEN '❌ Sin Configurar'
        WHEN LEN(PasswordHash) > 50 AND PasswordHash NOT LIKE 'PENDIENTE_%' THEN '✅ Ya Encriptada'
        ELSE '❓ Estado Desconocido'
    END AS [Estado],
    IsActive AS [Activa],
    IsDefault AS [Por Defecto]
FROM EmailConfiguration
ORDER BY 
    CASE WHEN PasswordHash LIKE 'PENDIENTE_%' THEN 1 ELSE 2 END,
    IsDefault DESC, 
    IsActive DESC;

PRINT '';
PRINT '🚨 IMPORTANTE: MÉTODOS DE ENCRIPTACIÓN RECOMENDADOS';
PRINT '==================================================';
PRINT '';
PRINT '✅ RECOMENDADO - USAR PÁGINAS WEB:';
PRINT '   1. 🌐 /ConfigurarHostinger (nuevo email completo)';
PRINT '   2. 🛠️ /Admin/EmailConfig (editar existente)';
PRINT '   3. 📧 /ValidarEmails (probar configuraciones)';
PRINT '';
PRINT '❌ NO RECOMENDADO - Script SQL directo porque:';
PRINT '   • Los scripts SQL no pueden acceder al servicio .NET de encriptación';
PRINT '   • Necesitas las claves de encriptación desde appsettings.json';
PRINT '   • Es más seguro usar la aplicación web que maneja todo automáticamente';
PRINT '';
PRINT '🔑 CLAVES DE ENCRIPTACIÓN USADAS (desde appsettings.json):';
PRINT '   Key: eGestion360-EmailCrypt-2026-SecKey'; 
PRINT '   IV:  eGestion360-IV16';
PRINT '';

-- Mostrar configuraciones específicas que necesitan atención
DECLARE @PendientesEncriptar INT = 0;
SELECT @PendientesEncriptar = COUNT(*) 
FROM EmailConfiguration 
WHERE PasswordHash LIKE 'PENDIENTE_ENCRIPTAR:%';

IF @PendientesEncriptar > 0
BEGIN
    PRINT '⚠️ CONTRASEÑAS QUE NECESITAN ENCRIPTACIÓN: ' + CAST(@PendientesEncriptar AS NVARCHAR(10));
    PRINT '';
    PRINT '📋 PASOS A SEGUIR:';
    PRINT '1. Abrir aplicación web: http://localhost:5000';
    PRINT '2. Ir a /ConfigurarHostinger'; 
    PRINT '3. Configurar email con contraseña real';
    PRINT '4. O ir a /Admin/EmailConfig para editar existente';
    PRINT '5. Probar en /ValidarEmails';
    PRINT '';
    
    -- Mostrar comandos útiles para después de encriptar
    PRINT '✅ VERIFICAR DESPUÉS DE ENCRIPTAR:';
    PRINT 'SELECT Id, ProfileName, FromEmail, LEN(PasswordHash) as LongitudHash, IsActive, IsDefault';  
    PRINT 'FROM EmailConfiguration WHERE Id IN (' ;
    
    SELECT STRING_AGG(CAST(Id AS NVARCHAR), ', ') as IdsParaVerificar
    FROM EmailConfiguration 
    WHERE PasswordHash LIKE 'PENDIENTE_ENCRIPTAR:%';
    
    PRINT ');';
END
ELSE
BEGIN
    PRINT '✅ No hay contraseñas pendientes de encriptación';
    PRINT '🎉 Todas las configuraciones están correctamente encriptadas';
END

PRINT '';
PRINT '🚀 COMANDOS RÁPIDOS DESPUÉS DE ENCRIPTAR:';
PRINT '========================================';
PRINT '-- Ver todas las configuraciones:';
PRINT 'SELECT * FROM EmailConfiguration ORDER BY IsDefault DESC, IsActive DESC;';
PRINT '';
PRINT '-- Probar configuración por defecto:';
PRINT 'EXEC sp_GetActiveEmailConfiguration;';
PRINT '';
PRINT '-- Ver estadísticas:';  
PRINT 'SELECT COUNT(*) as Total, SUM(CAST(IsActive AS INT)) as Activas, SUM(CAST(IsDefault AS INT)) as PorDefecto FROM EmailConfiguration;';
PRINT '';
PRINT '✅ GUÍA COMPLETADA - USA LAS PÁGINAS WEB PARA ENCRIPTAR';
PRINT '======================================================';