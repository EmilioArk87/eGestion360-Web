-- DROP SCHEMA dbo;

CREATE SCHEMA dbo;
-- eBD_SPD.dbo.EmailConfiguration definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.EmailConfiguration;

CREATE TABLE eBD_SPD.dbo.EmailConfiguration (
	Id int IDENTITY(1,1) NOT NULL,
	ProfileName nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	Provider nvarchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	FromEmail nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	FromName nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	SmtpHost nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	SmtpPort int DEFAULT 587 NOT NULL,
	UseSsl bit DEFAULT 1 NOT NULL,
	Username nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	PasswordHash nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	IsActive bit DEFAULT 1 NOT NULL,
	IsDefault bit DEFAULT 0 NOT NULL,
	CreatedAt datetime2 DEFAULT getutcdate() NOT NULL,
	UpdatedAt datetime2 DEFAULT getutcdate() NOT NULL,
	CreatedBy nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	LastTestedAt datetime2 NULL,
	TestEmailsSent int DEFAULT 0 NOT NULL,
	CONSTRAINT PK_EmailConfiguration PRIMARY KEY (Id),
	CONSTRAINT UK_EmailConfiguration_ProfileName UNIQUE (ProfileName)
);
 CREATE NONCLUSTERED INDEX IX_EmailConfiguration_Active_Default ON eBD_SPD.dbo.EmailConfiguration (  IsActive ASC  , IsDefault ASC  )  
	 INCLUDE ( FromEmail , ProfileName , Provider , SmtpHost , SmtpPort , UseSsl ) 
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_EmailConfiguration_Provider ON eBD_SPD.dbo.EmailConfiguration (  Provider ASC  , IsActive ASC  )  
	 INCLUDE ( FromEmail , ProfileName ) 
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.EmailConfiguration WITH NOCHECK ADD CONSTRAINT CK_EmailConfiguration_Provider CHECK (([Provider]='Office365' OR [Provider]='SendGrid' OR [Provider]='Outlook' OR [Provider]='Gmail' OR [Provider]='SMTP'));
ALTER TABLE eBD_SPD.dbo.EmailConfiguration WITH NOCHECK ADD CONSTRAINT CK_EmailConfiguration_Email CHECK (([FromEmail] like '%_@_%_.__%'));
ALTER TABLE eBD_SPD.dbo.EmailConfiguration WITH NOCHECK ADD CONSTRAINT CK_EmailConfiguration_SmtpPort CHECK (([SmtpPort]>=(25) AND [SmtpPort]<=(65535)));
ALTER TABLE eBD_SPD.dbo.EmailConfiguration WITH NOCHECK ADD CONSTRAINT CK_EmailConfiguration_OnlyOneDefault CHECK (([IsDefault]=(0) OR [IsActive]=(1)));


-- eBD_SPD.dbo.EmailConfigurations definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.EmailConfigurations;

