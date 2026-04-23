-- =========================================================================
-- SCRIPT: Validación Completa del Sistema de Emails
-- PROPÓSITO: Script que ejecuta todas las validaciones necesarias
-- USO: Ejecutar este script después de configurar emails para validar todo
-- FECHA: 2026-04-12
-- =========================================================================

PRINT '🚀 INICIANDO VALIDACIÓN COMPLETA DEL SISTEMA DE EMAILS';
PRINT '=====================================================';
PRINT 'Fecha: ' + FORMAT(GETUTCDATE(), 'dd/MM/yyyy HH:mm:ss');
PRINT '';

-- Variables para el resultado
DECLARE @ProblemasEncontrados INT = 0;
DECLARE @ConfiguracionesActivas INT = 0;
DECLARE @ConfiguracionesEncriptadas INT = 0;

-- =============================================
-- VALIDACIÓN 1: CONFIGURACIONES BÁSICAS
-- =============================================

PRINT '🔧 VALIDACIÓN 1: CONFIGURACIONES BÁSICAS';
PRINT '=======================================';

SELECT @ConfiguracionesActivas = COUNT(*) 
FROM EmailConfiguration WHERE IsActive = 1;

SELECT @ConfiguracionesEncriptadas = COUNT(*) 
FROM EmailConfiguration 
WHERE IsActive = 1 
  AND PasswordHash NOT LIKE 'PENDIENTE_%' 
  AND PasswordHash != 'CONFIGURAR_CONTRASEÑA_ENCRIPTADA'
  AND LEN(PasswordHash) > 50;

IF @ConfiguracionesActivas = 0
BEGIN
    PRINT '❌ ERROR CRÍTICO: No hay configuraciones activas';
    SET @ProblemasEncontrados = @ProblemasEncontrados + 1;
END
ELSE
BEGIN
    PRINT '✅ Configuraciones activas: ' + CAST(@ConfiguracionesActivas AS NVARCHAR(10));
END

IF @ConfiguracionesEncriptadas = 0
BEGIN
    PRINT '❌ ERROR: No hay configuraciones con contraseñas encriptadas';
    SET @ProblemasEncontrados = @ProblemasEncontrados + 1;
END
ELSE
BEGIN
    PRINT '✅ Configuraciones encriptadas: ' + CAST(@ConfiguracionesEncriptadas AS NVARCHAR(10));
END

-- Verificar configuración por defecto
IF NOT EXISTS (SELECT 1 FROM EmailConfiguration WHERE IsDefault = 1 AND IsActive = 1)
BEGIN
    PRINT '❌ ERROR: No hay configuración por defecto activa';
    SET @ProblemasEncontrados = @ProblemasEncontrados + 1;
END
ELSE
BEGIN
    PRINT '✅ Configuración por defecto establecida';
END

PRINT '';

-- =============================================
-- VALIDACIÓN 2: CONFIGURACIONES DE HOSTINGER
-- =============================================

PRINT '📧 VALIDACIÓN 2: CONFIGURACIONES DE HOSTINGER';
PRINT '===========================================';

DECLARE @HostingerConfigs INT = 0;
SELECT @HostingerConfigs = COUNT(*) 
FROM EmailConfiguration 
WHERE SmtpHost LIKE '%hostinger%';

IF @HostingerConfigs > 0
BEGIN
    PRINT '✅ Configuraciones de Hostinger encontradas: ' + CAST(@HostingerConfigs AS NVARCHAR(10));
    
    -- Detalles de configuraciones Hostinger
    SELECT 
        Id,
        ProfileName AS [Perfil],
        FromEmail AS [Email],
        SmtpPort AS [Puerto],
        CASE WHEN UseSsl = 1 THEN '🔒 SSL' ELSE '⚠️ Sin SSL' END AS [SSL],
        CASE 
            WHEN IsActive = 1 AND IsDefault = 1 THEN '⭐ Activa (Default)'
            WHEN IsActive = 1 THEN '✅ Activa'
            ELSE '❌ Inactiva'
        END AS [Estado],
        TestEmailsSent AS [Emails Enviados]
    FROM EmailConfiguration 
    WHERE SmtpHost LIKE '%hostinger%'
    ORDER BY IsDefault DESC, IsActive DESC;
    
    -- Validar configuraciones Hostinger problemáticas
    IF EXISTS (SELECT 1 FROM EmailConfiguration WHERE SmtpHost LIKE '%hostinger%' AND (SmtpPort NOT IN (587, 465) OR UseSsl = 0))
    BEGIN
        PRINT '⚠️ ADVERTENCIA: Configuración Hostinger con puerto/SSL no recomendado';
        SET @ProblemasEncontrados = @ProblemasEncontrados + 1;
    END
