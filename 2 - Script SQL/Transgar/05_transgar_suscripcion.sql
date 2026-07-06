-- =============================================================================
-- Transgar - 05: Suscripcion SOLO del modulo FLOTA para la empresa TRANSGAR
-- Deja unicamente flota activo (quita cualquier otro modulo de la suscripcion).
-- Idempotente. Requiere 00. (Texto ASCII a proposito para evitar problemas de codificacion.)
-- =============================================================================
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET NOCOUNT ON;
DECLARE @IdEmpresa INT = (SELECT id_empresa FROM dbo.empresas WHERE codigo='TRANSGAR');
IF @IdEmpresa IS NULL BEGIN RAISERROR('Empresa TRANSGAR no existe. Ejecute 00 primero.',16,1); RETURN; END
DECLARE @Ahora DATETIME2(3)=SYSUTCDATETIME();
DECLARE @IdFlota INT = (SELECT id_modulo FROM dbo.modulos WHERE codigo='flota');
IF @IdFlota IS NULL BEGIN RAISERROR('No existe el modulo flota en el catalogo.',16,1); RETURN; END

-- 1) Asegurar la empresa activa (sin baja)
UPDATE dbo.empresas SET activa=1, fecha_baja=NULL
WHERE id_empresa=@IdEmpresa AND (activa=0 OR fecha_baja IS NOT NULL);

-- 2) Alta del modulo flota si faltara
IF NOT EXISTS (SELECT 1 FROM dbo.empresa_modulos WHERE id_empresa=@IdEmpresa AND id_modulo=@IdFlota)
    INSERT INTO dbo.empresa_modulos (id_empresa, id_modulo, fecha_activacion, fecha_vencimiento, activo)
    VALUES (@IdEmpresa, @IdFlota, @Ahora, NULL, 1);

-- 3) Asegurar flota activo y sin vencimiento
UPDATE dbo.empresa_modulos SET activo=1, fecha_vencimiento=NULL
WHERE id_empresa=@IdEmpresa AND id_modulo=@IdFlota;

-- 4) Quitar cualquier otro modulo: la suscripcion queda SOLO con flota
DELETE FROM dbo.empresa_modulos WHERE id_empresa=@IdEmpresa AND id_modulo<>@IdFlota;
DECLARE @quitados INT = @@ROWCOUNT;

DECLARE @total INT = (SELECT COUNT(*) FROM dbo.empresa_modulos WHERE id_empresa=@IdEmpresa AND activo=1);
PRINT 'Modulos removidos (no-flota): ' + CAST(@quitados AS varchar(5));
PRINT 'Total modulos activos: '        + CAST(@total    AS varchar(5));
PRINT '=== 05 completado (solo flota) ===';
