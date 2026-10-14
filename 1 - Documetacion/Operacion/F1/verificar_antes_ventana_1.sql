-- ============================================================
-- Archivo  : verificar_antes_ventana_1.sql
-- Proposito: Comprobacion de SOLO LECTURA del estado de eBD_SPD ANTES de la ventana 1 (scripts 025 a 030).
--            Confirma que la base de produccion sigue como la ensayada: sin ningun objeto de F1 (sin esquemas saas ni seg,
--            sin columnas id_tenant, sin politicas de seguridad), con las 56 tablas de dbo y las 125 claves foraneas
--            de hoy, y con las filas clave que no deben variar (empresas, Users, personas).
-- Salida   : una fila por comprobacion con las columnas comprobacion, esperado, obtenido y estado ('OK' o 'FALLA'),
--            y una fila final RESUMEN con cuantas dieron OK y cuantas FALLA. Hay FALLA en cualquier estado que NO sea
--            "antes de 025": por ejemplo, despues de aplicar 025 ya fallan los esquemas y las columnas.
-- Uso      : ejecutar con ejecutar_verificacion.ps1 (ver LEEME.md). No escribe nada: solo SELECT, WITH y DECLARE de
--            variables escalares; no crea ni cambia ningun objeto ni dato.
-- Ajuste   : si de verdad cambiaron las filas de empresas, Users o personas desde el ensayo (alta legitima), cambie el
--            numero esperado de la DECLARE de abajo y anotelo. Las demas cifras son estructura y no deben cambiar.
-- BD       : eBD_SPD
-- ============================================================
SET NOCOUNT ON;

DECLARE @esp_tablas_dbo INT = 56;    -- tablas del esquema dbo (60 en total: 4 mas estan en el esquema retirado)
DECLARE @esp_fks        INT = 125;   -- claves foraneas de toda la base
DECLARE @esp_empresas   INT = 4;
DECLARE @esp_users      INT = 7;
DECLARE @esp_personas   INT = 97;

WITH
c AS (
    SELECT (SELECT COUNT(*) FROM sys.schemas WHERE name = N'saas') AS esq_saas,
           (SELECT COUNT(*) FROM sys.schemas WHERE name = N'seg') AS esq_seg,
           (SELECT COUNT(*) FROM sys.columns WHERE name = N'id_tenant') AS cols_id_tenant,
           (SELECT COUNT(*) FROM sys.security_policies) AS politicas,
           (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo')) AS tablas_dbo,
           (SELECT COUNT(*) FROM sys.foreign_keys) AS fks,
           (SELECT COUNT(*) FROM sys.triggers WHERE name = N'TR_bitacora_cambios_inmutable' AND is_disabled = 0
                                                AND parent_id = OBJECT_ID(N'dbo.bitacora_cambios')) AS trigger_bitacora,
           (SELECT COUNT(*) FROM (VALUES (1, N'00000000000000000001'), (2, N'0000000000000000002'),
                                         (3, N'0000000000000000003'), (4, N'TRANSGAR')) v (id_empresa, codigo)
            WHERE EXISTS (SELECT 1 FROM dbo.empresas e
                          WHERE e.id_empresa = v.id_empresa AND RTRIM(e.codigo) = v.codigo AND e.eliminado = 0)) AS empresas_025,
           (SELECT COUNT(*) FROM dbo.empresas) AS empresas,
           (SELECT COUNT(*) FROM dbo.Users) AS users,
           (SELECT COUNT(*) FROM dbo.personas) AS personas
),
r (orden, comprobacion, esperado, obtenido) AS (
    SELECT v.orden, v.comprobacion, v.esperado, v.obtenido
    FROM c
    CROSS APPLY (VALUES
        (10,  N'Esquema saas: todavia no existe (lo crea 025)',                           N'0', CAST(c.esq_saas AS NVARCHAR(400))),
        (20,  N'Esquema seg: todavia no existe (lo crea 025)',                            N'0', CAST(c.esq_seg AS NVARCHAR(400))),
        (30,  N'Columnas id_tenant en toda la base',                                      N'0', CAST(c.cols_id_tenant AS NVARCHAR(400))),
        (40,  N'Politicas de seguridad (RLS)',                                            N'0', CAST(c.politicas AS NVARCHAR(400))),
        (50,  N'Tablas en el esquema dbo',                   CAST(@esp_tablas_dbo AS NVARCHAR(400)), CAST(c.tablas_dbo AS NVARCHAR(400))),
        (60,  N'Claves foraneas en total',                   CAST(@esp_fks AS NVARCHAR(400)),        CAST(c.fks AS NVARCHAR(400))),
        (70,  N'Empresas que espera 025 (ids 1 a 4 con su codigo, no eliminadas)',        N'4', CAST(c.empresas_025 AS NVARCHAR(400))),
        (80,  N'Disparador TR_bitacora_cambios_inmutable presente y activo (lo usa 027)', N'1', CAST(c.trigger_bitacora AS NVARCHAR(400))),
        (90,  N'Filas de dbo.empresas (no deben variar)',    CAST(@esp_empresas AS NVARCHAR(400)),   CAST(c.empresas AS NVARCHAR(400))),
        (100, N'Filas de dbo.Users (no deben variar)',       CAST(@esp_users AS NVARCHAR(400)),      CAST(c.users AS NVARCHAR(400))),
        (110, N'Filas de dbo.personas (no deben variar)',    CAST(@esp_personas AS NVARCHAR(400)),   CAST(c.personas AS NVARCHAR(400)))
    ) v (orden, comprobacion, esperado, obtenido)
),
f AS (
    SELECT orden, comprobacion, esperado, obtenido, CASE WHEN esperado = obtenido THEN 1 ELSE 0 END AS ok
    FROM r
)
SELECT z.comprobacion, z.esperado, z.obtenido, z.estado
FROM (
    SELECT orden, comprobacion, esperado, obtenido, CASE WHEN ok = 1 THEN N'OK' ELSE N'FALLA' END AS estado
    FROM f
    UNION ALL
    SELECT 2147483647, N'RESUMEN', N'0 FALLA', CONCAT(SUM(ok), N' OK, ', COUNT(*) - SUM(ok), N' FALLA'),
           CASE WHEN SUM(ok) = COUNT(*) THEN N'OK' ELSE N'FALLA' END
    FROM f
) z
ORDER BY z.orden;
