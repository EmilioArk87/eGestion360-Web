-- ============================================================
-- Archivo  : verificar_despues_ventana_3.sql
-- Proposito: Comprobacion de SOLO LECTURA del estado de eBD_SPD DESPUES de la ventana 3 (script 037): la politica
--            seg.pol_tenant ENCENDIDA. Comprueba:
--              * is_enabled = 1 y los 199 predicados;
--              * que esta conexion NO fija contexto de sesion (SESSION_CONTEXT 'id_tenant' nulo), que es lo que hace el
--                ejecutor; sin contexto, la politica encendida no deja ver ninguna fila de tenant;
--              * cada una de las 37 tablas de tenant con politica devuelve 0 filas (se muestra cuantas filas reales tiene
--                segun sys.partitions, que no pasa por la politica: una tabla vacia no prueba nada, y la fila de cobertura
--                exige que al menos una tabla con filas quede oculta);
--              * empresas (directorio de lectura abierta) y domain_events (sin politica) siguen devolviendo todas sus filas;
--              * las 2 mixtas (tasas_cambio y sen_boletines) devuelven solo las filas globales (id_tenant nulo).
--            Es el estado EXACTO de esta ventana: con la politica apagada (ventanas 1 y 2) da FALLA.
-- Salida   : una fila por comprobacion (comprobacion, esperado, obtenido, estado 'OK' o 'FALLA') y una fila final RESUMEN.
-- Uso      : ejecutar con ejecutar_verificacion.ps1 (ver LEEME.md). No escribe nada.
-- Aviso    : si la ventana 3 se hace con el sitio en linea, domain_events puede cambiar entre las dos lecturas
--            (el despachador procesa eventos): si solo esa fila da FALLA, vuelva a correr el archivo.
-- Nota     : las cuentas de las mixtas no nombran la columna id_tenant (leen la fila como JSON), para dar FALLA y no un
--            error de compilacion si el archivo se corre antes de que exista la columna.
-- BD       : eBD_SPD
-- ============================================================
SET NOCOUNT ON;

