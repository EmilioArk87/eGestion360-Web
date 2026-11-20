-- ============================================================
-- Script   : alta_operador_admin_capae.sql
-- Proposito: Dar a la cuenta de Emilio (cuenta de operador de la plataforma; entra como admin, egaray o egaray.transgar) acceso de
--            ADMINISTRADOR DE EMPRESA en el tenant CAPAE (Centro de Atencion Psicologica y Avance Educativo), igual que los dos
--            administradores de CAPAE: empresa_admin sin rol, que recibe todo lo que la empresa tiene activado.
--            No hay pantalla para sumar una cuenta que ya existe a otro tenant: Admin/Usuarios/Edit lo intento el 2026-11-18 (21:46 UTC,
--            Empresa = CAPAE) y fallo, porque la membresia exige una persona DEL MISMO tenant (FKT_membresias_personas_id_persona) y la
--            persona del usuario egaray (102) es de SIP. Ese intento dejo dbo.Users a medias: los usuarios admin (Id 1) y egaray (Id 4)
--            quedaron apuntando a la empresa de CAPAE sin que la cuenta cambiara.
--            Hace, en una sola transaccion:
--              1. dbo.personas, dbo.persona_documentos  la persona de Emilio EN CAPAE: copia de su persona de SIP (nombres, apellidos,
--                                                       fecha de nacimiento, estado de identidad y su documento principal). Nada mas
--                                                       (sin telefonos, direccion, contactos, licencia ni datos de empleo).
--              2. dbo.persona_empresa                   su vinculo 'usuario' con la empresa de CAPAE (como los otros dos admin).
--              3. saas.membresias                       la membresia de la cuenta en CAPAE, activa, con esa persona.
--              4. dbo.accesos_empresa                   su acceso a la empresa de CAPAE: empresa_admin, sin rol, principal, activo.
--              5. dbo.Users                             devuelve los usuarios admin y egaray a como estaban antes del intento fallido:
--                                                       admin sin empresa (como lo dejo el 039) y egaray con la empresa, el rol y el
--                                                       rol de texto de su acceso en SIP. Asi Users vuelve a reflejar la cuenta y una
--                                                       edicion futura de esos usuarios no vuelve a fallar ni toca el acceso a CAPAE.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-11-19
-- BD       : eBD_SPD
-- Ejecucion: DESPUES de la aprobacion de /alerta-bd. Trae el interruptor @confirmar en 0: asi hace TODO dentro de una transaccion, la
--            comprueba y la REVIERTE (ensayo). Para aplicar de verdad se cambia @confirmar a 1. Es repetible: si ya esta hecho, no hace
--            nada y avisa; si los datos no son los esperados (otra cuenta, cuenta no operador, CAPAE inactivo o con otra empresa, una
--            membresia a medias, usuarios con valores distintos de los del intento fallido), se detiene con un error y no toca nada.
-- Reversa  : desactivar el acceso (dbo.accesos_empresa.activo = 0) o suspender la membresia (estado = 'suspendida'); la persona queda
--            como registro de CAPAE. Los usuarios 1 y 4 de dbo.Users no se vuelven a cambiar.
-- ============================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- >>> 0 = ENSAYO (hace, comprueba y revierte); 1 = APLICAR <<<
DECLARE @confirmar BIT = 0;

DECLARE @alias          NVARCHAR(256) = N'egaray';
DECLARE @tenant_origen  NVARCHAR(50)  = N'SIP';     -- de su membresia en este tenant sale la persona que se copia
DECLARE @tenant_destino NVARCHAR(50)  = N'CAPAE';
DECLARE @quien          NVARCHAR(100) = N'script:alta_operador_capae';
DECLARE @hoy            DATE          = CAST(SWITCHOFFSET(SYSDATETIMEOFFSET(), '-06:00') AS DATE);   -- fecha de Honduras

