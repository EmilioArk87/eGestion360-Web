-- ============================================================
-- Script   : 011_configurar_correo_notificaciones.sql
-- Proposito: Dar de alta el perfil SMTP de notificaciones del sistema
--            (notificaciones@siptecnologia.somee.com) en la tabla que
--            realmente consulta la aplicacion, y sanear las contrasenas
--            en texto plano que quedaron en la tabla huerfana.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-08-30
-- BD       : eBD_SPD
-- Requiere : dbo.EmailConfigurations (migracion EF AddEmailConfigurationTable)
-- Rollback : Ver seccion ROLLBACK al final
-- ============================================================
-- CONTEXTO IMPORTANTE
-- La base tiene DOS tablas casi identicas:
--   * dbo.EmailConfigurations (plural)  <- creada por EF el 2026-02-22.
--                                          Es la UNICA que lee la aplicacion
--                                          (DbSet<EmailConfiguration> del
--                                          ApplicationDbContext, sin ToTable).
--                                          Estaba VACIA: por eso no salia ningun correo.
--   * dbo.EmailConfiguration  (singular) <- creada a mano el 2026-04-10 por los
--                                          scripts de DBeaver. La usan los SP_*
--                                          de email, pero NO el envio real.
--                                          Tenia 4 filas con la contrasena SMTP
--                                          guardada EN TEXTO PLANO.
--
-- CIFRADO
-- PasswordHash NO es un hash: es AES-256-CBC + Base64 (Services/EncryptionService.cs),
-- reversible, con clave/IV tomados de Encryption__Key / Encryption__IV. El valor de
-- abajo fue cifrado con las claves cargadas en user-secrets el 2026-08-30. Si esas
-- claves cambian, este valor deja de descifrarse y hay que regenerarlo.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
GO

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmailConfigurations')
BEGIN
    RAISERROR('Falta dbo.EmailConfigurations. Ejecute la migracion EF AddEmailConfigurationTable antes. Abortar.', 16, 1);
    RETURN;
END;

-- Estado antes del cambio (para comparar con el POSTCHECK)
SELECT 'ANTES' AS momento,
       (SELECT COUNT(*) FROM dbo.EmailConfigurations) AS filas_plural_app,
       (SELECT COUNT(*) FROM sys.tables WHERE name = 'EmailConfiguration') AS existe_tabla_singular;

-- Cuantas contrasenas en claro hay hoy en la tabla huerfana
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmailConfiguration')
    SELECT 'ANTES' AS momento,
           COUNT(*) AS filas_con_password_en_claro
    FROM dbo.EmailConfiguration
    WHERE PasswordHash LIKE 'PENDIENTE[_]ENCRIPTAR%';
GO

-- ------------------------------------------------------------
-- CAMBIO
-- ------------------------------------------------------------
BEGIN TRANSACTION;

-- 1) Alta / actualizacion del perfil de notificaciones en la tabla que usa la app.
--    Idempotente: si el perfil ya existe se actualiza en lugar de duplicar
--    (ProfileName tiene indice unico).
DECLARE @ProfileName  nvarchar(50)  = N'Notificaciones Somee';
DECLARE @Email        nvarchar(100) = N'notificaciones@siptecnologia.somee.com';
DECLARE @FromName     nvarchar(100) = N'eGestion360 Notificaciones';
DECLARE @SmtpHost     nvarchar(100) = N'mail.siptecnologia.somee.com';
DECLARE @SmtpPort     int           = 465;   -- 465 = SSL implicito (SslOnConnect en MailKit)
DECLARE @UseSsl       bit           = 1;
-- Contrasena cifrada con AES-256-CBC (ver nota de CIFRADO en la cabecera)
DECLARE @PasswordEnc  nvarchar(500) = N'MHU2FSg/cslq5j6WWLJiAw==';

