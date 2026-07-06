-- =============================================================================
-- Transgar - 06: Dejar transgar.admin como usuario OPERATIVO (ve/usa los modulos)
-- Motivo: MainMenu muestra a todo empresa_admin solo el stub "Pago de Servicio".
-- Se pasa a empresa_user con el rol de empresa "Administrador" (permisos completos).
-- La administracion (crear usuarios, empresas) queda con el superadmin del sistema.
-- Idempotente. (Texto ASCII a proposito.)
-- =============================================================================
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET NOCOUNT ON;
DECLARE @IdEmpresa INT = (SELECT id_empresa FROM dbo.empresas WHERE codigo='TRANSGAR');
IF @IdEmpresa IS NULL BEGIN RAISERROR('Empresa TRANSGAR no existe. Ejecute 00 primero.',16,1); RETURN; END
DECLARE @RolAdmin INT = (SELECT id_rol FROM dbo.empresa_roles WHERE id_empresa=@IdEmpresa AND nombre='Administrador');
IF @RolAdmin IS NULL BEGIN RAISERROR('No existe el rol Administrador de la empresa. Ejecute 00 primero.',16,1); RETURN; END

-- 1) Alta de permisos completos del rol Administrador sobre cada modulo activo (los que falten)
INSERT INTO dbo.empresa_rol_permisos (id_rol, id_modulo, puede_ver, puede_crear, puede_editar, puede_eliminar)
SELECT @RolAdmin, em.id_modulo, 1, 1, 1, 1
FROM dbo.empresa_modulos em
WHERE em.id_empresa=@IdEmpresa AND em.activo=1
  AND NOT EXISTS (SELECT 1 FROM dbo.empresa_rol_permisos p WHERE p.id_rol=@RolAdmin AND p.id_modulo=em.id_modulo);

-- 2) Asegurar todos los flags en 1 para el rol Administrador en los modulos activos
UPDATE p SET puede_ver=1, puede_crear=1, puede_editar=1, puede_eliminar=1
FROM dbo.empresa_rol_permisos p
JOIN dbo.empresa_modulos em ON em.id_modulo=p.id_modulo AND em.id_empresa=@IdEmpresa AND em.activo=1
WHERE p.id_rol=@RolAdmin;

-- 3) Convertir transgar.admin en usuario operativo con el rol Administrador
UPDATE dbo.Users SET Role='empresa_user', EmpresaRolId=@RolAdmin
WHERE Username='transgar.admin' AND EmpresaId=@IdEmpresa;

PRINT 'transgar.admin -> empresa_user con rol Administrador (rol_id=' + CAST(@RolAdmin AS varchar(10)) + ').';
PRINT '=== 06 completado ===';
