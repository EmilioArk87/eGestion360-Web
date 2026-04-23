-- Script para configurar el email del usuario con datos de ejemplo
-- Este script debe ejecutarse después de crear la tabla EmailConfigurations

USE [eGestion360];

-- Verificar si ya existe una configuración predeterminada
IF NOT EXISTS (SELECT 1 FROM EmailConfigurations WHERE IsDefault = 1)
BEGIN
    -- Insertar configuración de Gmail de ejemplo
    INSERT INTO EmailConfigurations (
        ProfileName,
        Provider,
        FromEmail,
        FromName,
        SmtpHost,
        SmtpPort,
        UseSsl,
        Username,
        PasswordHash,
        IsActive,
        IsDefault,
        CreatedAt,
        UpdatedAt,
        CreatedBy,
        TestEmailsSent
    )
    VALUES (
        'Gmail Corporativo - Ejemplo',                    -- Nombre del perfil
        'Gmail',                                          -- Proveedor
        'tu-empresa@gmail.com',                          -- Email de la empresa (CAMBIAR)
        'eGestion360 - Tu Empresa',                     -- Nombre que aparecerá como remitente
        'smtp.gmail.com',                                -- Servidor SMTP de Gmail
        587,                                             -- Puerto SMTP
        1,                                               -- SSL habilitado
        'tu-empresa@gmail.com',                          -- Usuario SMTP (CAMBIAR)
        'PASSWORD_ENCRIPTADO_AQUI',                      -- Contraseña encriptada (SE DEBE CONFIGURAR)
        0,                                               -- Inactiva por defecto hasta configurar
        0,                                               -- No predeterminada hasta configurar
        GETDATE(),                                       -- Fecha creación
        GETDATE(),                                       -- Fecha actualización
        'System',                                        -- Creado por el sistema
        0                                                -- Emails de prueba enviados
    );

    PRINT 'Configuración de email de ejemplo creada.';
    PRINT 'IMPORTANTE: Debe configurar la contraseña real en la interfaz web.';
END
ELSE
BEGIN
    PRINT 'Ya existe una configuración predeterminada de email.';
END

GO

-- Mostrar instrucciones para el usuario
PRINT '=================================================================';
PRINT 'CONFIGURACIÓN DE EMAIL CORPORATIVO - INSTRUCCIONES';
PRINT '=================================================================';
PRINT '';
PRINT '1. Vaya a la página: http://localhost:5000/admin/email-config';
PRINT '';
PRINT '2. Para configurar Gmail corporativo necesita:';
PRINT '   - Email de su empresa (ej: info@suempresa.com)';
PRINT '   - Contraseña de aplicación específica de Gmail';
PRINT '   - Habilitar "Acceso de aplicaciones menos seguras" o usar OAuth2';
PRINT '';
PRINT '3. Para configurar SMTP personalizado necesita:';
PRINT '   - Servidor SMTP de su proveedor';
PRINT '   - Puerto (comúnmente 587 o 465)';
PRINT '   - Credenciales de autenticación';
PRINT '   - Configuración SSL/TLS';
PRINT '';
PRINT '4. Datos típicos por proveedor:';
PRINT '   Gmail:    smtp.gmail.com:587 (SSL)';
PRINT '   Outlook:  smtp-mail.outlook.com:587 (SSL)';
PRINT '   Yahoo:    smtp.mail.yahoo.com:587 (SSL)';
PRINT '';
PRINT '5. Después de configurar, pruebe el envío en la interfaz web';
PRINT '';
PRINT '=================================================================';

-- Verificar estado de la base de datos
SELECT 
    'Configuraciones Email' as Tabla,
    COUNT(*) as RegistrosTotal,
    COUNT(CASE WHEN IsActive = 1 THEN 1 END) as Activas,
    COUNT(CASE WHEN IsDefault = 1 THEN 1 END) as Predeterminadas
FROM EmailConfigurations;

SELECT 
    'Usuarios' as Tabla,
    COUNT(*) as RegistrosTotal
FROM Users;

SELECT 
    'Códigos Reset' as Tabla,
    COUNT(*) as RegistrosTotal,
    COUNT(CASE WHEN IsUsed = 0 AND ExpiresAt > GETDATE() THEN 1 END) as Válidos
FROM PasswordResetCodes;

PRINT '';
PRINT 'Estado actual de la base de datos mostrado arriba.';