IF EXISTS (SELECT 1 FROM dbo.EmailConfigurations WHERE ProfileName = @ProfileName)
BEGIN
    UPDATE dbo.EmailConfigurations
       SET Provider     = N'SMTP',
           FromEmail    = @Email,
           FromName     = @FromName,
           SmtpHost     = @SmtpHost,
           SmtpPort     = @SmtpPort,
           UseSsl       = @UseSsl,
           Username     = @Email,
           PasswordHash = @PasswordEnc,
           IsActive     = 1,
           IsDefault    = 1,
           UpdatedAt    = SYSUTCDATETIME()
     WHERE ProfileName  = @ProfileName;

    PRINT 'Perfil actualizado: ' + @ProfileName;
END
ELSE
BEGIN
    INSERT INTO dbo.EmailConfigurations
        (ProfileName, Provider, FromEmail, FromName, SmtpHost, SmtpPort, UseSsl,
         Username, PasswordHash, IsActive, IsDefault, CreatedAt, UpdatedAt,
         CreatedBy, TestEmailsSent)
    VALUES
        (@ProfileName, N'SMTP', @Email, @FromName, @SmtpHost, @SmtpPort, @UseSsl,
         @Email, @PasswordEnc, 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME(),
         N'Script 011', 0);

    PRINT 'Perfil insertado: ' + @ProfileName;
END;

-- 2) Solo puede haber un perfil por defecto.
UPDATE dbo.EmailConfigurations
   SET IsDefault = 0,
       UpdatedAt = SYSUTCDATETIME()
 WHERE ProfileName <> @ProfileName
   AND IsDefault = 1;

-- 3) Saneamiento de la tabla huerfana: quitar las contrasenas en texto plano y
--    desactivar sus perfiles para que nadie los tome por buenos. No se borran las
--    filas ni la tabla: los SP_* de email siguen apuntando ahi y esa limpieza se
--    decide aparte.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmailConfiguration')
BEGIN
    UPDATE dbo.EmailConfiguration
       SET PasswordHash = N'CONFIGURAR_CONTRASENA_ENCRIPTADA',
           IsActive     = 0,
           IsDefault    = 0,
           UpdatedAt    = SYSUTCDATETIME()
     WHERE PasswordHash LIKE 'PENDIENTE[_]ENCRIPTAR%';

    PRINT 'Contrasenas en texto plano neutralizadas en dbo.EmailConfiguration: '
          + CAST(@@ROWCOUNT AS varchar(10));
END;

COMMIT TRANSACTION;
GO

-- ------------------------------------------------------------
-- POSTCHECK (verificacion)
-- ------------------------------------------------------------
-- Debe devolver exactamente 1 fila activa y por defecto, con el correo nuevo.
SELECT 'DESPUES' AS momento,
       Id, ProfileName, Provider, FromEmail, FromName,
       SmtpHost, SmtpPort, UseSsl, Username,
       LEN(PasswordHash) AS largo_password, IsActive, IsDefault, CreatedBy
FROM dbo.EmailConfigurations
ORDER BY IsDefault DESC, Id;

-- Debe devolver 0.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmailConfiguration')
    SELECT 'DESPUES' AS momento,
           COUNT(*) AS filas_con_password_en_claro
    FROM dbo.EmailConfiguration
    WHERE PasswordHash LIKE 'PENDIENTE[_]ENCRIPTAR%';
GO

-- ------------------------------------------------------------
-- ROLLBACK (ejecutar solo si hay que deshacer)
-- ------------------------------------------------------------
-- Nota: el paso 3 NO es reversible con este bloque; las contrasenas en claro que
-- se neutralizaron ya no se pueden recuperar desde la base (y no deberian volver).
-- (sin GO dentro del bloque: un GO dentro de un comentario parte el batch en
--  SSMS/DBeaver y deja el /* sin cerrar)
/*
DELETE FROM dbo.EmailConfigurations
 WHERE ProfileName = N'Notificaciones Somee';
*/
