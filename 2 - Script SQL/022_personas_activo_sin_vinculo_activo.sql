-- ============================================================
-- Script   : 022_personas_activo_sin_vinculo_activo.sql
-- Proposito: Corrige dbo.personas.activo de las personas que quedaron activas sin ningun vinculo activo.
--            Causa: al dar de baja a quien solo era cliente, VinculoService apagaba el vinculo y la ficha de
--            cliente pero no recalculaba personas.activo (PersonaService.CambiarEstadoAsync si lo hace con los
--            empleados). La aplicacion ya lo corrige al guardar (fase U0 del plan "Usuarios y personas");
--            este script ordena lo que ya estaba guardado.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-10
-- BD       : eBD_SPD
-- Requiere : scripts 014 a 019 aplicados.
-- Rollback : bloque comentado al final (vuelve a encender solo las personas que apago este script).
-- ============================================================
-- CLASIFICACION DE LOS CAMBIOS (para /alerta-bd)
--   [VERDE / AGREGA]
--     + dbo.bitacora_cambios: una fila por persona corregida (campo activo, true -> false,
--       usuario script_022, origen script:022).
--   [AMARILLO / MODIFICA]
--     ~ dbo.personas.activo = 0 (y modificado_por, fecha_modificacion) en las personas vivas que estan
--       activas y no tienen ningun vinculo activo. Al 2026-10-10 son 3, todas clientes de prueba de Demo
--       dados de baja (ids 97, 99 y 101).
--   [AZUL / IMPACTO]
--     * No toca vinculos, fichas, documentos ni a ninguna otra persona. Las pantallas que listan personal
--       leen los vinculos, no esta columna, asi que nadie cambia de lista.
--     * Tope de seguridad: si hubiera mas de 20 personas por corregir, aborta para revisarlas a mano.
--
-- Seguridad de ejecucion: una sola sesion, marca #precheck_022, una transaccion con el POSTCHECK adentro,
-- idempotente (si no queda nadie por corregir no cambia nada) y aprobacion /alerta-bd antes de ejecutar.
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- ------------------------------------------------------------
-- PRECHECK (validaciones previas; no cambia nada)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_022') IS NOT NULL DROP TABLE #precheck_022;
IF OBJECT_ID(N'tempdb..#plan_022') IS NOT NULL DROP TABLE #plan_022;
CREATE TABLE #precheck_022 (ok BIT NOT NULL, por_corregir INT NOT NULL);
CREATE TABLE #plan_022 (id_persona INT NOT NULL PRIMARY KEY, id_empresa INT NULL);

IF DB_NAME() <> N'eBD_SPD'
    THROW 50022, N'Este script es solo para la base eBD_SPD. Abortar.', 1;

IF OBJECT_ID(N'dbo.personas', N'U') IS NULL OR OBJECT_ID(N'dbo.persona_empresa', N'U') IS NULL
   OR OBJECT_ID(N'dbo.bitacora_cambios', N'U') IS NULL
    THROW 50022, N'Faltan dbo.personas, dbo.persona_empresa o dbo.bitacora_cambios. Abortar.', 1;

-- Personas vivas, activas y sin ningun vinculo activo. La empresa de la fila de bitacora es la de su vinculo
-- mas reciente (todas tienen al menos uno: 0 personas sin vinculo al 2026-10-10).
INSERT INTO #plan_022 (id_persona, id_empresa)
SELECT p.id_persona,
       (SELECT TOP (1) v.id_empresa
        FROM dbo.persona_empresa v
        WHERE v.id_persona = p.id_persona AND v.eliminado = 0
        ORDER BY v.id_persona_empresa DESC)
FROM dbo.personas p
WHERE p.eliminado = 0
  AND p.id_persona_principal IS NULL
  AND p.activo = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.persona_empresa v
                  WHERE v.id_persona = p.id_persona AND v.activo = 1 AND v.eliminado = 0);

