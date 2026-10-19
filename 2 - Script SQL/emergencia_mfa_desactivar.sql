-- ============================================================
-- Script   : emergencia_mfa_desactivar.sql
-- Proposito: EMERGENCIA. Desactiva el MFA (TOTP) de UNA cuenta, por ejemplo si perdio el telefono y sus codigos de recuperacion: borra su
--            secreto (saas.cuenta_mfa) y sus codigos de recuperacion (saas.cuenta_mfa_recuperacion) para que vuelva a entrar solo con su clave.
--            NO borra la cuenta ni toca su clave, estado, membresias ni intentos fallidos. Si la cuenta debe tener MFA (operador de la
--            plataforma o administrador de tenant, D22), la aplicacion le pedira configurarlo de nuevo al iniciar sesion.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-15
-- BD       : eBD_SPD
-- Ejecucion: pensada para correrse a mano, con la identidad ya verificada por otro medio. ANTES de correrla hay que EDITAR la variable
--            @correo del inicio del archivo con el correo de la cuenta (con el valor de ejemplo no hace nada y avisa). Es un script sin PRECHECK
--            ni dependencias: es idempotente (si la cuenta no existe o no tiene MFA, no hace nada) y deja el rastro modificado_por =
--            'emergencia:mfa' en la cuenta. Por ser una EMERGENCIA no espera la aprobacion previa de /alerta-bd: se avisa a quien corresponda
--            despues y se revisa por que el usuario perdio el segundo factor.
-- ============================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- >>> EDITE ESTA LINEA con el correo de la cuenta cuyo MFA se desactiva <<<
DECLARE @correo NVARCHAR(256) = N'EDITAR-ESTE-CORREO';

IF @correo = N'EDITAR-ESTE-CORREO'
    THROW 50090, N'Edite la variable @correo al inicio del archivo con el correo de la cuenta. No se cambio nada.', 1;

IF OBJECT_ID(N'saas.cuentas', N'U') IS NULL OR OBJECT_ID(N'saas.cuenta_mfa', N'U') IS NULL OR OBJECT_ID(N'saas.cuenta_mfa_recuperacion', N'U') IS NULL
BEGIN
    PRINT N'Nada que hacer: las tablas de identidad (script 038) no existen en esta base.';
    RETURN;
END

DECLARE @id_cuenta INT = (SELECT id_cuenta FROM saas.cuentas WHERE correo = @correo);
IF @id_cuenta IS NULL
BEGIN
    PRINT N'Nada que hacer: no hay ninguna cuenta con ese correo.';
    RETURN;
END

DECLARE @tenia_mfa INT = (SELECT COUNT(*) FROM saas.cuenta_mfa WHERE id_cuenta = @id_cuenta);
DECLARE @codigos INT = (SELECT COUNT(*) FROM saas.cuenta_mfa_recuperacion WHERE id_cuenta = @id_cuenta);
IF @tenia_mfa = 0 AND @codigos = 0
BEGIN
    PRINT N'Nada que hacer: la cuenta no tiene MFA configurado.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    DELETE FROM saas.cuenta_mfa_recuperacion WHERE id_cuenta = @id_cuenta;
    DELETE FROM saas.cuenta_mfa WHERE id_cuenta = @id_cuenta;
    UPDATE saas.cuentas SET modificado_por = N'emergencia:mfa', fecha_modificacion = SYSUTCDATETIME() WHERE id_cuenta = @id_cuenta;

    IF EXISTS (SELECT 1 FROM saas.cuenta_mfa WHERE id_cuenta = @id_cuenta)
       OR EXISTS (SELECT 1 FROM saas.cuenta_mfa_recuperacion WHERE id_cuenta = @id_cuenta)
        THROW 50090, N'No se pudo desactivar el MFA de la cuenta. Se revierte.', 1;

    COMMIT TRANSACTION;

    PRINT CONCAT(N'MFA DESACTIVADO para la cuenta ', @id_cuenta, N' (', @tenia_mfa, N' secreto y ', @codigos, N' codigo(s) de recuperacion borrados). La cuenta sigue activa.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT c.id_cuenta, c.estado, c.es_operador,
       (SELECT COUNT(*) FROM saas.cuenta_mfa m WHERE m.id_cuenta = c.id_cuenta) AS secretos_mfa,
       (SELECT COUNT(*) FROM saas.cuenta_mfa_recuperacion r WHERE r.id_cuenta = c.id_cuenta) AS codigos_recuperacion
FROM saas.cuentas c WHERE c.id_cuenta = @id_cuenta;
