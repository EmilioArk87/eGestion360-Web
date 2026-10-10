namespace eGestion360Web.Tests.SqlServer.Rls
{
    /// <summary>
    /// Esquema de laboratorio de la prueba de concepto de Row-Level Security (paso F0.5, ADR-001). No es un script de
    /// eBD_SPD: crea tablas <c>poc_*</c> en una base de prueba desechable, las siembra con datos inventados y al final
    /// activa la política, igual que haría un script real (primero la estructura y los datos, después la seguridad).
    ///
    /// Datos sembrados (los usan las pruebas para comparar):
    ///   * Tenant 1: 3 clientes y 4 facturas que suman 1000.00; tasa USD propia.
    ///   * Tenant 2: 2 clientes y 3 facturas que suman 600.00; tasa USD propia.
    ///   * Tenants 3 y 4: un cliente cada uno, sin facturas; los usan las pruebas que escriben.
    ///   * Tenants 5 a 24: 10 clientes y 500 facturas de 10.00 cada uno (volumen para el plan de ejecución).
    ///   * Tasas oficiales (sin tenant): USD y EUR.
    ///
    /// Contexto de sesión (lo fija la aplicación al abrir cada conexión, de solo lectura):
    ///   * <c>id_tenant</c>: el tenant de la petición, o nulo.
    ///   * <c>ambito</c>: <c>tenant</c> para las peticiones de usuarios; <c>plataforma</c> para los jobs que escriben
    ///     filas globales (por ejemplo, la tasa oficial del día).
    ///
    /// Los predicados de las tablas de tenant comparan solo el tenant, sin excepciones: cualquier OR (ámbito de
    /// plataforma, usuario de plataforma, UNION ALL) hace que SQL Server recorra el índice completo, con las filas de
    /// todos los tenants, en lugar de buscar las del tenant (medido en la prueba de concepto, ver
    /// 1 - Documetacion/Arquitectura/ADR-015). La plataforma lee los datos de un tenant con el contexto de ese tenant.
    /// </summary>
    public static class EsquemaPocRls
    {
        public const int TenantA = 1;
        public const int TenantB = 2;
        public const int TenantEscrituraC = 3;
        public const int TenantEscrituraD = 4;
        public const int PrimerTenantDeVolumen = 5;
        public const int UltimoTenantDeVolumen = 24;

        public const int ClientesA = 3, FacturasA = 4;
        public const decimal TotalA = 1000.00m;
        public const int ClientesB = 2, FacturasB = 3;
        public const decimal TotalB = 600.00m;
        public const int ClientesPorTenantDeVolumen = 10, FacturasPorTenantDeVolumen = 500;
        public const decimal TotalPorTenantDeVolumen = 5000.00m;
        public const int TasasGlobales = 2;

        public const string IndiceFacturas = "IX_poc_facturas_tenant_cliente";

        public const string Script = """
            CREATE SCHEMA seg;
            GO

            CREATE TABLE dbo.poc_tenants (
                id_tenant  INT            NOT NULL CONSTRAINT PK_poc_tenants PRIMARY KEY,
                nombre     NVARCHAR(100)  NOT NULL
            );

            CREATE TABLE dbo.poc_clientes (
                id_cliente INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_poc_clientes PRIMARY KEY,
                id_tenant  INT            NOT NULL CONSTRAINT FK_poc_clientes_tenant REFERENCES dbo.poc_tenants(id_tenant),
                nombre     NVARCHAR(100)  NOT NULL,
                CONSTRAINT UX_poc_clientes_tenant_id UNIQUE (id_tenant, id_cliente)
            );
            CREATE INDEX IX_poc_clientes_tenant_nombre ON dbo.poc_clientes(id_tenant, nombre);

            CREATE TABLE dbo.poc_facturas (
                id_factura INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_poc_facturas PRIMARY KEY,
                id_tenant  INT            NOT NULL,
                id_cliente INT            NOT NULL,
                numero     NVARCHAR(40)   NOT NULL,
                total      DECIMAL(18,2)  NOT NULL,
                CONSTRAINT FK_poc_facturas_cliente FOREIGN KEY (id_tenant, id_cliente)
                    REFERENCES dbo.poc_clientes(id_tenant, id_cliente)
            );
            CREATE INDEX IX_poc_facturas_tenant_cliente ON dbo.poc_facturas(id_tenant, id_cliente) INCLUDE (total);

            -- Tabla mixta: id_tenant nulo = tasa oficial, visible para todos; con valor = tasa propia del tenant.
            CREATE TABLE dbo.poc_tasas (
                id_tasa    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_poc_tasas PRIMARY KEY,
                id_tenant  INT            NULL CONSTRAINT FK_poc_tasas_tenant REFERENCES dbo.poc_tenants(id_tenant),
                moneda     CHAR(3)        NOT NULL,
                valor      DECIMAL(18,6)  NOT NULL
            );
            CREATE INDEX IX_poc_tasas_tenant_moneda ON dbo.poc_tasas(id_tenant, moneda);
            GO

            -- Tablas de tenant, lectura y escritura: solo el tenant de la sesión. Sin excepciones (ver el resumen arriba).
            CREATE FUNCTION seg.fn_tenant_actual (@id_tenant INT)
            RETURNS TABLE WITH SCHEMABINDING AS
            RETURN SELECT 1 AS permitido
                   WHERE @id_tenant = CAST(SESSION_CONTEXT(N'id_tenant') AS INT);
            GO

            -- Lectura en tablas mixtas: las filas globales y las del tenant de la sesión. Las dos condiciones son sobre
            -- la misma columna, así que SQL Server sigue buscando por el índice (dos rangos).
            CREATE FUNCTION seg.fn_mixta_lectura (@id_tenant INT)
            RETURNS TABLE WITH SCHEMABINDING AS
            RETURN SELECT 1 AS permitido
                   WHERE @id_tenant IS NULL
                      OR @id_tenant = CAST(SESSION_CONTEXT(N'id_tenant') AS INT);
            GO

            -- Escritura en tablas mixtas: las filas propias las escribe su tenant; las globales, solo la plataforma.
            CREATE FUNCTION seg.fn_mixta_escritura (@id_tenant INT)
            RETURNS TABLE WITH SCHEMABINDING AS
            RETURN SELECT 1 AS permitido
                   WHERE @id_tenant = CAST(SESSION_CONTEXT(N'id_tenant') AS INT)
                      OR (@id_tenant IS NULL AND CAST(SESSION_CONTEXT(N'ambito') AS NVARCHAR(20)) = N'plataforma');
            GO

            INSERT INTO dbo.poc_tenants (id_tenant, nombre)
            SELECT n.i, CONCAT(N'Tenant ', RIGHT(CONCAT(N'0', n.i), 2))
            FROM (SELECT TOP (24) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects) AS n;

            INSERT INTO dbo.poc_clientes (id_tenant, nombre) VALUES
                (1, N'Cliente A1'), (1, N'Cliente A2'), (1, N'Cliente A3'),
                (2, N'Cliente B1'), (2, N'Cliente B2'),
                (3, N'Cliente C1'),
                (4, N'Cliente D1');

            INSERT INTO dbo.poc_facturas (id_tenant, id_cliente, numero, total)
            SELECT c.id_tenant, c.id_cliente, v.numero, v.total
            FROM (VALUES
                    (N'Cliente A1', N'A-001', 100.00), (N'Cliente A1', N'A-002', 200.00),
                    (N'Cliente A2', N'A-003', 300.00), (N'Cliente A3', N'A-004', 400.00),
                    (N'Cliente B1', N'B-001', 150.00), (N'Cliente B1', N'B-002', 200.00),
                    (N'Cliente B2', N'B-003', 250.00)) AS v (cliente, numero, total)
            JOIN dbo.poc_clientes AS c ON c.nombre = v.cliente;

            INSERT INTO dbo.poc_clientes (id_tenant, nombre)
            SELECT t.id_tenant, CONCAT(N'Cliente ', t.id_tenant, N'-', n.i)
            FROM dbo.poc_tenants AS t
            CROSS JOIN (SELECT TOP (10) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects) AS n
            WHERE t.id_tenant BETWEEN 5 AND 24;

            INSERT INTO dbo.poc_facturas (id_tenant, id_cliente, numero, total)
            SELECT c.id_tenant, c.id_cliente, CONCAT(N'V-', c.id_tenant, N'-', c.id_cliente, N'-', n.i), 10.00
            FROM dbo.poc_clientes AS c
            CROSS JOIN (SELECT TOP (50) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects) AS n
            WHERE c.id_tenant BETWEEN 5 AND 24;

            INSERT INTO dbo.poc_tasas (id_tenant, moneda, valor) VALUES
                (NULL, 'USD', 24.500000), (NULL, 'EUR', 27.000000),
                (1, 'USD', 25.000000), (2, 'USD', 24.800000);

            UPDATE STATISTICS dbo.poc_clientes WITH FULLSCAN;
            UPDATE STATISTICS dbo.poc_facturas WITH FULLSCAN;
            GO

            -- Usuario con los permisos que tendría la aplicación: leer y escribir datos, nada más.
            CREATE USER usr_app WITHOUT LOGIN;
            GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO usr_app;
            GO

            -- Cada tabla de tenant lleva un filtro y cuatro bloqueos: insertar o dejar la fila en otro tenant (AFTER),
            -- y modificar o borrar una fila que la sesión ve pero no le pertenece (BEFORE).
            CREATE SECURITY POLICY seg.pol_tenant
                ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_clientes,
                ADD BLOCK  PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_clientes AFTER INSERT,
                ADD BLOCK  PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_clientes AFTER UPDATE,
                ADD BLOCK  PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_clientes BEFORE UPDATE,
                ADD BLOCK  PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_clientes BEFORE DELETE,
                ADD FILTER PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_facturas,
                ADD BLOCK  PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_facturas AFTER INSERT,
                ADD BLOCK  PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_facturas AFTER UPDATE,
                ADD BLOCK  PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_facturas BEFORE UPDATE,
                ADD BLOCK  PREDICATE seg.fn_tenant_actual(id_tenant)    ON dbo.poc_facturas BEFORE DELETE,
                ADD FILTER PREDICATE seg.fn_mixta_lectura(id_tenant)    ON dbo.poc_tasas,
                ADD BLOCK  PREDICATE seg.fn_mixta_escritura(id_tenant)  ON dbo.poc_tasas AFTER INSERT,
                ADD BLOCK  PREDICATE seg.fn_mixta_escritura(id_tenant)  ON dbo.poc_tasas AFTER UPDATE,
                ADD BLOCK  PREDICATE seg.fn_mixta_escritura(id_tenant)  ON dbo.poc_tasas BEFORE UPDATE,
                ADD BLOCK  PREDICATE seg.fn_mixta_escritura(id_tenant)  ON dbo.poc_tasas BEFORE DELETE
            WITH (STATE = ON, SCHEMABINDING = ON);
            GO
            """;
    }
}