DECLARE @n INT = (SELECT COUNT(*) FROM #plan_022);
IF @n > 20
    THROW 50022, N'Hay mas de 20 personas por corregir: revisarlas a mano antes de seguir. Abortar.', 1;

INSERT INTO #precheck_022 (ok, por_corregir) VALUES (1, @n);

SELECT pl.id_persona, p.nombres, p.apellidos, pl.id_empresa FROM #plan_022 pl JOIN dbo.personas p ON p.id_persona = pl.id_persona ORDER BY pl.id_persona;
PRINT CONCAT(N'PRECHECK 022 correcto. Personas por corregir: ', @n, N'.');
GO

-- ------------------------------------------------------------
-- CAMBIO + POSTCHECK (una transaccion)
-- ------------------------------------------------------------
IF OBJECT_ID(N'tempdb..#precheck_022') IS NULL OR NOT EXISTS (SELECT 1 FROM #precheck_022 WHERE ok = 1)
    THROW 50022, N'El PRECHECK no se ejecuto o no paso en esta sesion. No se cambia nada.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @tx UNIQUEIDENTIFIER = NEWID();
    DECLARE @esperadas INT = (SELECT por_corregir FROM #precheck_022);
    DECLARE @actualizadas INT = 0, @filas_bitacora INT = 0;

    IF @esperadas > 0
    BEGIN
        UPDATE p
        SET p.activo = 0,
            p.modificado_por = N'script_022',
            p.fecha_modificacion = SYSUTCDATETIME()
        FROM dbo.personas p
        JOIN #plan_022 pl ON pl.id_persona = p.id_persona
        WHERE p.activo = 1;
        SET @actualizadas = @@ROWCOUNT;

        INSERT INTO dbo.bitacora_cambios (id_transaccion, id_empresa, id_persona, entidad, id_registro, operacion, campo, valor_anterior, valor_nuevo, usuario, origen)
        SELECT @tx, pl.id_empresa, pl.id_persona, 'personas', pl.id_persona, 'UPDATE', 'activo', 'true', 'false', N'script_022', 'script:022'
        FROM #plan_022 pl;
        SET @filas_bitacora = @@ROWCOUNT;
    END;

    -- POSTCHECK
    IF @actualizadas <> @esperadas OR @filas_bitacora <> @esperadas
        THROW 50022, N'POSTCHECK 022: las filas cambiadas no coinciden con el plan. Se revierte todo.', 1;

    IF EXISTS (SELECT 1 FROM dbo.personas p
               WHERE p.eliminado = 0 AND p.id_persona_principal IS NULL AND p.activo = 1
                 AND NOT EXISTS (SELECT 1 FROM dbo.persona_empresa v
                                 WHERE v.id_persona = p.id_persona AND v.activo = 1 AND v.eliminado = 0))
        THROW 50022, N'POSTCHECK 022: todavia hay personas activas sin vinculo activo. Se revierte todo.', 1;

    COMMIT TRANSACTION;

    IF @esperadas = 0
        PRINT N'No habia nada que corregir. POSTCHECK correcto.';
    ELSE
        PRINT CONCAT(N'Listo: ', @actualizadas, N' persona(s) corregidas y ', @filas_bitacora, N' fila(s) de bitacora. POSTCHECK correcto.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'tempdb..#precheck_022') IS NOT NULL DROP TABLE #precheck_022;
IF OBJECT_ID(N'tempdb..#plan_022') IS NOT NULL DROP TABLE #plan_022;
GO

-- ------------------------------------------------------------
-- ROLLBACK (reversa; NO ejecutar sin aprobacion /alerta-bd)
--   Vuelve a encender solo las personas que apago este script y deja su propia fila en la bitacora.
-- ------------------------------------------------------------
/*
DECLARE @tx UNIQUEIDENTIFIER = NEWID();
BEGIN TRANSACTION;
UPDATE p SET p.activo = 1, p.modificado_por = N'script_022_reversa', p.fecha_modificacion = SYSUTCDATETIME()
FROM dbo.personas p
WHERE p.id_persona IN (SELECT b.id_persona FROM dbo.bitacora_cambios b WHERE b.origen = 'script:022' AND b.campo = 'activo');
INSERT INTO dbo.bitacora_cambios (id_transaccion, id_empresa, id_persona, entidad, id_registro, operacion, campo, valor_anterior, valor_nuevo, usuario, origen)
SELECT @tx, b.id_empresa, b.id_persona, 'personas', b.id_persona, 'UPDATE', 'activo', 'false', 'true', N'script_022_reversa', 'script:022-reversa'
FROM dbo.bitacora_cambios b WHERE b.origen = 'script:022' AND b.campo = 'activo';
COMMIT TRANSACTION;
*/