CREATE TABLE eBD_SPD.dbo.EmailConfigurations (
	Id int IDENTITY(1,1) NOT NULL,
	ProfileName nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	Provider nvarchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	FromEmail nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	FromName nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	SmtpHost nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	SmtpPort int NOT NULL,
	UseSsl bit NOT NULL,
	Username nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	PasswordHash nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	IsActive bit NOT NULL,
	IsDefault bit NOT NULL,
	CreatedAt datetime2 NOT NULL,
	UpdatedAt datetime2 NOT NULL,
	CreatedBy nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	LastTestedAt datetime2 NULL,
	TestEmailsSent int NOT NULL,
	CONSTRAINT PK_EmailConfigurations PRIMARY KEY (Id)
);
 CREATE NONCLUSTERED INDEX IX_EmailConfigurations_IsActive_IsDefault ON eBD_SPD.dbo.EmailConfigurations (  IsActive ASC  , IsDefault ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX IX_EmailConfigurations_ProfileName ON eBD_SPD.dbo.EmailConfigurations (  ProfileName ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_EmailConfigurations_Provider ON eBD_SPD.dbo.EmailConfigurations (  Provider ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;


-- eBD_SPD.dbo.Users definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.Users;

CREATE TABLE eBD_SPD.dbo.Users (
	Id int IDENTITY(1,1) NOT NULL,
	Username nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	Email nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	Password nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	CreatedAt datetime2 NOT NULL,
	IsActive bit NOT NULL,
	RequirePasswordChange bit DEFAULT 0 NOT NULL,
	CONSTRAINT PK_Users PRIMARY KEY (Id)
);
 CREATE UNIQUE NONCLUSTERED INDEX IX_Users_Email ON eBD_SPD.dbo.Users (  Email ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX IX_Users_Username ON eBD_SPD.dbo.Users (  Username ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;


-- eBD_SPD.dbo.[__EFMigrationsHistory] definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.[__EFMigrationsHistory];

CREATE TABLE eBD_SPD.dbo.[__EFMigrationsHistory] (
	MigrationId nvarchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	ProductVersion nvarchar(32) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY (MigrationId)
);


-- eBD_SPD.dbo.catalogo_paises definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.catalogo_paises;

CREATE TABLE eBD_SPD.dbo.catalogo_paises (
	pais_iso char(2) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	pais_iso3 char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	pais_num char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	nombre_espanol nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	nombre_ingles nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	activo bit DEFAULT 1 NOT NULL,
	fecha_creacion datetime2(0) DEFAULT sysdatetime() NOT NULL,
	CONSTRAINT PK_catalogo_paises PRIMARY KEY (pais_iso)
);


-- eBD_SPD.dbo.usuarios definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.usuarios;

CREATE TABLE eBD_SPD.dbo.usuarios (
	id_usuario uniqueidentifier DEFAULT newsequentialid() NOT NULL,
	nombre_usuario nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	nombre_usuario_normalizado nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	correo nvarchar(254) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	correo_normalizado nvarchar(254) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	hash_contrasena varbinary(256) NOT NULL,
	sal_contrasena varbinary(128) NULL,
	algoritmo_contrasena nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'PBKDF2-SHA256' NOT NULL,
	requiere_cambio_contrasena bit DEFAULT 0 NOT NULL,
	fecha_ultimo_cambio_contrasena datetime2(0) NULL,
	mfa_habilitado bit DEFAULT 0 NOT NULL,
	mfa_tipo nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	secreto_mfa_cifrado varbinary(MAX) NULL,
	nombres nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	apellidos nvarchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	telefono nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	idioma_preferido nvarchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'es' NOT NULL,
	zona_horaria_preferida nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	activo bit DEFAULT 1 NOT NULL,
	intentos_fallidos_acceso int DEFAULT 0 NOT NULL,
	bloqueado_hasta datetime2(0) NULL,
	fecha_ultimo_acceso datetime2(0) NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(0) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT suser_sname() NOT NULL,
	fecha_creacion datetime2(0) DEFAULT sysdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(0) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_usuarios PRIMARY KEY (id_usuario)
);
 CREATE NONCLUSTERED INDEX IX_usuarios_acceso ON eBD_SPD.dbo.usuarios (  nombre_usuario_normalizado ASC  , activo ASC  )  
	 INCLUDE ( bloqueado_hasta , intentos_fallidos_acceso ) 
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_usuarios_activo ON eBD_SPD.dbo.usuarios (  activo ASC  )  
	 INCLUDE ( correo_normalizado ) 
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_usuarios_correo_norm ON eBD_SPD.dbo.usuarios (  correo_normalizado ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_usuarios_nombre_usuario_norm ON eBD_SPD.dbo.usuarios (  nombre_usuario_normalizado ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;


-- eBD_SPD.dbo.PasswordResetCodes definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.PasswordResetCodes;

CREATE TABLE eBD_SPD.dbo.PasswordResetCodes (
	Id int IDENTITY(1,1) NOT NULL,
	UserId int NOT NULL,
	Code nvarchar(6) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	Email nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	CreatedAt datetime2 DEFAULT getutcdate() NOT NULL,
	ExpiresAt datetime2 NOT NULL,
	IsUsed bit DEFAULT 0 NOT NULL,
	UsedAt datetime2 NULL,
	IpAddress nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	CONSTRAINT PK_PasswordResetCodes PRIMARY KEY (Id),
	CONSTRAINT FK_PasswordResetCodes_Users FOREIGN KEY (UserId) REFERENCES eBD_SPD.dbo.Users(Id) ON DELETE CASCADE
);
 CREATE NONCLUSTERED INDEX IX_PasswordResetCodes_Email_Code_IsUsed ON eBD_SPD.dbo.PasswordResetCodes (  Email ASC  , Code ASC  , IsUsed ASC  )  
	 INCLUDE ( CreatedAt , ExpiresAt , UserId ) 
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_PasswordResetCodes_ExpiresAt ON eBD_SPD.dbo.PasswordResetCodes (  ExpiresAt ASC  )  
	 WHERE  ([IsUsed]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_PasswordResetCodes_UserId_CreatedAt ON eBD_SPD.dbo.PasswordResetCodes (  UserId ASC  , CreatedAt DESC  )  
	 INCLUDE ( Code , ExpiresAt , IsUsed ) 
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.PasswordResetCodes WITH NOCHECK ADD CONSTRAINT CK_PasswordResetCodes_Code CHECK (([Code] like '[0-9][0-9][0-9][0-9][0-9][0-9]'));
ALTER TABLE eBD_SPD.dbo.PasswordResetCodes WITH NOCHECK ADD CONSTRAINT CK_PasswordResetCodes_Email CHECK (([Email] like '%_@_%_.__%'));
ALTER TABLE eBD_SPD.dbo.PasswordResetCodes WITH NOCHECK ADD CONSTRAINT CK_PasswordResetCodes_Expiry CHECK (([ExpiresAt]>[CreatedAt]));
ALTER TABLE eBD_SPD.dbo.PasswordResetCodes WITH NOCHECK ADD CONSTRAINT CK_PasswordResetCodes_UsedLogic CHECK (([IsUsed]=(0) AND [UsedAt] IS NULL OR [IsUsed]=(1) AND [UsedAt] IS NOT NULL));


-- eBD_SPD.dbo.empresas definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.empresas;

CREATE TABLE eBD_SPD.dbo.empresas (
	id_empresa int IDENTITY(1,1) NOT NULL,
	codigo varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	razon_social nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	nombre_comercial nvarchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	identificador_fiscal nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	pais_iso char(2) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'HN' NOT NULL,
	moneda_iso char(3) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'HNL' NOT NULL,
	zona_horaria nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'America/Tegucigalpa' NOT NULL,
	activa bit DEFAULT 1 NOT NULL,
	fecha_activacion datetime2(0) DEFAULT sysdatetime() NOT NULL,
	fecha_baja datetime2(0) NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(0) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT suser_sname() NOT NULL,
	fecha_creacion datetime2(0) DEFAULT sysdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(0) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_empresas PRIMARY KEY (id_empresa),
	CONSTRAINT FK_empresas_pais FOREIGN KEY (pais_iso) REFERENCES eBD_SPD.dbo.catalogo_paises(pais_iso)
);
 CREATE NONCLUSTERED INDEX IX_empresas_activa ON eBD_SPD.dbo.empresas (  activa ASC  )  
	 INCLUDE ( razon_social ) 
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_empresas_codigo ON eBD_SPD.dbo.empresas (  codigo ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;


-- eBD_SPD.dbo.personas definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.personas;

CREATE TABLE eBD_SPD.dbo.personas (
	id_persona int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	documento varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	tipo_documento varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'DNI' NOT NULL,
	nombres nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	apellidos nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	cargo varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	tarifa_diaria decimal(18,2) NULL,
	moneda_tarifa char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	telefono varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	email varchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_ingreso date NULL,
	fecha_baja date NULL,
	activo bit DEFAULT 1 NOT NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_personas PRIMARY KEY (id_persona),
	CONSTRAINT FK_personas_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa)
);
 CREATE NONCLUSTERED INDEX IX_personas_empresa_cargo ON eBD_SPD.dbo.personas (  id_empresa ASC  , cargo ASC  )  
	 WHERE  ([eliminado]=(0) AND [activo]=(1))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_personas_empresa_documento ON eBD_SPD.dbo.personas (  id_empresa ASC  , documento ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.personas WITH NOCHECK ADD CONSTRAINT CK_personas_cargo CHECK (([cargo]='OTRO' OR [cargo]='SUPERVISOR' OR [cargo]='MECANICO' OR [cargo]='COBRADOR' OR [cargo]='CONDUCTOR'));


-- eBD_SPD.dbo.rutas definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.rutas;

CREATE TABLE eBD_SPD.dbo.rutas (
	id_ruta int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	codigo varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	nombre nvarchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	descripcion nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	distancia_km decimal(10,2) NULL,
	activo bit DEFAULT 1 NOT NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_rutas PRIMARY KEY (id_ruta),
	CONSTRAINT FK_rutas_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa)
);
 CREATE UNIQUE NONCLUSTERED INDEX UX_rutas_empresa_codigo ON eBD_SPD.dbo.rutas (  id_empresa ASC  , codigo ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;


-- eBD_SPD.dbo.sen_boletines definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.sen_boletines;

CREATE TABLE eBD_SPD.dbo.sen_boletines (
	id_sen_boletin int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	fecha_vigencia date NOT NULL,
	url_imagen nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_publicacion datetime2(3) NULL,
	wp_media_id int NULL,
	procesado bit DEFAULT 0 NOT NULL,
	fecha_procesado datetime2(3) NULL,
	procesado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	observaciones nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	CONSTRAINT PK_sen_boletines PRIMARY KEY (id_sen_boletin),
	CONSTRAINT FK_sen_boletines_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa)
);
 CREATE NONCLUSTERED INDEX IX_sen_boletines_pendientes ON eBD_SPD.dbo.sen_boletines (  id_empresa ASC  , procesado ASC  , fecha_vigencia DESC  )  
	 WHERE  ([procesado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_sen_boletines_empresa_fecha ON eBD_SPD.dbo.sen_boletines (  id_empresa ASC  , fecha_vigencia ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;


-- eBD_SPD.dbo.talleres definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.talleres;

CREATE TABLE eBD_SPD.dbo.talleres (
	id_taller int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	codigo varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	nombre nvarchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	rtn varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	direccion nvarchar(250) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	telefono varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	email varchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	contacto nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	activo bit DEFAULT 1 NOT NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_talleres PRIMARY KEY (id_taller),
	CONSTRAINT FK_talleres_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa)
);
 CREATE UNIQUE NONCLUSTERED INDEX UX_talleres_empresa_codigo ON eBD_SPD.dbo.talleres (  id_empresa ASC  , codigo ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;


-- eBD_SPD.dbo.tasas_cambio definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.tasas_cambio;

CREATE TABLE eBD_SPD.dbo.tasas_cambio (
	id_tasa_cambio int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	fecha date NOT NULL,
	moneda_origen char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	moneda_destino char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	tasa decimal(18,8) NOT NULL,
	fuente nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_tasas_cambio PRIMARY KEY (id_tasa_cambio),
	CONSTRAINT FK_tasas_cambio_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa)
);
 CREATE NONCLUSTERED INDEX IX_tasas_cambio_par_fecha ON eBD_SPD.dbo.tasas_cambio (  id_empresa ASC  , moneda_origen ASC  , moneda_destino ASC  , fecha DESC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_tasas_cambio_empresa_fecha_par ON eBD_SPD.dbo.tasas_cambio (  id_empresa ASC  , fecha ASC  , moneda_origen ASC  , moneda_destino ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.tasas_cambio WITH NOCHECK ADD CONSTRAINT CK_tasas_cambio_tasa CHECK (([tasa]>(0)));
ALTER TABLE eBD_SPD.dbo.tasas_cambio WITH NOCHECK ADD CONSTRAINT CK_tasas_cambio_monedas CHECK (([moneda_origen]<>[moneda_destino]));


-- eBD_SPD.dbo.tipos_vehiculo definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.tipos_vehiculo;

CREATE TABLE eBD_SPD.dbo.tipos_vehiculo (
	id_tipo_vehiculo int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	codigo varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	nombre nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	descripcion nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	activo bit DEFAULT 1 NOT NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_tipos_vehiculo PRIMARY KEY (id_tipo_vehiculo),
	CONSTRAINT FK_tipos_vehiculo_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa)
);
 CREATE UNIQUE NONCLUSTERED INDEX UX_tipos_vehiculo_empresa_codigo ON eBD_SPD.dbo.tipos_vehiculo (  id_empresa ASC  , codigo ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;


-- eBD_SPD.dbo.usuarios_empresas definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.usuarios_empresas;

CREATE TABLE eBD_SPD.dbo.usuarios_empresas (
	id_usuario uniqueidentifier NOT NULL,
	id_empresa int NOT NULL,
	rol_principal nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	estado_acceso nvarchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'ACTIVO' NOT NULL,
	fecha_alta datetime2(0) DEFAULT sysdatetime() NOT NULL,
	fecha_baja datetime2(0) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT suser_sname() NOT NULL,
	fecha_creacion datetime2(0) DEFAULT sysdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(0) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_usuarios_empresas PRIMARY KEY (id_usuario,id_empresa),
	CONSTRAINT FK_usuarios_empresas_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa),
	CONSTRAINT FK_usuarios_empresas_usuario FOREIGN KEY (id_usuario) REFERENCES eBD_SPD.dbo.usuarios(id_usuario)
);
 CREATE NONCLUSTERED INDEX IX_usuarios_empresas_empresa ON eBD_SPD.dbo.usuarios_empresas (  id_empresa ASC  , estado_acceso ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_usuarios_empresas_usuario ON eBD_SPD.dbo.usuarios_empresas (  id_usuario ASC  , estado_acceso ASC  )  
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.usuarios_empresas WITH NOCHECK ADD CONSTRAINT CK_usuarios_empresas_estado_acceso CHECK (([estado_acceso]='REVOCADO' OR [estado_acceso]='INVITADO' OR [estado_acceso]='SUSPENDIDO' OR [estado_acceso]='ACTIVO'));


-- eBD_SPD.dbo.vehiculos definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.vehiculos;

CREATE TABLE eBD_SPD.dbo.vehiculos (
	id_vehiculo int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	id_tipo_vehiculo int NOT NULL,
	id_ruta int NULL,
	placa varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	numero_interno varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	marca nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	modelo nvarchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	anio smallint NULL,
	vin varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	color nvarchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	capacidad int NULL,
	tipo_combustible varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'DIESEL' NOT NULL,
	km_inicial decimal(12,2) DEFAULT 0 NOT NULL,
	fecha_alta date NULL,
	fecha_baja date NULL,
	activo bit DEFAULT 1 NOT NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_vehiculos PRIMARY KEY (id_vehiculo),
	CONSTRAINT FK_vehiculos_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa),
	CONSTRAINT FK_vehiculos_ruta FOREIGN KEY (id_ruta) REFERENCES eBD_SPD.dbo.rutas(id_ruta),
	CONSTRAINT FK_vehiculos_tipo FOREIGN KEY (id_tipo_vehiculo) REFERENCES eBD_SPD.dbo.tipos_vehiculo(id_tipo_vehiculo)
);
 CREATE NONCLUSTERED INDEX IX_vehiculos_empresa_ruta ON eBD_SPD.dbo.vehiculos (  id_empresa ASC  , id_ruta ASC  )  
	 WHERE  ([eliminado]=(0) AND [id_ruta] IS NOT NULL)
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_vehiculos_empresa_tipo ON eBD_SPD.dbo.vehiculos (  id_empresa ASC  , id_tipo_vehiculo ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_vehiculos_empresa_placa ON eBD_SPD.dbo.vehiculos (  id_empresa ASC  , placa ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.vehiculos WITH NOCHECK ADD CONSTRAINT CK_vehiculos_km_inicial CHECK (([km_inicial]>=(0)));
ALTER TABLE eBD_SPD.dbo.vehiculos WITH NOCHECK ADD CONSTRAINT CK_vehiculos_anio CHECK (([anio] IS NULL OR [anio]>=(1950) AND [anio]<=(2100)));


-- eBD_SPD.dbo.cargas_combustible definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.cargas_combustible;

CREATE TABLE eBD_SPD.dbo.cargas_combustible (
	id_carga_combustible int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	id_vehiculo int NOT NULL,
	fecha date NOT NULL,
	hora time(0) NULL,
	no_factura varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	proveedor nvarchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	tipo_combustible varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	unidad_medida varchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'GAL' NOT NULL,
	cantidad decimal(12,3) NOT NULL,
	precio_unitario decimal(18,4) NOT NULL,
	moneda char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	total AS (CONVERT([decimal](18,4),[cantidad])*[precio_unitario]) PERSISTED,
	km_odometro decimal(12,2) NULL,
	id_conductor int NULL,
	observaciones nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_cargas_combustible PRIMARY KEY (id_carga_combustible),
	CONSTRAINT FK_cargas_combustible_conductor FOREIGN KEY (id_conductor) REFERENCES eBD_SPD.dbo.personas(id_persona),
	CONSTRAINT FK_cargas_combustible_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa),
	CONSTRAINT FK_cargas_combustible_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES eBD_SPD.dbo.vehiculos(id_vehiculo)
);
 CREATE NONCLUSTERED INDEX IX_cargas_combustible_empresa_fecha ON eBD_SPD.dbo.cargas_combustible (  id_empresa ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_cargas_combustible_vehiculo_fecha ON eBD_SPD.dbo.cargas_combustible (  id_empresa ASC  , id_vehiculo ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_cargas_combustible_empresa_factura ON eBD_SPD.dbo.cargas_combustible (  id_empresa ASC  , no_factura ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.cargas_combustible WITH NOCHECK ADD CONSTRAINT CK_cargas_combustible_cantidad CHECK (([cantidad]>(0)));
ALTER TABLE eBD_SPD.dbo.cargas_combustible WITH NOCHECK ADD CONSTRAINT CK_cargas_combustible_precio CHECK (([precio_unitario]>=(0)));
ALTER TABLE eBD_SPD.dbo.cargas_combustible WITH NOCHECK ADD CONSTRAINT CK_cargas_combustible_unidad CHECK (([unidad_medida]='KWH' OR [unidad_medida]='LTR' OR [unidad_medida]='GAL'));


-- eBD_SPD.dbo.categorias_repuesto definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.categorias_repuesto;

CREATE TABLE eBD_SPD.dbo.categorias_repuesto (
	id_categoria_repuesto int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	nombre nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	descripcion nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	activo bit DEFAULT 1 NOT NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_categorias_repuesto PRIMARY KEY (id_categoria_repuesto),
	CONSTRAINT FK_categorias_repuesto_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa)
);
 CREATE UNIQUE NONCLUSTERED INDEX UX_categorias_repuesto_empresa_nombre ON eBD_SPD.dbo.categorias_repuesto (  id_empresa ASC  , nombre ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;


-- eBD_SPD.dbo.gastos_repuestos definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.gastos_repuestos;

CREATE TABLE eBD_SPD.dbo.gastos_repuestos (
	id_gasto_repuesto int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	id_vehiculo int NOT NULL,
	id_categoria_repuesto int NOT NULL,
	fecha date NOT NULL,
	no_factura varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	proveedor nvarchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	descripcion nvarchar(250) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	cantidad decimal(12,3) DEFAULT 1 NOT NULL,
	precio_unitario decimal(18,4) NOT NULL,
	moneda char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	subtotal AS (CONVERT([decimal](18,4),[cantidad])*[precio_unitario]) PERSISTED,
	km_odometro decimal(12,2) NULL,
	observaciones nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_gastos_repuestos PRIMARY KEY (id_gasto_repuesto),
	CONSTRAINT FK_gastos_repuestos_categoria FOREIGN KEY (id_categoria_repuesto) REFERENCES eBD_SPD.dbo.categorias_repuesto(id_categoria_repuesto),
	CONSTRAINT FK_gastos_repuestos_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa),
	CONSTRAINT FK_gastos_repuestos_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES eBD_SPD.dbo.vehiculos(id_vehiculo)
);
 CREATE NONCLUSTERED INDEX IX_gastos_repuestos_categoria ON eBD_SPD.dbo.gastos_repuestos (  id_empresa ASC  , id_categoria_repuesto ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_gastos_repuestos_empresa_fecha ON eBD_SPD.dbo.gastos_repuestos (  id_empresa ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_gastos_repuestos_vehiculo_fecha ON eBD_SPD.dbo.gastos_repuestos (  id_empresa ASC  , id_vehiculo ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.gastos_repuestos WITH NOCHECK ADD CONSTRAINT CK_gastos_repuestos_cantidad CHECK (([cantidad]>(0)));
ALTER TABLE eBD_SPD.dbo.gastos_repuestos WITH NOCHECK ADD CONSTRAINT CK_gastos_repuestos_precio CHECK (([precio_unitario]>=(0)));


-- eBD_SPD.dbo.ingresos_operativos definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.ingresos_operativos;

CREATE TABLE eBD_SPD.dbo.ingresos_operativos (
	id_ingreso_operativo int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	id_vehiculo int NOT NULL,
	fecha date NOT NULL,
	id_ruta int NULL,
	viajes int NULL,
	pasajeros int NULL,
	km_recorridos decimal(12,2) NULL,
	monto_ingreso decimal(18,2) NOT NULL,
	moneda char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	observaciones nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_ingresos_operativos PRIMARY KEY (id_ingreso_operativo),
	CONSTRAINT FK_ingresos_operativos_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa),
	CONSTRAINT FK_ingresos_operativos_ruta FOREIGN KEY (id_ruta) REFERENCES eBD_SPD.dbo.rutas(id_ruta),
	CONSTRAINT FK_ingresos_operativos_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES eBD_SPD.dbo.vehiculos(id_vehiculo)
);
 CREATE NONCLUSTERED INDEX IX_ingresos_operativos_empresa_fecha ON eBD_SPD.dbo.ingresos_operativos (  id_empresa ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_ingresos_operativos_ruta_fecha ON eBD_SPD.dbo.ingresos_operativos (  id_empresa ASC  , id_ruta ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0) AND [id_ruta] IS NOT NULL)
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_ingresos_operativos_vehiculo_fecha ON eBD_SPD.dbo.ingresos_operativos (  id_empresa ASC  , id_vehiculo ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.ingresos_operativos WITH NOCHECK ADD CONSTRAINT CK_ingresos_operativos_monto CHECK (([monto_ingreso]>=(0)));
ALTER TABLE eBD_SPD.dbo.ingresos_operativos WITH NOCHECK ADD CONSTRAINT CK_ingresos_operativos_viajes CHECK (([viajes] IS NULL OR [viajes]>=(0)));
ALTER TABLE eBD_SPD.dbo.ingresos_operativos WITH NOCHECK ADD CONSTRAINT CK_ingresos_operativos_pasajeros CHECK (([pasajeros] IS NULL OR [pasajeros]>=(0)));


-- eBD_SPD.dbo.odometro_diario definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.odometro_diario;

CREATE TABLE eBD_SPD.dbo.odometro_diario (
	id_odometro_diario int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	id_vehiculo int NOT NULL,
	fecha date NOT NULL,
	km_inicial decimal(12,2) NOT NULL,
	km_final decimal(12,2) NOT NULL,
	km_recorridos AS ([km_final]-[km_inicial]) PERSISTED,
	id_ruta int NULL,
	id_conductor int NULL,
	observaciones nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_odometro_diario PRIMARY KEY (id_odometro_diario),
	CONSTRAINT FK_odometro_diario_conductor FOREIGN KEY (id_conductor) REFERENCES eBD_SPD.dbo.personas(id_persona),
	CONSTRAINT FK_odometro_diario_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa),
	CONSTRAINT FK_odometro_diario_ruta FOREIGN KEY (id_ruta) REFERENCES eBD_SPD.dbo.rutas(id_ruta),
	CONSTRAINT FK_odometro_diario_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES eBD_SPD.dbo.vehiculos(id_vehiculo)
);
 CREATE NONCLUSTERED INDEX IX_odometro_diario_empresa_fecha ON eBD_SPD.dbo.odometro_diario (  id_empresa ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_odometro_diario_vehiculo_fecha ON eBD_SPD.dbo.odometro_diario (  id_empresa ASC  , id_vehiculo ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.odometro_diario WITH NOCHECK ADD CONSTRAINT CK_odometro_diario_km CHECK (([km_final]>=[km_inicial]));


-- eBD_SPD.dbo.ordenes_mantenimiento definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.ordenes_mantenimiento;

CREATE TABLE eBD_SPD.dbo.ordenes_mantenimiento (
	id_orden_mantenimiento int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	id_vehiculo int NOT NULL,
	id_taller int NOT NULL,
	fecha date NOT NULL,
	no_factura varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	tipo_mantenimiento varchar(20) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	descripcion nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	monto_mano_obra decimal(18,2) DEFAULT 0 NOT NULL,
	monto_repuestos decimal(18,2) DEFAULT 0 NOT NULL,
	monto_otros decimal(18,2) DEFAULT 0 NOT NULL,
	total AS (([monto_mano_obra]+[monto_repuestos])+[monto_otros]) PERSISTED,
	moneda char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	km_odometro decimal(12,2) NULL,
	observaciones nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_ordenes_mantenimiento PRIMARY KEY (id_orden_mantenimiento),
	CONSTRAINT FK_ordenes_mantenimiento_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa),
	CONSTRAINT FK_ordenes_mantenimiento_taller FOREIGN KEY (id_taller) REFERENCES eBD_SPD.dbo.talleres(id_taller),
	CONSTRAINT FK_ordenes_mantenimiento_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES eBD_SPD.dbo.vehiculos(id_vehiculo)
);
 CREATE NONCLUSTERED INDEX IX_ordenes_mantenimiento_taller_fecha ON eBD_SPD.dbo.ordenes_mantenimiento (  id_empresa ASC  , id_taller ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_ordenes_mantenimiento_vehiculo_fecha ON eBD_SPD.dbo.ordenes_mantenimiento (  id_empresa ASC  , id_vehiculo ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_ordenes_mantenimiento_empresa_factura ON eBD_SPD.dbo.ordenes_mantenimiento (  id_empresa ASC  , no_factura ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.ordenes_mantenimiento WITH NOCHECK ADD CONSTRAINT CK_ordenes_mantenimiento_montos CHECK (([monto_mano_obra]>=(0) AND [monto_repuestos]>=(0) AND [monto_otros]>=(0)));
ALTER TABLE eBD_SPD.dbo.ordenes_mantenimiento WITH NOCHECK ADD CONSTRAINT CK_ordenes_mantenimiento_tipo CHECK (([tipo_mantenimiento]='REVISION' OR [tipo_mantenimiento]='CORRECTIVO' OR [tipo_mantenimiento]='PREVENTIVO'));


-- eBD_SPD.dbo.polizas_seguros definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.polizas_seguros;

CREATE TABLE eBD_SPD.dbo.polizas_seguros (
	id_poliza_seguro int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	id_vehiculo int NOT NULL,
	no_poliza varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	aseguradora nvarchar(150) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	tipo_cobertura varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_inicio date NOT NULL,
	fecha_fin date NOT NULL,
	prima_total decimal(18,2) NOT NULL,
	moneda char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	costo_diario AS ([prima_total]/nullif(CONVERT([decimal](18,4),datediff(day,[fecha_inicio],[fecha_fin])+(1)),(0))) PERSISTED,
	observaciones nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_polizas_seguros PRIMARY KEY (id_poliza_seguro),
	CONSTRAINT FK_polizas_seguros_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa),
	CONSTRAINT FK_polizas_seguros_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES eBD_SPD.dbo.vehiculos(id_vehiculo)
);
 CREATE NONCLUSTERED INDEX IX_polizas_seguros_vehiculo_vigencia ON eBD_SPD.dbo.polizas_seguros (  id_empresa ASC  , id_vehiculo ASC  , fecha_inicio ASC  , fecha_fin ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_polizas_seguros_empresa_poliza_vehiculo ON eBD_SPD.dbo.polizas_seguros (  id_empresa ASC  , no_poliza ASC  , id_vehiculo ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.polizas_seguros WITH NOCHECK ADD CONSTRAINT CK_polizas_seguros_vigencia CHECK (([fecha_fin]>=[fecha_inicio]));
ALTER TABLE eBD_SPD.dbo.polizas_seguros WITH NOCHECK ADD CONSTRAINT CK_polizas_seguros_prima CHECK (([prima_total]>=(0)));
ALTER TABLE eBD_SPD.dbo.polizas_seguros WITH NOCHECK ADD CONSTRAINT CK_polizas_seguros_cobertura CHECK (([tipo_cobertura]='OTRA' OR [tipo_cobertura]='LIMITADA' OR [tipo_cobertura]='AMPLIA' OR [tipo_cobertura]='RC'));


-- eBD_SPD.dbo.precios_combustible definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.precios_combustible;

CREATE TABLE eBD_SPD.dbo.precios_combustible (
	id_precio_combustible int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	tipo_combustible varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	unidad_medida varchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT 'GAL' NOT NULL,
	fecha_vigencia date NOT NULL,
	precio decimal(18,4) NOT NULL,
	moneda char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fuente nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	id_sen_boletin int NULL,
	CONSTRAINT PK_precios_combustible PRIMARY KEY (id_precio_combustible),
	CONSTRAINT FK_precios_combustible_boletin FOREIGN KEY (id_sen_boletin) REFERENCES eBD_SPD.dbo.sen_boletines(id_sen_boletin),
	CONSTRAINT FK_precios_combustible_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa)
);
 CREATE NONCLUSTERED INDEX IX_precios_combustible_lookup ON eBD_SPD.dbo.precios_combustible (  id_empresa ASC  , tipo_combustible ASC  , fecha_vigencia DESC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_precios_combustible_empresa_tipo_fecha ON eBD_SPD.dbo.precios_combustible (  id_empresa ASC  , tipo_combustible ASC  , fecha_vigencia ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.precios_combustible WITH NOCHECK ADD CONSTRAINT CK_precios_combustible_precio CHECK (([precio]>(0)));
ALTER TABLE eBD_SPD.dbo.precios_combustible WITH NOCHECK ADD CONSTRAINT CK_precios_combustible_unidad CHECK (([unidad_medida]='KWH' OR [unidad_medida]='LTR' OR [unidad_medida]='GAL'));


-- eBD_SPD.dbo.salarios_diarios definition

-- Drop table

-- DROP TABLE eBD_SPD.dbo.salarios_diarios;

CREATE TABLE eBD_SPD.dbo.salarios_diarios (
	id_salario_diario int IDENTITY(1,1) NOT NULL,
	id_empresa int NOT NULL,
	id_vehiculo int NOT NULL,
	id_persona int NOT NULL,
	fecha date NOT NULL,
	cargo varchar(30) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	monto decimal(18,2) NOT NULL,
	moneda char(3) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	observaciones nvarchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	eliminado bit DEFAULT 0 NOT NULL,
	fecha_eliminado datetime2(3) NULL,
	creado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
	fecha_creacion datetime2(3) DEFAULT sysutcdatetime() NOT NULL,
	modificado_por nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
	fecha_modificacion datetime2(3) NULL,
	token_concurrencia timestamp NOT NULL,
	CONSTRAINT PK_salarios_diarios PRIMARY KEY (id_salario_diario),
	CONSTRAINT FK_salarios_diarios_empresa FOREIGN KEY (id_empresa) REFERENCES eBD_SPD.dbo.empresas(id_empresa),
	CONSTRAINT FK_salarios_diarios_persona FOREIGN KEY (id_persona) REFERENCES eBD_SPD.dbo.personas(id_persona),
	CONSTRAINT FK_salarios_diarios_vehiculo FOREIGN KEY (id_vehiculo) REFERENCES eBD_SPD.dbo.vehiculos(id_vehiculo)
);
 CREATE NONCLUSTERED INDEX IX_salarios_diarios_empresa_fecha ON eBD_SPD.dbo.salarios_diarios (  id_empresa ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE NONCLUSTERED INDEX IX_salarios_diarios_vehiculo_fecha ON eBD_SPD.dbo.salarios_diarios (  id_empresa ASC  , id_vehiculo ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
 CREATE UNIQUE NONCLUSTERED INDEX UX_salarios_diarios_vehiculo_persona_fecha ON eBD_SPD.dbo.salarios_diarios (  id_empresa ASC  , id_vehiculo ASC  , id_persona ASC  , fecha ASC  )  
	 WHERE  ([eliminado]=(0))
	 WITH (  PAD_INDEX = OFF ,FILLFACTOR = 100  ,SORT_IN_TEMPDB = OFF , IGNORE_DUP_KEY = OFF , STATISTICS_NORECOMPUTE = OFF , ONLINE = OFF , ALLOW_ROW_LOCKS = ON , ALLOW_PAGE_LOCKS = ON  )
	 ON [PRIMARY ] ;
ALTER TABLE eBD_SPD.dbo.salarios_diarios WITH NOCHECK ADD CONSTRAINT CK_salarios_diarios_monto CHECK (([monto]>=(0)));
ALTER TABLE eBD_SPD.dbo.salarios_diarios WITH NOCHECK ADD CONSTRAINT CK_salarios_diarios_cargo CHECK (([cargo]='OTRO' OR [cargo]='SUPERVISOR' OR [cargo]='MECANICO' OR [cargo]='COBRADOR' OR [cargo]='CONDUCTOR'));


-- dbo.vw_diccionario_datos source

ALTER VIEW dbo.vw_diccionario_datos AS
SELECT 
    s.name AS esquema,
    t.name AS tabla,
    c.column_id AS orden_columna,
    c.name AS columna,
    ty.name +
        CASE 
          WHEN ty.name IN ('varchar','char','varbinary','binary','nvarchar','nchar')
             THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX'
                              WHEN ty.name LIKE 'n%' THEN CONVERT(VARCHAR(10), c.max_length/2)
                              ELSE CONVERT(VARCHAR(10), c.max_length) END + ')'
          WHEN ty.name IN ('decimal','numeric')
             THEN '(' + CONVERT(VARCHAR(10), c.precision) + ',' + CONVERT(VARCHAR(10), c.scale) + ')'
          ELSE ''
        END AS tipo_datos,
    c.is_nullable AS admite_null,
    dc.definition AS default_definition,
    epCol.value AS descripcion_columna,
    epTab.value AS descripcion_tabla
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
LEFT JOIN sys.columns c ON t.object_id = c.object_id
LEFT JOIN sys.types ty ON c.user_type_id = ty.user_type_id
LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
LEFT JOIN sys.extended_properties epCol
   ON epCol.class = 1 AND epCol.major_id = t.object_id 
  AND epCol.minor_id = c.column_id AND epCol.name='MS_Description'
LEFT JOIN sys.extended_properties epTab
   ON epTab.class = 1 AND epTab.major_id = t.object_id
  AND epTab.minor_id = 0 AND epTab.name='MS_Description';