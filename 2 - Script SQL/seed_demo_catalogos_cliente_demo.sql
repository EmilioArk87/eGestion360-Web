-- ============================================================================
-- seed_demo_catalogos_cliente_demo.sql
-- Da al usuario cliente_demo (empresa Demo, rol "Flota") acceso al modulo Catalogos
-- (hoy solo contiene la pantalla de Clientes): activa el modulo para la empresa Demo y
-- concede ver, crear y editar al rol. NO concede eliminar: el rol "Flota" tampoco lo
-- tiene sobre flota.
--
-- Contexto : el 2026-10-02 la prueba de escritura en Demo activo Catalogos de forma
--            temporal (INSERT) y lo apago al final (UPDATE a 0). Las filas existen, asi
--            que este script solo las vuelve a encender; no inserta nada si ya estan.
-- Alcance  : empresa 2 (Demo) y el rol "Flota" de esa empresa, que solo usa cliente_demo.
--            No toca otras empresas ni otros modulos. Modulos y permisos se leen al
--            iniciar sesion: el usuario debe volver a entrar para verlo.
-- Estado   : escrito, NO ejecutado. Requiere /alerta-bd y aprobacion de Emilio.
-- Reversa  : ver el bloque ROLLBACK al final (deja el modulo apagado y el rol en 0).
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() <> N'eBD_SPD' THROW 50200, N'Solo para eBD_SPD.', 1;
GO

-- ----------------------------------------------------------------------------
-- PRECHECK + CAMBIO + POSTCHECK (una sola transaccion)
-- ----------------------------------------------------------------------------
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @id_empresa INT = 2;
    DECLARE @id_modulo  INT = (SELECT id_modulo FROM dbo.modulos WHERE codigo = N'catalogos');
    DECLARE @id_rol     INT = (SELECT id_rol FROM dbo.empresa_roles WHERE id_empresa = @id_empresa AND nombre = N'Flota');

    -- PRECHECK
    IF @id_modulo IS NULL THROW 50201, N'No existe el modulo catalogos.', 1;
    IF @id_rol IS NULL    THROW 50202, N'No existe el rol Flota en la empresa Demo.', 1;
    IF (SELECT COUNT(*) FROM dbo.Users WHERE EmpresaRolId = @id_rol) <> 1
        THROW 50203, N'El rol Flota lo usa mas de un usuario: se detiene para no dar acceso a otros.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = N'cliente_demo' AND EmpresaId = @id_empresa AND EmpresaRolId = @id_rol)
        THROW 50204, N'cliente_demo no es el usuario del rol Flota de Demo.', 1;

    -- CAMBIO 1: el modulo activo para la empresa Demo
    IF EXISTS (SELECT 1 FROM dbo.empresa_modulos WHERE id_empresa = @id_empresa AND id_modulo = @id_modulo)
        UPDATE dbo.empresa_modulos SET activo = 1 WHERE id_empresa = @id_empresa AND id_modulo = @id_modulo;
    ELSE
        INSERT INTO dbo.empresa_modulos (id_empresa, id_modulo, fecha_activacion, activo)
        VALUES (@id_empresa, @id_modulo, SYSUTCDATETIME(), 1);

    -- CAMBIO 2: permisos del rol Flota sobre Catalogos (ver, crear, editar; sin eliminar)
    IF EXISTS (SELECT 1 FROM dbo.empresa_rol_permisos WHERE id_rol = @id_rol AND id_modulo = @id_modulo)
        UPDATE dbo.empresa_rol_permisos
        SET puede_ver = 1, puede_crear = 1, puede_editar = 1, puede_eliminar = 0
        WHERE id_rol = @id_rol AND id_modulo = @id_modulo;
    ELSE
        INSERT INTO dbo.empresa_rol_permisos (id_rol, id_modulo, puede_ver, puede_crear, puede_editar, puede_eliminar)
        VALUES (@id_rol, @id_modulo, 1, 1, 1, 0);

    -- POSTCHECK
    IF (SELECT COUNT(*) FROM dbo.empresa_modulos WHERE id_empresa = @id_empresa AND id_modulo = @id_modulo AND activo = 1) <> 1
        THROW 50205, N'No quedo activo Catalogos en Demo. Se revierte.', 1;
    IF (SELECT COUNT(*) FROM dbo.empresa_rol_permisos
        WHERE id_rol = @id_rol AND id_modulo = @id_modulo AND puede_ver = 1 AND puede_crear = 1 AND puede_editar = 1 AND puede_eliminar = 0) <> 1
        THROW 50206, N'No quedaron los permisos esperados del rol Flota. Se revierte.', 1;
    IF EXISTS (SELECT 1 FROM dbo.empresa_modulos WHERE id_modulo = @id_modulo AND id_empresa <> @id_empresa AND activo = 1)
        THROW 50207, N'Catalogos quedo activo en otra empresa. Se revierte.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Resultado
SELECT N'modulos de Demo' AS dato, m.codigo AS valor, em.activo AS activo
FROM dbo.empresa_modulos em JOIN dbo.modulos m ON m.id_modulo = em.id_modulo WHERE em.id_empresa = 2
UNION ALL
SELECT N'permisos del rol Flota (ver/crear/editar/eliminar)', m.codigo,
       CAST(p.puede_ver AS INT) * 1000 + CAST(p.puede_crear AS INT) * 100 + CAST(p.puede_editar AS INT) * 10 + CAST(p.puede_eliminar AS INT)
FROM dbo.empresa_rol_permisos p JOIN dbo.modulos m ON m.id_modulo = p.id_modulo
WHERE p.id_rol = (SELECT id_rol FROM dbo.empresa_roles WHERE id_empresa = 2 AND nombre = N'Flota');
GO

-- ----------------------------------------------------------------------------
-- ROLLBACK (comentado; ejecutar solo si hace falta deshacer)
-- ----------------------------------------------------------------------------
/*
UPDATE dbo.empresa_modulos SET activo = 0
 WHERE id_empresa = 2 AND id_modulo = (SELECT id_modulo FROM dbo.modulos WHERE codigo = N'catalogos');
UPDATE dbo.empresa_rol_permisos SET puede_ver = 0, puede_crear = 0, puede_editar = 0, puede_eliminar = 0
 WHERE id_rol = (SELECT id_rol FROM dbo.empresa_roles WHERE id_empresa = 2 AND nombre = N'Flota')
   AND id_modulo = (SELECT id_modulo FROM dbo.modulos WHERE codigo = N'catalogos');
*/
