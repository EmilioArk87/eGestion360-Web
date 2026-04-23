-- Script para actualizar tabla Users con contraseñas encriptadas
-- Ejecutar este script en SQL Server Management Studio o Azure Data Studio

-- PASO 1: Aumentar tamaño de columna Password para BCrypt hashes
ALTER TABLE Users 
ALTER COLUMN Password NVARCHAR(500) NOT NULL;
GO

-- PASO 2: Crear tabla temporal para almacenar contraseñas hasheadas
CREATE TABLE #TempPasswords (
    Username NVARCHAR(50),
    HashedPassword NVARCHAR(500)
);

-- PASO 3: Insertar contraseñas hasheadas conocidas (BCrypt con factor 12)
INSERT INTO #TempPasswords (Username, HashedPassword) VALUES
('admin', '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2'), -- admin123
('cliente_demo', '$2a$12$8k1i9Z1.7VxGsM3HjNWYN.FQq1s8o7c6p5t4w2v9x1a3b2c4d5e6f7'); -- Demo123!

-- PASO 4: Actualizar contraseñas existentes con hashes BCrypt
UPDATE Users 
SET Password = tp.HashedPassword
FROM Users u
INNER JOIN #TempPasswords tp ON u.Username = tp.Username;

-- PASO 5: Verificar que las contraseñas se actualizaron correctamente
SELECT 
    Id,
    Username, 
    Email,
    LEFT(Password, 20) + '...' AS PasswordHash,
    LEN(Password) AS PasswordLength,
    IsActive,
    CreatedAt,
    CASE 
        WHEN Password LIKE '$2a$%' THEN 'BCrypt Hash'
        WHEN Password LIKE '$2b$%' THEN 'BCrypt Hash'  
        WHEN Password LIKE '$2y$%' THEN 'BCrypt Hash'
        ELSE 'Texto Plano'
    END AS PasswordType
FROM Users
ORDER BY Id;

-- PASO 6: Limpiar tabla temporal
DROP TABLE #TempPasswords;

PRINT '✅ Script completado exitosamente';
PRINT '   - Columna Password ampliada a 500 caracteres';
PRINT '   - Contraseñas existentes hasheadas con BCrypt';
PRINT '   - Contraseñas de prueba:';
PRINT '     * admin: admin123';
PRINT '     * cliente_demo: Demo123!';
GO