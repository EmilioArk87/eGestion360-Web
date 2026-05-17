-- Seed: categorias_repuesto
-- Inserta categorías dummy para la primera empresa (id=1) y la empresa demo (id=2).
-- Ejecutar una sola vez. El índice único UX_categorias_repuesto_empresa_nombre
-- previene duplicados si se ejecuta más de una vez.

-- Categorías a insertar (compartidas por ambas empresas)
DECLARE @Categorias TABLE (nombre NVARCHAR(100), descripcion NVARCHAR(500));
INSERT INTO @Categorias VALUES
    ('Motor',            'Piezas y componentes del motor: pistones, cigüeñal, válvulas, juntas y filtros de aceite'),
    ('Transmisión',      'Caja de cambios, embrague, diferencial y componentes de la transmisión'),
    ('Frenos',           'Pastillas, discos, tambores, cilindros de freno y líquido de frenos'),
    ('Suspensión',       'Amortiguadores, resortes, rótulas, bujes y brazos de suspensión'),
    ('Dirección',        'Caja de dirección, terminales, barra estabilizadora y columna de dirección'),
    ('Sistema Eléctrico','Batería, alternador, arranque, fusibles, sensores y cableado'),
    ('Neumáticos',       'Neumáticos, llantas, válvulas y accesorios de rueda'),
    ('Carrocería',       'Paneles, puertas, parabrisas, parachoques y espejos'),
    ('Refrigeración',    'Radiador, termostato, bomba de agua, mangueras y líquido refrigerante'),
    ('Combustible',      'Bomba de combustible, inyectores, filtro de combustible y depósito'),
    ('Escape',           'Catalizador, silenciador, tubos de escape y juntas'),
    ('Climatización',    'Compresor de A/C, condensador, evaporador y filtro de habitáculo'),
    ('Iluminación',      'Faros, luces traseras, bombillas, LEDs y faros antiniebla'),
    ('Aceites y Fluidos','Aceite de motor, aceite de caja, líquido de frenos y dirección hidráulica'),
    ('Filtros',          'Filtro de aire, aceite, combustible y habitáculo'),
    ('Correas y Cadenas','Correa de distribución, correa serpentina, tensores y cadena de distribución'),
    ('Accesorios',       'Repuestos varios y accesorios no clasificados en otras categorías');

-- Insertar para cada empresa que exista (id 1 y 2)
DECLARE @Empresas TABLE (id_empresa INT);
INSERT INTO @Empresas
SELECT id_empresa FROM dbo.empresas WHERE id_empresa IN (1, 2);

IF NOT EXISTS (SELECT 1 FROM @Empresas)
BEGIN
    RAISERROR('No existe ninguna empresa con id 1 o 2. Cree una empresa primero.', 16, 1);
    RETURN;
END

INSERT INTO dbo.categorias_repuesto (id_empresa, nombre, descripcion, activo, eliminado, creado_por, fecha_creacion)
SELECT e.id_empresa, c.nombre, c.descripcion, 1, 0, 'seed', SYSUTCDATETIME()
FROM @Empresas e
CROSS JOIN @Categorias c
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.categorias_repuesto cr
    WHERE cr.id_empresa = e.id_empresa
      AND cr.nombre     = c.nombre
      AND cr.eliminado  = 0
);

-- Resultado final
SELECT
    id_empresa            AS Empresa,
    id_categoria_repuesto AS Id,
    nombre                AS Categoría,
    activo                AS Activo
FROM dbo.categorias_repuesto
WHERE id_empresa IN (1, 2)
  AND eliminado  = 0
ORDER BY id_empresa, nombre;