WITH
vis AS (
    SELECT 1 AS n, N'empresas' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.empresas
    UNION ALL
    SELECT 2 AS n, N'empresa_modulos' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.empresa_modulos
    UNION ALL
    SELECT 3 AS n, N'empresa_roles' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.empresa_roles
    UNION ALL
    SELECT 4 AS n, N'empresa_rol_permisos' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.empresa_rol_permisos
    UNION ALL
    SELECT 5 AS n, N'cargos' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.cargos
    UNION ALL
    SELECT 6 AS n, N'personas' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.personas
    UNION ALL
    SELECT 7 AS n, N'persona_documentos' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.persona_documentos
    UNION ALL
    SELECT 8 AS n, N'persona_empresa' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.persona_empresa
    UNION ALL
    SELECT 9 AS n, N'empleados' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.empleados
    UNION ALL
    SELECT 10 AS n, N'clientes' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.clientes
    UNION ALL
    SELECT 11 AS n, N'proveedores' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.proveedores
    UNION ALL
    SELECT 12 AS n, N'bitacora_cambios' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.bitacora_cambios
    UNION ALL
    SELECT 13 AS n, N'vehiculos' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.vehiculos
    UNION ALL
    SELECT 14 AS n, N'tipos_vehiculo' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.tipos_vehiculo
    UNION ALL
    SELECT 15 AS n, N'rutas' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.rutas
    UNION ALL
    SELECT 16 AS n, N'talleres' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.talleres
    UNION ALL
    SELECT 17 AS n, N'categorias_repuesto' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.categorias_repuesto
    UNION ALL
    SELECT 18 AS n, N'odometro_diario' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.odometro_diario
    UNION ALL
    SELECT 19 AS n, N'cargas_combustible' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.cargas_combustible
    UNION ALL
    SELECT 20 AS n, N'salarios_diarios' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.salarios_diarios
    UNION ALL
    SELECT 21 AS n, N'ordenes_mantenimiento' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.ordenes_mantenimiento
    UNION ALL
    SELECT 22 AS n, N'gastos_repuestos' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.gastos_repuestos
    UNION ALL
    SELECT 23 AS n, N'polizas_seguros' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.polizas_seguros
    UNION ALL
    SELECT 24 AS n, N'peajes' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.peajes
    UNION ALL
    SELECT 25 AS n, N'control_salidas' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.control_salidas
    UNION ALL
    SELECT 26 AS n, N'notas' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.notas
    UNION ALL
    SELECT 27 AS n, N'ingresos_operativos' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.ingresos_operativos
    UNION ALL
    SELECT 28 AS n, N'precios_combustible' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.precios_combustible
    UNION ALL
    SELECT 29 AS n, N'condiciones_pago' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.condiciones_pago
    UNION ALL
    SELECT 30 AS n, N'condiciones_pago_cuotas' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.condiciones_pago_cuotas
    UNION ALL
    SELECT 31 AS n, N'formas_pago' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.formas_pago
    UNION ALL
    SELECT 32 AS n, N'impuestos' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.impuestos
    UNION ALL
    SELECT 33 AS n, N'productos_servicios' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.productos_servicios
    UNION ALL
    SELECT 34 AS n, N'facturas' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.facturas
    UNION ALL
    SELECT 35 AS n, N'factura_detalle' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.factura_detalle
    UNION ALL
    SELECT 36 AS n, N'factura_secuencias' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.factura_secuencias
    UNION ALL
    SELECT 37 AS n, N'pagos' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.pagos
    UNION ALL
    SELECT 38 AS n, N'pago_aplicaciones' AS tabla, N'T' AS tipo, COUNT(*) AS visibles FROM dbo.pago_aplicaciones
    UNION ALL
    SELECT 39 AS n, N'tasas_cambio' AS tabla, N'M' AS tipo, COUNT(*) AS visibles FROM dbo.tasas_cambio
    UNION ALL
    SELECT 40 AS n, N'sen_boletines' AS tabla, N'M' AS tipo, COUNT(*) AS visibles FROM dbo.sen_boletines
    UNION ALL
    SELECT 41 AS n, N'domain_events' AS tabla, N'I' AS tipo, COUNT(*) AS visibles FROM dbo.domain_events
),
reales AS (
    SELECT v.n, v.tabla, v.tipo, v.visibles,
           COALESCE((SELECT SUM(p.rows) FROM sys.partitions p
                     WHERE p.object_id = OBJECT_ID(N'dbo.' + v.tabla) AND p.index_id IN (0, 1)), 0) AS filas_reales
    FROM vis v
),
mix AS (
    SELECT N'tasas_cambio' AS tabla, COUNT(*) AS visibles,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL THEN 1 ELSE 0 END), 0) AS con_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS globales
    FROM dbo.tasas_cambio x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
    UNION ALL
    SELECT N'sen_boletines' AS tabla, COUNT(*) AS visibles,
           COALESCE(SUM(CASE WHEN j.t IS NOT NULL THEN 1 ELSE 0 END), 0) AS con_tenant,
           COALESCE(SUM(CASE WHEN j.t IS NULL THEN 1 ELSE 0 END), 0) AS globales
    FROM dbo.sen_boletines x
    CROSS APPLY (SELECT x.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) d (doc)
    CROSS APPLY (SELECT TRY_CAST(JSON_VALUE(d.doc, N'$.id_tenant') AS INT) AS t) j
),
pol AS (
    SELECT (SELECT COUNT(*) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS existe,
           (SELECT MAX(CAST(is_enabled AS INT)) FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg')) AS encendida,
           (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_tenant')) AS predicados,
           CASE WHEN SESSION_CONTEXT(N'id_tenant') IS NULL THEN N'sin contexto'
                ELSE CONCAT(N'con contexto id_tenant=', CAST(SESSION_CONTEXT(N'id_tenant') AS NVARCHAR(50))) END AS contexto
),
r (orden, comprobacion, esperado, obtenido) AS (
    SELECT v.orden, v.comprobacion, v.esperado, v.obtenido
    FROM pol
    CROSS APPLY (VALUES
        (10, N'Contexto de sesion id_tenant de esta conexion (el ejecutor no lo fija)', N'sin contexto', pol.contexto),
        (20, N'Politica seg.pol_tenant existe',                                   N'1',   CAST(pol.existe AS NVARCHAR(400))),
        (30, N'Politica ENCENDIDA (is_enabled = 1; la enciende el script 037)',   N'1',   COALESCE(CAST(pol.encendida AS NVARCHAR(400)), N'no existe')),
        (40, N'Predicados de la politica',                                        N'199', CAST(pol.predicados AS NVARCHAR(400)))
    ) v (orden, comprobacion, esperado, obtenido)
    UNION ALL
    SELECT 100 + n,
           CONCAT(N'RLS: ', tabla, N' devuelve 0 filas sin contexto (filas reales: ', filas_reales, N')'),
           N'0', CAST(visibles AS NVARCHAR(400))
    FROM reales
    WHERE tipo <> N'M' AND tabla NOT IN (N'empresas', N'domain_events')
    UNION ALL
    SELECT 200 + CASE tabla WHEN N'tasas_cambio' THEN 0 ELSE 1 END,
           CONCAT(N'RLS: ', tabla, N' no deja ver filas de ningun tenant (visibles: ', visibles, N', globales: ', globales, N')'),
           N'0', CAST(con_tenant AS NVARCHAR(400))
    FROM mix
),
rg (orden, comprobacion, esperado, obtenido, ok) AS (
    SELECT 300, CONCAT(N'Directorio empresas sigue legible: devuelve todas sus filas (filas reales: ', filas_reales, N')'),
           CAST(filas_reales AS NVARCHAR(400)), CAST(visibles AS NVARCHAR(400)),
           CASE WHEN visibles = filas_reales AND visibles > 0 THEN 1 ELSE 0 END
    FROM reales WHERE tabla = N'empresas'
    UNION ALL
    SELECT 310, CONCAT(N'domain_events (sin politica) sigue devolviendo todas sus filas (filas reales: ', filas_reales, N')'),
           CAST(filas_reales AS NVARCHAR(400)), CAST(visibles AS NVARCHAR(400)),
           CASE WHEN visibles = filas_reales THEN 1 ELSE 0 END
    FROM reales WHERE tabla = N'domain_events'
    UNION ALL
    SELECT 320 + CASE m.tabla WHEN N'tasas_cambio' THEN 0 ELSE 1 END,
           CONCAT(N'RLS: ', m.tabla, N' sigue mostrando sus filas globales (filas reales: ', rr.filas_reales, N')'),
           N'mayor que 0 si la tabla tiene filas', CAST(m.globales AS NVARCHAR(400)),
           CASE WHEN rr.filas_reales = 0 OR m.globales > 0 THEN 1 ELSE 0 END
    FROM mix m JOIN reales rr ON rr.tabla = m.tabla
    UNION ALL
    SELECT 400, N'Cobertura: tablas de tenant con filas reales que la politica oculta (prueba concluyente)',
           N'1 o mas', CAST(COUNT(*) AS NVARCHAR(400)),
           CASE WHEN COUNT(*) >= 1 THEN 1 ELSE 0 END
    FROM reales WHERE tipo <> N'M' AND tabla NOT IN (N'empresas', N'domain_events') AND filas_reales > 0 AND visibles = 0
),
f AS (
    SELECT orden, comprobacion, esperado, obtenido, CASE WHEN esperado = obtenido THEN 1 ELSE 0 END AS ok
    FROM r
    UNION ALL
    SELECT orden, comprobacion, esperado, obtenido, ok FROM rg
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