-- 1) Cuenta, tenants y empresa -----------------------------------------------------------------------------------------------
IF (SELECT COUNT(*) FROM saas.cuenta_alias WHERE alias = @alias AND tipo = N'usuario') <> 1
    THROW 50200, N'Se esperaba exactamente 1 alias de usuario con ese nombre. No se cambio nada.', 1;

DECLARE @id_cuenta      INT = (SELECT id_cuenta FROM saas.cuenta_alias WHERE alias = @alias AND tipo = N'usuario');
DECLARE @id_user_alias  INT = (SELECT id_user_legado FROM saas.cuenta_alias WHERE alias = @alias AND tipo = N'usuario');
DECLARE @id_user_cuenta INT = (SELECT id_user_legado FROM saas.cuentas WHERE id_cuenta = @id_cuenta);

IF NOT EXISTS (SELECT 1 FROM saas.cuentas WHERE id_cuenta = @id_cuenta AND estado = N'activa' AND es_operador = 1)
    THROW 50201, N'La cuenta del alias no esta activa o no es la cuenta de operador de la plataforma. No se cambio nada.', 1;

DECLARE @t_origen  INT = (SELECT id_tenant FROM saas.tenants WHERE codigo = @tenant_origen);
DECLARE @t_destino INT = (SELECT id_tenant FROM saas.tenants WHERE codigo = @tenant_destino AND estado = N'activo');
IF @t_origen IS NULL OR @t_destino IS NULL OR @t_origen = @t_destino
    THROW 50202, N'No se encontro el tenant de origen o el tenant destino activo. No se cambio nada.', 1;

IF (SELECT COUNT(*) FROM dbo.empresas WHERE id_tenant = @t_destino AND activa = 1 AND eliminado = 0) <> 1
    THROW 50203, N'El tenant destino no tiene exactamente 1 empresa activa. No se cambio nada.', 1;
DECLARE @id_empresa INT = (SELECT id_empresa FROM dbo.empresas WHERE id_tenant = @t_destino AND activa = 1 AND eliminado = 0);

DECLARE @m_origen INT = (SELECT id_membresia FROM saas.membresias
                          WHERE id_cuenta = @id_cuenta AND id_tenant = @t_origen AND estado = N'activa' AND id_persona IS NOT NULL);
DECLARE @id_persona_origen INT = (SELECT id_persona FROM saas.membresias WHERE id_membresia = @m_origen);
IF @m_origen IS NULL
    THROW 50204, N'La cuenta no tiene una membresia activa con persona en el tenant de origen. No se cambio nada.', 1;

-- 2) Lo que se copia del tenant de origen (persona, documento principal y acceso de egaray) -----------------------------------
EXEC sp_set_session_context @key = N'id_tenant', @value = @t_origen, @read_only = 0;
EXEC sp_set_session_context @key = N'ambito',    @value = N'tenant',  @read_only = 0;

DECLARE @persona TABLE (
    nombres NVARCHAR(200), apellidos NVARCHAR(200), primer_nombre NVARCHAR(100), segundo_nombre NVARCHAR(100),
    primer_apellido NVARCHAR(100), segundo_apellido NVARCHAR(100), nombre_normalizado NVARCHAR(400), sexo CHAR(1),
    fecha_nacimiento DATE, pais_nacionalidad CHAR(2), estado_identidad VARCHAR(15));
INSERT INTO @persona
SELECT nombres, apellidos, primer_nombre, segundo_nombre, primer_apellido, segundo_apellido, nombre_normalizado, sexo,
       fecha_nacimiento, pais_nacionalidad, estado_identidad
  FROM dbo.personas
 WHERE id_persona = @id_persona_origen AND id_tenant = @t_origen AND eliminado = 0 AND activo = 1 AND id_persona_principal IS NULL;
IF @@ROWCOUNT <> 1
    THROW 50205, N'La persona de la membresia de origen no esta vigente. No se cambio nada.', 1;

