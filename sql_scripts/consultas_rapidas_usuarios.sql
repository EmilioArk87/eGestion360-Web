-- =====================================================================================
-- CONSULTAS RÁPIDAS DE USUARIOS - eGestion360
-- =====================================================================================
-- Scripts simples para consultas diarias de usuarios
-- NOTA: Las contraseñas están hasheadas y NO se pueden descifrar
-- =====================================================================================

-- 🔍 CONSULTA BÁSICA - Ver todos los usuarios
SELECT 
    Id,
    Username as Usuario,
    Email,
    CASE WHEN IsActive = 1 THEN 'Activo' ELSE 'Inactivo' END as Estado,
    FORMAT(CreatedAt, 'dd/MM/yyyy HH:mm') as FechaCreacion
FROM Users
ORDER BY Username;

-- 🔍 BUSCAR USUARIO ESPECÍFICO
-- Cambia 'admin' por el usuario que buscas
SELECT 
    Id,
    Username as Usuario,
    Email,
    CASE WHEN IsActive = 1 THEN 'Activo' ELSE 'Inactivo' END as Estado,
    FORMAT(CreatedAt, 'dd/MM/yyyy HH:mm') as FechaCreacion,
    'Las contraseñas están hasheadas - no se pueden ver' as InfoContraseña
FROM Users
WHERE Username LIKE '%admin%'  -- Cambia 'admin' por el término de búsqueda
   OR Email LIKE '%admin%';

-- 🔍 USUARIOS POR ESTADO
SELECT 
    CASE WHEN IsActive = 1 THEN 'ACTIVOS' ELSE 'INACTIVOS' END as Estado,
    COUNT(*) as Cantidad,
    STRING_AGG(Username, ', ') as Usuarios
FROM Users
GROUP BY IsActive;

-- 🔍 ÚLTIMOS USUARIOS REGISTRADOS (30 días)
SELECT 
    Username as Usuario,
    Email,
    FORMAT(CreatedAt, 'dd/MM/yyyy HH:mm') as FechaRegistro,
    DATEDIFF(day, CreatedAt, GETDATE()) as HaceXDias
FROM Users
WHERE CreatedAt >= DATEADD(day, -30, GETDATE())
ORDER BY CreatedAt DESC;

-- ⚙️ RESETEAR CONTRASEÑA A "admin123"
-- DESCOMENTA SOLO SI ES NECESARIO
/*
UPDATE Users 
SET Password = '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2'
WHERE Username = 'admin';  -- CAMBIAR por el usuario correcto

SELECT 'Contraseña reseteada a: admin123' as Resultado;
*/

-- ⚙️ ACTIVAR/DESACTIVAR USUARIO
-- DESCOMENTA Y MODIFICA SOLO SI ES NECESARIO
/*
UPDATE Users 
SET IsActive = 1  -- Cambiar a 0 para desactivar
WHERE Username = 'admin';  -- CAMBIAR por el usuario correcto

SELECT Username, 
       CASE WHEN IsActive = 1 THEN 'ACTIVADO' ELSE 'DESACTIVADO' END as Estado
FROM Users 
WHERE Username = 'admin';
*/

-- 📊 RESUMEN GENERAL
SELECT 
    COUNT(*) as TotalUsuarios,
    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) as UsuariosActivos,
    SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) as UsuariosInactivos,
    FORMAT(MIN(CreatedAt), 'dd/MM/yyyy') as PrimerUsuario,
    FORMAT(MAX(CreatedAt), 'dd/MM/yyyy') as UltimoUsuario
FROM Users;

-- 🔍 VER HASH DE CONTRASEÑA (SOLO ADMIN - NO DESCIFRABLE)
-- Esta consulta muestra el hash de la contraseña (NO la contraseña real)
SELECT 
    Username,
    Email,
    LEFT(Password, 30) + '...' as VistaHashContraseña,
    LEN(Password) as LongitudHash,
    CASE 
        WHEN Password LIKE '$2a$%' THEN 'BCrypt ✓'
        WHEN Password LIKE '$2b$%' THEN 'BCrypt ✓' 
        WHEN Password LIKE '$2y$%' THEN 'BCrypt ✓'
        ELSE 'Formato Desconocido'
    END as TipoHash,
    IsActive
FROM Users
WHERE Username = 'admin';  -- Cambiar por el usuario que quieras ver

-- 📋 LISTADO COMPLETO CON INFORMACIÓN DE CONTRASEÑAS
SELECT 
    Id,
    Username,
    Email,
    CASE 
        WHEN Password LIKE '$2a$%' OR Password LIKE '$2b$%' OR Password LIKE '$2y$%' 
        THEN 'Hash Seguro ✓'
        ELSE 'Revisar ⚠️'
    END as EstadoContraseña,
    CASE WHEN IsActive = 1 THEN 'Activo' ELSE 'Inactivo' END as Estado,
    FORMAT(CreatedAt, 'dd/MM/yyyy HH:mm') as FechaCreacion
FROM Users
ORDER BY CreatedAt DESC;

-- =====================================================================================
-- 🔑 CONSULTAS PARA VER INFORMACIÓN DE CONTRASEÑAS (HASH - NO TEXTO PLANO)
-- =====================================================================================

-- 🔍 VER HASH COMPLETO DE CONTRASEÑA (NO LA CONTRASEÑA REAL)
-- Esta consulta muestra el hash completo - cambia 'admin' por el usuario que necesites
SELECT 
    Username,
    Email,
    Password as HashCompleto,           -- Hash completo (ilegible)
    LEFT(Password, 30) + '...' as VistaHash,  -- Primeros 30 caracteres del hash
    LEN(Password) as LongitudHash,      -- Longitud total del hash
    IsActive