END
ELSE
BEGIN
    PRINT 'ℹ️ No hay configuraciones de Hostinger (es opcional)';
END

PRINT '';

-- =============================================
-- VALIDACIÓN 3: CÓDIGOS DE RESET
-- =============================================

PRINT '🔑 VALIDACIÓN 3: CÓDIGOS DE RESET';
PRINT '===============================';

DECLARE @CodigosVigentes INT = 0;
DECLARE @CodigosExpirados INT = 0;
DECLARE @CodigosAntiguos INT = 0;

SELECT @CodigosVigentes = COUNT(*) 
FROM PasswordResetCodes 
WHERE ExpiresAt > GETUTCDATE() AND IsUsed = 0;

SELECT @CodigosExpirados = COUNT(*) 
FROM PasswordResetCodes 
WHERE ExpiresAt <= GETUTCDATE() AND IsUsed = 0;

SELECT @CodigosAntiguos = COUNT(*) 
FROM PasswordResetCodes 
WHERE CreatedAt < DATEADD(DAY, -30, GETUTCDATE());

PRINT 'Códigos vigentes: ' + CAST(@CodigosVigentes AS NVARCHAR(10));
PRINT 'Códigos expirados: ' + CAST(@CodigosExpirados AS NVARCHAR(10));

IF @CodigosAntiguos > 100
BEGIN
    PRINT '⚠️ ADVERTENCIA: Muchos códigos antiguos (' + CAST(@CodigosAntiguos AS NVARCHAR(10)) + '). Considerar limpieza';
    SET @ProblemasEncontrados = @ProblemasEncontrados + 1;
END
ELSE
BEGIN
    PRINT '✅ Cantidad de códigos bajo control';
END

PRINT '';

-- =============================================
-- VALIDACIÓN 4: CONSTRAINTS Y INTEGRIDAD
-- =============================================

PRINT '🛡️ VALIDACIÓN 4: CONSTRAINTS Y INTEGRIDAD';
PRINT '========================================';

-- Verificar violaciones del constraint principal
DECLARE @ViolacionesConstraint INT = 0;
SELECT @ViolacionesConstraint = COUNT(*) 
FROM EmailConfiguration 
WHERE IsActive = 0 AND IsDefault = 1;

IF @ViolacionesConstraint > 0
BEGIN
    PRINT '❌ ERROR CRÍTICO: Violaciones del constraint CK_EmailConfiguration_OnlyOneDefault';
    PRINT '   Configuraciones inactivas marcadas como por defecto: ' + CAST(@ViolacionesConstraint AS NVARCHAR(10));
    PRINT '   Solución: Ejecutar script corregir_constraint_email_configuration.sql';
    SET @ProblemasEncontrados = @ProblemasEncontrados + 1;
END
ELSE
BEGIN
    PRINT '✅ No hay violaciones de constraints';
END

-- Verificar emails con formato válido
DECLARE @EmailsInvalidos INT = 0;
SELECT @EmailsInvalidos = COUNT(*) 
FROM EmailConfiguration 
WHERE FromEmail NOT LIKE '%@%.%';

IF @EmailsInvalidos > 0
BEGIN
    PRINT '❌ ERROR: Emails con formato inválido: ' + CAST(@EmailsInvalidos AS NVARCHAR(10));
    SET @ProblemasEncontrados = @ProblemasEncontrados + 1;
END
ELSE
BEGIN
    PRINT '✅ Todos los emails tienen formato válido';
END

PRINT '';

-- =============================================
-- VALIDACIÓN 5: ESTADÍSTICAS Y USO
-- =============================================

PRINT '📊 VALIDACIÓN 5: ESTADÍSTICAS Y USO';
PRINT '=================================';