DECLARE @documento TABLE (tipo_documento VARCHAR(20), pais_emisor CHAR(2), numero NVARCHAR(60), numero_normalizado NVARCHAR(60), fecha_vencimiento DATE);
INSERT INTO @documento
SELECT tipo_documento, pais_emisor, numero, numero_normalizado, fecha_vencimiento
  FROM dbo.persona_documentos
 WHERE id_persona = @id_persona_origen AND id_tenant = @t_origen AND eliminado = 0 AND activo = 1 AND es_principal = 1;
IF @@ROWCOUNT <> 1
    THROW 50206, N'La persona de origen no tiene exactamente 1 documento principal vigente. No se cambio nada.', 1;

-- El acceso principal de la cuenta en el tenant de origen: es lo que el usuario egaray reflejaba antes del intento fallido.
DECLARE @u_empresa INT, @u_rol INT, @u_rol_legado NVARCHAR(20);
SELECT @u_empresa = id_empresa, @u_rol = id_rol, @u_rol_legado = rol_legado
  FROM dbo.accesos_empresa
 WHERE id_membresia = @m_origen AND id_tenant = @t_origen AND activo = 1 AND es_principal = 1;
IF @u_empresa IS NULL
    THROW 50207, N'La membresia de origen no tiene un acceso principal activo. No se cambio nada.', 1;

-- 3) Estado en el destino: hecho, a medias o por hacer -------------------------------------------------------------------------
EXEC sp_set_session_context @key = N'id_tenant', @value = @t_destino, @read_only = 0;

DECLARE @m_destino INT = (SELECT id_membresia FROM saas.membresias WHERE id_cuenta = @id_cuenta AND id_tenant = @t_destino);
DECLARE @falta_acceso BIT = 1;
IF @m_destino IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM saas.membresias m
                 JOIN dbo.personas p ON p.id_persona = m.id_persona AND p.id_tenant = m.id_tenant
                 JOIN dbo.accesos_empresa a ON a.id_membresia = m.id_membresia AND a.id_tenant = m.id_tenant
                WHERE m.id_membresia = @m_destino AND m.estado = N'activa' AND p.eliminado = 0 AND p.id_persona_principal IS NULL
                  AND a.id_empresa = @id_empresa AND a.activo = 1 AND a.rol_legado = N'empresa_admin')
        SET @falta_acceso = 0;
    ELSE
        THROW 50208, N'La cuenta ya tiene una membresia en el tenant destino, pero no como administrador activo con persona: revisarla a mano. No se cambio nada.', 1;
END

IF @falta_acceso = 1
   AND EXISTS (SELECT 1 FROM dbo.persona_documentos d JOIN @documento x
                 ON d.tipo_documento = x.tipo_documento AND d.pais_emisor = x.pais_emisor AND d.numero_normalizado = x.numero_normalizado
               WHERE d.id_tenant = @t_destino AND d.eliminado = 0)
    THROW 50209, N'Ya hay una persona con ese documento en el tenant destino: vincularla a mano en vez de crear otra. No se cambio nada.', 1;

-- Usuarios de dbo.Users: o tienen los valores del intento fallido (se corrigen) o ya tienen los buenos (no se tocan).
DECLARE @falta_users_cuenta BIT = 0, @falta_users_alias BIT = 0;
IF EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @id_user_cuenta AND Role = N'admin' AND EmpresaId = @id_empresa AND EmpresaRolId IS NULL)
    SET @falta_users_cuenta = 1;
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @id_user_cuenta AND Role = N'admin' AND EmpresaId IS NULL AND EmpresaRolId IS NULL)
    THROW 50210, N'El usuario principal de la cuenta no tiene ni los valores del intento fallido ni los esperados. No se cambio nada.', 1;

IF EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @id_user_alias AND Role = N'empresa_admin' AND EmpresaId = @id_empresa AND EmpresaRolId IS NULL)
    SET @falta_users_alias = 1;
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @id_user_alias AND Role = @u_rol_legado AND EmpresaId = @u_empresa
                       AND ISNULL(EmpresaRolId, -1) = ISNULL(@u_rol, -1))
    THROW 50211, N'El usuario del alias no tiene ni los valores del intento fallido ni los de su acceso de origen. No se cambio nada.', 1;

