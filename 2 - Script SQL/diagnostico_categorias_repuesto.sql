-- Diagnóstico: categorias_repuesto
-- Ejecuta este script y compara el EmpresaId que ves en la sesión
-- con los id_empresa que tienen categorías registradas.

-- 1. Empresas registradas en el sistema
SELECT id_empresa, nombre, activo
FROM dbo.empresas
ORDER BY id_empresa;

-- 2. Categorías por empresa (incluyendo eliminadas para ver todo)
SELECT
    e.id_empresa,
    e.nombre        AS empresa,
    cr.id_categoria_repuesto,
    cr.nombre       AS categoria,
    cr.activo,
    cr.eliminado
FROM dbo.empresas e
LEFT JOIN dbo.categorias_repuesto cr ON cr.id_empresa = e.id_empresa
ORDER BY e.id_empresa, cr.nombre;

-- 3. Resumen: cuántas categorías activas tiene cada empresa
SELECT
    e.id_empresa,
    e.nombre                                           AS empresa,
    COUNT(CASE WHEN cr.activo = 1 AND cr.eliminado = 0 THEN 1 END) AS categorias_activas,
    COUNT(cr.id_categoria_repuesto)                    AS total_registros
FROM dbo.empresas e
LEFT JOIN dbo.categorias_repuesto cr ON cr.id_empresa = e.id_empresa
GROUP BY e.id_empresa, e.nombre
ORDER BY e.id_empresa;

-- 4. Usuarios y su empresa asignada (para saber con qué EmpresaId se loguean)
SELECT
    u.id,
    u.username,
    u.role,
    u.empresa_id,
    e.nombre AS empresa_nombre
FROM dbo.users u
LEFT JOIN dbo.empresas e ON e.id_empresa = u.empresa_id
ORDER BY u.empresa_id;
