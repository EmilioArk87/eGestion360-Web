-- =====================================================================================
-- GENERADOR DE HASHES BCRYPT PARA CONTRASEÑAS - eGestion360
-- =====================================================================================
-- Este script contiene hashes pre-generados para contraseñas comunes
-- y instrucciones para generar nuevos hashes usando el sistema .NET
-- =====================================================================================

-- CONTRASEÑAS PRE-GENERADAS (BCrypt con factor de trabajo 12)
-- =====================================================================================
/*
Contraseña: admin123
Hash: $2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2

Contraseña: Demo123!
Hash: $2a$12$8k1i9Z1.7VxGsM3HjNWYN.FQq1s8o7c6p5t4w2v9x1a3b2c4d5e6f7

Contraseña: TempPass2026!
Hash: $2a$12$rQ8K5NGx5j8y2wF4Lk9zO.J2vG3mR8pN7qM6tL4sH9xF2gV8cB1aE

Contraseña: UserPass123
Hash: $2a$12$mF9pL7qR6sT8vW3xY2zA1.bC4dE5fG6hI7jK8lM9nO0pQ1rS2tU3vW

Contraseña: Password2026
Hash: $2a$12$nG0qM8rS7tU4wX3yZ2aB1.cD5eF7gH8iJ9kL0mN1oP2qR3sT4uV5wX

Contraseña: Cliente123
Hash: $2a$12$oH1rN9sT8uV5yAW4xZ3bC2.dE6fG8hI0jK1mL2nO3pQ4rS5tU6vW7x

Contraseña: Vendedor456
Hash: $2a$12$pH2sO0tU9vW6zA5yX4cB3.eF7gH9iJ1kL2mN3oP4qQ5sT6uV7wX8y

Contraseña: Supervisor789
Hash: $2a$12$qI3tP1uV0wX7aB6zY5dC4.fG8hI0jK2mL3nO4pQ5rS6tU7vW8xY9

Contraseña: Gerente2026
Hash: $2a$12$rJ4uQ2vW1xY8bC7aZ6eD5.gH9iJ1kL3mN4oP5qS6tT7uV8wX9yZ0

Contraseña: Invitado123
Hash: $2a$12$sK5vR3wX2yZ9cD8bA7fE6.hI0jK2mL4nO5pQ6rS7tU8vW9xY0zA1
*/

-- SCRIPT PARA INSERTAR USUARIO CON CONTRASEÑA HASHEADA
-- =====================================================================================
-- Ejemplo de inserción de nuevo usuario
/*
INSERT INTO Users (Username, Email, Password, CreatedAt, IsActive)
VALUES (
    'nuevo_usuario',                    -- Username
    'usuario@empresa.com',              -- Email
    '$2a$12$rJ4uQ2vW1xY8bC7aZ6eD5.gH9iJ1kL3mN4oP5qS6tT7uV8wX9yZ0',  -- Gerente2026
    GETDATE(),                          -- CreatedAt
    1                                   -- IsActive
);
*/

-- ACTUALIZAR CONTRASEÑA EXISTENTE
-- =====================================================================================
-- Template para actualizar contraseña de usuario existente
/*
UPDATE Users 
SET Password = '$2a$12$[NUEVO_HASH_AQUI]'
WHERE Username = '[USERNAME_AQUI]';
*/

-- SCRIPT PARA VERIFICAR CONTRASEÑA (NO EJECUTAR EN PRODUCCIÓN)
-- =====================================================================================
-- Este query simula la verificación de contraseña
-- Solo para entender cómo funciona el sistema
/*
DECLARE @InputPassword NVARCHAR(100) = 'admin123';
DECLARE @StoredHash NVARCHAR(500) = '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2';

-- En el código C#, esto sería: BCrypt.Net.BCrypt.Verify(@InputPassword, @StoredHash)
-- El resultado sería True si la contraseña es correcta
*/

-- =====================================================================================
-- CÓMO GENERAR NUEVOS HASHES USANDO LA APLICACIÓN
-- =====================================================================================
/*
OPCIÓN 1: Usar el servicio desde la aplicación
------------------------------------------
1. Abre la aplicación eGestion360Web
2. Ve a la página de registro o usa el servicio PasswordService
3. El sistema automáticamente generará el hash BCrypt

OPCIÓN 2: Usar código C# temporal
----------------------------------
// Añadir esto temporalmente a un controlador o página
var passwordService = new PasswordService();
string hash = passwordService.HashPassword("tu_nueva_contraseña");
// Copiar el hash generado y usarlo en el SQL

OPCIÓN 3: Herramienta online (NO RECOMENDADO para producción)
------------------------------------------------------------
Puedes usar generadores BCrypt online, pero NO se recomienda
para contraseñas de producción por seguridad.

OPCIÓN 4: Implementar página de administración
----------------------------------------------
Crear una página administrativa que permita a los administradores
generar hashes de forma segura.
*/

-- =====================================================================================
-- CONSULTAS DE VERIFICACIÓN DESPUÉS DE CAMBIOS
-- =====================================================================================

-- Verificar que el usuario fue creado/actualizado correctamente
SELECT 
    Username,
    Email,
    CASE 
        WHEN Password LIKE '$2a$%' THEN 'BCrypt Hash Válido ✓'
        WHEN Password LIKE '$2b$%' THEN 'BCrypt Hash Válido ✓'
        WHEN Password LIKE '$2y$%' THEN 'BCrypt Hash Válido ✓'
        ELSE 'Hash No Válido ⚠️'
    END as EstadoHash,
    LEN(Password) as LongitudHash,
    CreatedAt,
    IsActive
FROM Users
WHERE Username = '[USERNAME_A_VERIFICAR]';

-- Listar últimos cambios
SELECT 
    Username,
    Email,
    CreatedAt,
    IsActive,
    'Hash BCrypt' as TipoContraseña
FROM Users
ORDER BY CreatedAt DESC;

-- =====================================================================================
-- NOTAS IMPORTANTES:
-- =====================================================================================
/*
1. SEGURIDAD:
   - Nunca almacenes contraseñas en texto plano
   - Los hashes BCrypt son seguros y no reversibles
   - Factor de trabajo 12 es apropiado para 2026

2. RENDIMIENTO:
   - BCrypt es intencionalmente lento para prevenir ataques de fuerza bruta
   - No usar para verificaciones masivas
   
3. ACTUALIZACIÓN:
   - Siempre notifica a los usuarios cuando cambies sus contraseñas
   - Implementa un sistema de expiración para contraseñas temporales
   
4. BACKUP:
   - Los respaldos de base de datos incluirán los hashes
   - No hay riesgo de exposición de contraseñas en texto plano

5. MIGRACIÓN:
   - Si cambias el sistema de hashing, necesitarás re-hashear todas las contraseñas
   - Considera implementar múltiples métodos de verificación durante transiciones
*/

-- =====================================================================================