IF @falta_acceso = 0 AND @falta_users_cuenta = 0 AND @falta_users_alias = 0
BEGIN
    PRINT N'Nada que hacer: la cuenta ya es administradora en el tenant destino y dbo.Users ya esta corregido.';
    EXEC sp_set_session_context @key = N'id_tenant', @value = NULL, @read_only = 0;
    EXEC sp_set_session_context @key = N'ambito',    @value = NULL, @read_only = 0;
    RETURN;
END

-- 4) Cambio (todo o nada) ------------------------------------------------------------------------------------------------------
DECLARE @id_persona INT, @id_acceso INT, @n_users_cuenta INT = 0, @n_users_alias INT = 0;

BEGIN TRANSACTION;

IF @falta_acceso = 1
BEGIN
    INSERT INTO dbo.personas (id_tenant, nombres, apellidos, primer_nombre, segundo_nombre, primer_apellido, segundo_apellido,
                              nombre_normalizado, sexo, fecha_nacimiento, pais_nacionalidad, estado_identidad, activo, eliminado, creado_por)
    SELECT @t_destino, nombres, apellidos, primer_nombre, segundo_nombre, primer_apellido, segundo_apellido,
           nombre_normalizado, sexo, fecha_nacimiento, pais_nacionalidad, estado_identidad, 1, 0, @quien
      FROM @persona;
    SET @id_persona = SCOPE_IDENTITY();

    INSERT INTO dbo.persona_documentos (id_tenant, id_persona, tipo_documento, pais_emisor, numero, es_principal, fecha_vencimiento,
                                        activo, eliminado, creado_por)
    SELECT @t_destino, @id_persona, tipo_documento, pais_emisor, numero, 1, fecha_vencimiento, 1, 0, @quien
      FROM @documento;

    INSERT INTO dbo.persona_empresa (id_tenant, id_empresa, id_persona, tipo_vinculo, fecha_inicio, activo, eliminado, creado_por)
    VALUES (@t_destino, @id_empresa, @id_persona, 'usuario', @hoy, 1, 0, @quien);

    INSERT INTO saas.membresias (id_cuenta, id_tenant, id_persona, estado, fecha_estado, creado_por, fecha_creacion)
    VALUES (@id_cuenta, @t_destino, @id_persona, N'activa', SYSUTCDATETIME(), @quien, SYSUTCDATETIME());
    SET @m_destino = SCOPE_IDENTITY();

    INSERT INTO dbo.accesos_empresa (id_tenant, id_membresia, id_empresa, id_rol, rol_legado, es_principal, activo, creado_por, fecha_creacion)
    VALUES (@t_destino, @m_destino, @id_empresa, NULL, N'empresa_admin', 1, 1, @quien, SYSUTCDATETIME());
    SET @id_acceso = SCOPE_IDENTITY();
END

IF @falta_users_cuenta = 1
BEGIN
    UPDATE dbo.Users SET EmpresaId = NULL
     WHERE Id = @id_user_cuenta AND Role = N'admin' AND EmpresaId = @id_empresa AND EmpresaRolId IS NULL;
    SET @n_users_cuenta = @@ROWCOUNT;
    IF @n_users_cuenta <> 1 THROW 50212, N'La correccion del usuario principal no afecto exactamente 1 fila. Se revierte todo.', 1;
END

IF @falta_users_alias = 1
BEGIN
    UPDATE dbo.Users SET EmpresaId = @u_empresa, EmpresaRolId = @u_rol, Role = @u_rol_legado
     WHERE Id = @id_user_alias AND Role = N'empresa_admin' AND EmpresaId = @id_empresa AND EmpresaRolId IS NULL;
    SET @n_users_alias = @@ROWCOUNT;
    IF @n_users_alias <> 1 THROW 50213, N'La correccion del usuario del alias no afecto exactamente 1 fila. Se revierte todo.', 1;
