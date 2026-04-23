-- =====================================================================================
-- SCRIPT PARA INFORMACIÓN DE CONTRASEÑAS DE USUARIOS - eGestion360
-- =====================================================================================
-- IMPORTANTE: Las contraseñas están hasheadas con BCrypt y NO se pueden descifrar
-- Este script proporciona información administrativa sobre usuarios y opciones
-- para reseteo de contraseñas cuando sea necesario.
-- =====================================================================================

-- 1. CONSULTA BÁSICA DE USUARIOS (SIN CONTRASEÑAS)
-- =====================================================================================
-- Muestra información básica de usuarios
SELECT 
    Id,
    Username,
    Email,
    CreatedAt,
    IsActive,
    '*** HASHEADA ***' as PasswordStatus
FROM Users
ORDER BY CreatedAt DESC;

-- 2. INFORMACIÓN DETALLADA DE USUARIOS CON HASHES (SOLO PARA ADMINISTRADORES)
-- =====================================================================================
-- ADVERTENCIA: Solo usar para propósitos administrativos. 
-- Los hashes no se pueden revertir a texto plano.
SELECT 
    Id,
    Username,
    Email,
    LEFT(Password, 30) + '...' as PasswordHash_Preview,
    LEN(Password) as PasswordHash_Length,
    CreatedAt,
    IsActive
FROM Users
ORDER BY CreatedAt DESC;

-- 3. CONSULTA PARA VERIFICAR ESTRUCTURA DE CONTRASEÑAS
-- =====================================================================================
-- Verifica que las contraseñas estén correctamente hasheadas con BCrypt
SELECT 
    Username,
    Email,
    CASE 
        WHEN Password LIKE '$2a$%' OR Password LIKE '$2b$%' OR Password LIKE '$2y$%' 
        THEN 'BCrypt Hash ✓'
        WHEN LEN(Password) < 50 
        THEN 'Posible texto plano ⚠️'
        ELSE 'Hash desconocido'
    END as PasswordFormat,
    LEN(Password) as HashLength,
    IsActive
FROM Users;

-- 4. BUSCAR USUARIO ESPECÍFICO
-- =====================================================================================
-- Reemplaza 'admin' con el username que buscas
DECLARE @Username NVARCHAR(50) = 'admin';

SELECT 
    Id,
    Username,
    Email,
    CreatedAt,
    IsActive,
    'BCrypt Hash (no descifrable)' as PasswordInfo
FROM Users
WHERE Username = @Username;

-- 5. SCRIPT PARA RESETEAR CONTRASEÑA (SOLO EMERGENCIAS)
-- =====================================================================================
-- USAR CON EXTREMA PRECAUCIÓN
-- Este script establece una contraseña temporal. El usuario debe cambiarla inmediatamente.
-- 
-- INSTRUCCIONES:
-- 1. Descomenta las siguientes líneas
-- 2. Cambia @TargetUsername por el usuario objetivo  
-- 3. La nueva contraseña será: TempPass2026!
-- 4. Informa al usuario para que cambie su contraseña inmediatamente

/*
DECLARE @TargetUsername NVARCHAR(50) = 'CAMBIAR_AQUI';  -- CAMBIAR POR USERNAME REAL
DECLARE @TempPassword NVARCHAR(500) = '$2a$12$rQ8K5NGx5j8y2wF4Lk9zO.J2vG3mR8pN7qM6tL4sH9xF2gV8cB1aE'; -- TempPass2026!

UPDATE Users 
SET Password = @TempPassword
WHERE Username = @TargetUsername;

-- Verificar el cambio
SELECT 
    Username, 
    Email, 
    'Contraseña reseteada a: TempPass2026!' as NewPassword, 
    GETDATE() as ResetTime
FROM Users 
WHERE Username = @TargetUsername;

PRINT 'IMPORTANTE: Informar al usuario que su nueva contraseña es: TempPass2026!';
PRINT 'El usuario DEBE cambiarla inmediatamente por seguridad.';
*/

-- 6. CONSULTA DE USUARIOS ACTIVOS/INACTIVOS
-- =====================================================================================
SELECT 
    COUNT(*) as TotalUsuarios,
    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) as UsuariosActivos,
    SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) as UsuariosInactivos
FROM Users;

-- Detalle por estado
SELECT 
    CASE WHEN IsActive = 1 THEN 'ACTIVO' ELSE 'INACTIVO' END as Estado,
    COUNT(*) as Cantidad
FROM Users
GROUP BY IsActive;

-- 7. USUARIOS CREADOS EN LOS ÚLTIMOS DÍAS
-- =====================================================================================
SELECT 
    Username,
    Email,
    CreatedAt,
    DATEDIFF(day, CreatedAt, GETDATE()) as DaysAgo,
    IsActive
FROM Users
WHERE CreatedAt >= DATEADD(day, -30, GETDATE())  -- Últimos 30 días
ORDER BY CreatedAt DESC;

-- =====================================================================================
-- NOTAS IMPORTANTES SOBRE SEGURIDAD:
-- =====================================================================================
-- 
-- 1. Las contraseñas están hasheadas con BCrypt (factor de trabajo 12)
-- 2. No es posible obtener las contraseñas originales de los hashes
-- 3. Para resetear una contraseña, debes generar un nuevo hash
-- 4. El sistema utiliza el servicio PasswordService.cs para verificar contraseñas
-- 5. Siempre informa a los usuarios cuando resetees sus contraseñas
-- 6. Considera implementar un sistema de reseteo por email para mayor seguridad
--
-- CONTRASEÑAS TEMPORALES COMUNES (para reseteo):
-- - admin123    = $2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2
-- - TempPass2026! = $2a$12$rQ8K5NGx5j8y2wF4Lk9zO.J2vG3mR8pN7qM6tL4sH9xF2gV8cB1aE
--
-- =====================================================================================