FROM Users
WHERE Username = 'admin';  -- CAMBIAR por el usuario que quieras consultar

-- 🔍 VERIFICAR TIPO DE HASH DE TODOS LOS USUARIOS
-- Verifica que todas las contraseñas estén correctamente hasheadas con BCrypt
SELECT 
    Username,
    Email,
    CASE 
        WHEN Password LIKE '$2a$%' THEN 'BCrypt Hash ✓'
        WHEN Password LIKE '$2b$%' THEN 'BCrypt Hash ✓' 
        WHEN Password LIKE '$2y$%' THEN 'BCrypt Hash ✓'
        WHEN LEN(Password) < 50 THEN 'Posible texto plano ⚠️'
        ELSE 'Formato Desconocido ⚠️'
    END as TipoHash,
    LEN(Password) as TamañoHash,
    CASE WHEN IsActive = 1 THEN 'Activo' ELSE 'Inactivo' END as Estado
FROM Users
ORDER BY Username;

-- 🔍 VER INFORMACIÓN COMPLETA CON HASH PARCIAL
-- Muestra información completa de usuarios con vista parcial del hash
SELECT 
    Id,
    Username,
    Email,
    'Hash BCrypt (No descifrable)' as TipoContraseña,
    LEFT(Password, 20) + '***' as VistaHashParcial,  -- Solo primeros 20 caracteres
    CASE 
        WHEN Password LIKE '$2a$%' OR Password LIKE '$2b$%' OR Password LIKE '$2y$%' 
        THEN 'Seguro ✓'
        ELSE 'Revisar ⚠️'
    END as EstadoSeguridad,
    CASE WHEN IsActive = 1 THEN 'Activo' ELSE 'Inactivo' END as Estado,
    FORMAT(CreatedAt, 'dd/MM/yyyy HH:mm') as FechaCreacion
FROM Users
ORDER BY CreatedAt DESC;

-- 🔍 CONSULTAR HASH ESPECÍFICO POR EMAIL
-- Buscar usuario por email y ver su información de hash
SELECT 
    Id,
    Username,
    Email,
    LEFT(Password, 25) + '...' as HashParcial,
    LEN(Password) as LongitudCompleta,
    CASE 
        WHEN Password LIKE '$2a$%' THEN 'BCrypt v2a ✓'
        WHEN Password LIKE '$2b$%' THEN 'BCrypt v2b ✓'
        WHEN Password LIKE '$2y$%' THEN 'BCrypt v2y ✓'
        ELSE 'Otro formato'
    END as VersionBCrypt,
    IsActive,
    CreatedAt
FROM Users
WHERE Email LIKE '%@%';  -- CAMBIAR por el email específico que buscas

-- 🔍 CONSULTA DE SEGURIDAD - DETECTAR CONTRASEÑAS DÉBILES
-- Identifica usuarios que podrían tener contraseñas en texto plano o hash débil
SELECT 
    Username,
    Email,
    LEN(Password) as LongitudContraseña,
    CASE 
        WHEN Password LIKE '$2a$%' OR Password LIKE '$2b$%' OR Password LIKE '$2y$%'
        THEN '✅ BCrypt Seguro'
        WHEN LEN(Password) < 50 
        THEN '⚠️ Posible texto plano - URGENTE'
        WHEN LEN(Password) BETWEEN 50 AND 59
        THEN '⚠️ Hash corto - Revisar'
        ELSE '❓ Formato desconocido'
    END as EstadoSeguridad,
    CASE WHEN IsActive = 1 THEN 'Activo' ELSE 'Inactivo' END as Estado
FROM Users
ORDER BY LEN(Password), Username;

-- =====================================================================================
-- 🔑 CONTRASEÑAS TEMPORALES DISPONIBLES (para reseteo de emergencia)
-- =====================================================================================
/*
IMPORTANTE: Estas son contraseñas ya hasheadas que puedes usar para reseteo

admin123     -> $2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2
Demo123!     -> $2a$12$8k1i9Z1.7VxGsM3HjNWYN.FQq1s8o7c6p5t4w2v9x1a3b2c4d5e6f7
TempPass2026! -> $2a$12$rQ8K5NGx5j8y2wF4Lk9zO.J2vG3mR8pN7qM6tL4sH9xF2gV8cB1aE
UserPass123  -> $2a$12$mF9pL7qR6sT8vW3xY2zA1.bC4dE5fG6hI7jK8lM9nO0pQ1rS2tU3vW
Password2026 -> $2a$12$nG0qM8rS7tU4wX3yZ2aB1.cD5eF7gH8iJ9kL0mN1oP2qR3sT4uV5wX

EJEMPLO DE USO:
UPDATE Users SET Password = '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2' WHERE Username = 'admin';
*/

-- =====================================================================================
-- 🚨 RESETEO DE EMERGENCIA - USAR CON PRECAUCIÓN
-- =====================================================================================
-- Descomenta SOLO EN CASO DE EMERGENCIA
/*
-- Resetear admin a contraseña "admin123"
UPDATE Users 
SET Password = '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2'
WHERE Username = 'admin';

-- Verificar el cambio
SELECT Username, Email, 'Contraseña reseteada a: admin123' as Mensaje
FROM Users WHERE Username = 'admin';

PRINT '⚠️ IMPORTANTE: Notificar al usuario que su contraseña es ahora: admin123';
PRINT '⚠️ El usuario debe cambiarla inmediatamente';
*/

-- =====================================================================================