END

-- POSTCHECK dentro de la transaccion: lo mismo que mira el inicio de sesion (DirectorioDeCuentas) para ofrecer el sitio.
DECLARE @sitios INT = (
    SELECT COUNT(*)
      FROM saas.membresias m
      JOIN saas.tenants t ON t.id_tenant = m.id_tenant
      JOIN dbo.personas p ON p.id_persona = m.id_persona AND p.id_tenant = m.id_tenant
      JOIN dbo.accesos_empresa a ON a.id_membresia = m.id_membresia AND a.id_tenant = m.id_tenant
      JOIN dbo.empresas e ON e.id_empresa = a.id_empresa AND e.id_tenant = a.id_tenant
     WHERE m.id_cuenta = @id_cuenta AND m.id_tenant = @t_destino AND m.estado = N'activa' AND t.estado <> N'cancelado'
       AND p.eliminado = 0 AND p.id_persona_principal IS NULL
       AND a.activo = 1 AND a.rol_legado = N'empresa_admin' AND a.id_rol IS NULL
       AND e.id_empresa = @id_empresa AND e.activa = 1 AND e.eliminado = 0);
IF @sitios <> 1
    THROW 50214, N'POSTCHECK: la cuenta no quedo con exactamente 1 sitio de administrador en el tenant destino. Se revierte todo.', 1;

IF EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @id_user_cuenta AND EmpresaId IS NOT NULL)
   OR NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @id_user_alias AND EmpresaId = @u_empresa AND Role = @u_rol_legado
                     AND ISNULL(EmpresaRolId, -1) = ISNULL(@u_rol, -1))
    THROW 50215, N'POSTCHECK: dbo.Users no quedo como se esperaba. Se revierte todo.', 1;

PRINT CONCAT(N'Cuenta ', @id_cuenta, N' -> tenant ', @t_destino, N', empresa ', @id_empresa,
             CASE WHEN @falta_acceso = 1 THEN CONCAT(N' | persona nueva ', @id_persona, N', membresia ', @m_destino, N', acceso ', @id_acceso)
                  ELSE N' | acceso ya existia' END,
             N' | dbo.Users corregidos: principal ', @n_users_cuenta, N', alias ', @n_users_alias, N' | POSTCHECK correcto');

IF @confirmar = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT N'APLICADO. La cuenta vera el sitio nuevo al volver a iniciar sesion.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT N'ENSAYO: se revirtio todo, no se cambio nada. Para aplicar cambie @confirmar a 1 al inicio y vuelva a correr el script.';
END

-- 5) Comprobacion final (con @confirmar = 1: 1 membresia, 1 acceso y Users corregidos; en ensayo: lo que habia antes) ------------
SELECT (SELECT COUNT(*) FROM saas.membresias WHERE id_cuenta = @id_cuenta AND id_tenant = @t_destino AND estado = N'activa') AS membresias_destino,
       (SELECT COUNT(*) FROM dbo.accesos_empresa a JOIN saas.membresias m ON m.id_membresia = a.id_membresia
         WHERE m.id_cuenta = @id_cuenta AND a.id_tenant = @t_destino AND a.activo = 1 AND a.rol_legado = N'empresa_admin') AS accesos_admin_destino,
       (SELECT EmpresaId FROM dbo.Users WHERE Id = @id_user_cuenta) AS empresa_user_principal,
       (SELECT EmpresaId FROM dbo.Users WHERE Id = @id_user_alias)  AS empresa_user_alias,
       (SELECT Role FROM dbo.Users WHERE Id = @id_user_alias)       AS rol_user_alias;

EXEC sp_set_session_context @key = N'id_tenant', @value = NULL, @read_only = 0;
EXEC sp_set_session_context @key = N'ambito',    @value = NULL, @read_only = 0;
