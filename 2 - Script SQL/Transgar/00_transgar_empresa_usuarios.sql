-- =============================================================================
-- Transportes Garay (Transgar) - 00: Empresa, módulos, roles y usuarios
-- Emula la configuración de la empresa Demo (Compañía para demostración, id=2).
-- Idempotente: puede ejecutarse varias veces sin duplicar.
-- Aplicar con:  sqlcmd -S eBD_SPD.mssql.somee.com -U acc_datos -P '***' -d eBD_SPD -I -i "00_transgar_empresa_usuarios.sql"
-- =============================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

DECLARE @Codigo        VARCHAR(20)   = 'TRANSGAR';
DECLARE @RazonSocial   NVARCHAR(200) = 'Transportes Garay';
DECLARE @NombreComerc  NVARCHAR(150) = 'Transgar';
DECLARE @DemoEmpresaId INT           = 2;      -- Compañía para demostración
DECLARE @Ahora         DATETIME2(3)  = SYSUTCDATETIME();

-- ---------------------------------------------------------------------------
-- 1. EMPRESA
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.empresas WHERE codigo = @Codigo)
BEGIN
    INSERT INTO dbo.empresas
        (codigo, razon_social, nombre_comercial, identificador_fiscal,
         pais_iso, moneda_iso, zona_horaria, activa, fecha_activacion,
         eliminado, creado_por, fecha_creacion)
    VALUES
        (@Codigo, @RazonSocial, @NombreComerc, NULL,
         'HN', 'HNL', 'America/Tegucigalpa', 1, @Ahora,
         0, 'seed-transgar', @Ahora);
    PRINT 'Empresa Transgar creada.';
END
ELSE
    PRINT 'Empresa Transgar ya existía (se reutiliza).';

DECLARE @IdEmpresa INT = (SELECT id_empresa FROM dbo.empresas WHERE codigo = @Codigo);
PRINT 'IdEmpresa = ' + CAST(@IdEmpresa AS NVARCHAR(10));

-- ---------------------------------------------------------------------------
-- 2. MÓDULOS  (copia los de la empresa Demo; si no hay, activa flota+catálogos)
-- ---------------------------------------------------------------------------
INSERT INTO dbo.empresa_modulos (id_empresa, id_modulo, fecha_activacion, activo)
SELECT @IdEmpresa, em.id_modulo, @Ahora, 1
FROM dbo.empresa_modulos em
WHERE em.id_empresa = @DemoEmpresaId
  AND em.activo = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.empresa_modulos x
                  WHERE x.id_empresa = @IdEmpresa AND x.id_modulo = em.id_modulo);

-- Respaldo: si la Demo no tenía módulos, activar al menos flota y catálogos
IF NOT EXISTS (SELECT 1 FROM dbo.empresa_modulos WHERE id_empresa = @IdEmpresa)
BEGIN
    INSERT INTO dbo.empresa_modulos (id_empresa, id_modulo, fecha_activacion, activo)
    SELECT @IdEmpresa, m.id_modulo, @Ahora, 1
    FROM dbo.modulos m
    WHERE m.codigo IN ('flota', 'catalogos')
      AND NOT EXISTS (SELECT 1 FROM dbo.empresa_modulos x
                      WHERE x.id_empresa = @IdEmpresa AND x.id_modulo = m.id_modulo);
END
DECLARE @NModulos INT = (SELECT COUNT(*) FROM dbo.empresa_modulos WHERE id_empresa = @IdEmpresa AND activo = 1);
PRINT 'Módulos activos para Transgar: ' + CAST(@NModulos AS NVARCHAR(10));