DECLARE @TotalEmailsEnviados INT = 0;
DECLARE @ConfigsSinUso INT = 0;
DECLARE @ConfigsSinProbar INT = 0;

SELECT @TotalEmailsEnviados = SUM(TestEmailsSent) FROM EmailConfiguration;
SELECT @ConfigsSinUso = COUNT(*) FROM EmailConfiguration WHERE TestEmailsSent = 0 AND IsActive = 1;
SELECT @ConfigsSinProbar = COUNT(*) FROM EmailConfiguration WHERE LastTestedAt IS NULL AND IsActive = 1;

PRINT 'Total emails enviados: ' + CAST(@TotalEmailsEnviados AS NVARCHAR(10));

IF @ConfigsSinUso > 0
BEGIN
    PRINT '⚠️ ADVERTENCIA: Configuraciones activas sin uso: ' + CAST(@ConfigsSinUso AS NVARCHAR(10));
    PRINT '   Recomendación: Probar usando /ValidarEmails';
END

IF @ConfigsSinProbar > 0
BEGIN
    PRINT '⚠️ ADVERTENCIA: Configuraciones sin probar: ' + CAST(@ConfigsSinProbar AS NVARCHAR(10));
    SET @ProblemasEncontrados = @ProblemasEncontrados + 1;
END

PRINT '';

-- =============================================
-- RESUMEN Y RECOMENDACIONES
-- =============================================

PRINT '📋 RESUMEN DE VALIDACIÓN COMPLETA';
PRINT '===============================';

IF @ProblemasEncontrados = 0
BEGIN
    PRINT '🎉 ¡EXCELENTE! El sistema de emails está funcionando correctamente';
    PRINT '✅ Todas las validaciones pasaron exitosamente';
    PRINT '';
    PRINT '📧 Sistema listo para enviar correos';
    PRINT '🔧 No se necesitan acciones correctivas';
END
ELSE
BEGIN
    PRINT '⚠️ Se encontraron ' + CAST(@ProblemasEncontrados AS NVARCHAR(10)) + ' problemas que necesitan atención';
    PRINT '';
    
    PRINT '🛠️ ACCIONES RECOMENDADAS:';
    
    IF @ConfiguracionesActivas = 0
        PRINT '1. ⚡ URGENTE: Configurar al menos un email usando /ConfigurarHostinger';
    
    IF @ConfiguracionesEncriptadas = 0
        PRINT '2. 🔐 URGENTE: Encriptar contraseñas usando /EncryptPasswords';
    
    IF @ViolacionesConstraint > 0
        PRINT '3. 🔧 URGENTE: Ejecutar script corregir_constraint_email_configuration.sql';
    
    IF @ConfigsSinProbar > 0
        PRINT '4. 📧 Probar configuraciones usando /ValidarEmails';
    
    IF @CodigosAntiguos > 100
        PRINT '5. 🧹 Limpiar códigos antiguos (opcional)';
END

PRINT '';

-- =============================================
-- PRÓXIMOS PASOS SUGERIDOS
-- =============================================

PRINT '🚀 PRÓXIMOS PASOS SUGERIDOS';
PRINT '==========================';
PRINT '1. 🌐 Probar envío: /ValidarEmails';
PRINT '2. 🔧 Gestionar configuraciones: /ConfigurarHostinger';
PRINT '3. 🔐 Encriptar contraseñas: /EncryptPasswords';
PRINT '4. 📊 Ver estadísticas: Ejecutar consultas_email_stats.sql';
PRINT '5. 📚 Documentación completa: VALIDACION_EMAILS_GUIA.md';

PRINT '';
PRINT '✅ VALIDACIÓN COMPLETADA';
PRINT '=====================';
PRINT 'Fecha fin: ' + FORMAT(GETUTCDATE(), 'dd/MM/yyyy HH:mm:ss');

-- Retornar estado final
IF @ProblemasEncontrados = 0
    PRINT 'RESULTADO: ¡SISTEMA SALUDABLE! 🎉'
ELSE
    PRINT 'RESULTADO: NECESITA ATENCIÓN (' + CAST(@ProblemasEncontrados AS NVARCHAR(10)) + ' problemas) ⚠️';