-- ---------------------------------------------------------------------------
-- 3. ROLES DE EMPRESA
-- ---------------------------------------------------------------------------
--   Administrador     -> es_admin = 1 (acceso total a los módulos de la empresa)
--   Operador de Flota -> ver/crear/editar en flota
--   Consulta          -> solo ver
IF NOT EXISTS (SELECT 1 FROM dbo.empresa_roles WHERE id_empresa = @IdEmpresa AND nombre = 'Administrador')
    INSERT INTO dbo.empresa_roles (id_empresa, nombre, descripcion, es_admin, activo)
    VALUES (@IdEmpresa, 'Administrador', 'Administrador de la empresa', 1, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.empresa_roles WHERE id_empresa = @IdEmpresa AND nombre = 'Operador de Flota')
    INSERT INTO dbo.empresa_roles (id_empresa, nombre, descripcion, es_admin, activo)
    VALUES (@IdEmpresa, 'Operador de Flota', 'Registra operación y gastos de flota', 0, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.empresa_roles WHERE id_empresa = @IdEmpresa AND nombre = 'Consulta')
    INSERT INTO dbo.empresa_roles (id_empresa, nombre, descripcion, es_admin, activo)
    VALUES (@IdEmpresa, 'Consulta', 'Solo lectura', 0, 1);

DECLARE @RolOperador INT = (SELECT id_rol FROM dbo.empresa_roles WHERE id_empresa = @IdEmpresa AND nombre = 'Operador de Flota');
DECLARE @RolConsulta INT = (SELECT id_rol FROM dbo.empresa_roles WHERE id_empresa = @IdEmpresa AND nombre = 'Consulta');

-- Permisos: Operador de Flota = ver/crear/editar en cada módulo activo de la empresa
INSERT INTO dbo.empresa_rol_permisos (id_rol, id_modulo, puede_ver, puede_crear, puede_editar, puede_eliminar)
SELECT @RolOperador, em.id_modulo, 1, 1, 1, 0
FROM dbo.empresa_modulos em
WHERE em.id_empresa = @IdEmpresa AND em.activo = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.empresa_rol_permisos p
                  WHERE p.id_rol = @RolOperador AND p.id_modulo = em.id_modulo);

-- Permisos: Consulta = solo ver
INSERT INTO dbo.empresa_rol_permisos (id_rol, id_modulo, puede_ver, puede_crear, puede_editar, puede_eliminar)
SELECT @RolConsulta, em.id_modulo, 1, 0, 0, 0
FROM dbo.empresa_modulos em
WHERE em.id_empresa = @IdEmpresa AND em.activo = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.empresa_rol_permisos p
                  WHERE p.id_rol = @RolConsulta AND p.id_modulo = em.id_modulo);

PRINT 'Roles de empresa configurados.';

-- ---------------------------------------------------------------------------
-- 4. USUARIOS  (2 cuentas)
--    Contraseña en texto plano: el sistema la migra a BCrypt en el primer login.
--    Credenciales iniciales -> cámbielas después de ingresar.
-- ---------------------------------------------------------------------------
--   transgar.admin / Transgar#2026  -> empresa_admin (acceso total a sus módulos)
--   transgar.flota / Transgar#2026  -> empresa_user  (rol Operador de Flota)
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = 'transgar.admin' OR Email = 'admin@transgar.hn')
    INSERT INTO dbo.Users (Username, Email, Password, CreatedAt, IsActive, RequirePasswordChange, Role, EmpresaId, EmpresaRolId)
    VALUES ('transgar.admin', 'admin@transgar.hn', 'Transgar#2026', @Ahora, 1, 0, 'empresa_admin', @IdEmpresa, NULL);

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = 'transgar.flota' OR Email = 'flota@transgar.hn')
    INSERT INTO dbo.Users (Username, Email, Password, CreatedAt, IsActive, RequirePasswordChange, Role, EmpresaId, EmpresaRolId)
    VALUES ('transgar.flota', 'flota@transgar.hn', 'Transgar#2026', @Ahora, 1, 0, 'empresa_user', @IdEmpresa, @RolOperador);

PRINT 'Usuarios creados/verificados: transgar.admin, transgar.flota';
PRINT '=== 00 completado ===';
