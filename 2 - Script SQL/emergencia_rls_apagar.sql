-- ============================================================
-- Script   : emergencia_rls_apagar.sql
-- Proposito: EMERGENCIA. Apaga la politica de Row-Level Security seg.pol_tenant (la enciende el script 037). Usar si, con la politica
--            encendida, alguna pantalla o proceso queda sin datos o rechaza escrituras por falta de contexto de tenant.
--            No borra nada ni cambia datos: solo deja de filtrar. Para volver a encender: script 037.
-- Autor    : eGestion360-Web
-- Fecha    : 2026-10-13
-- BD       : eBD_SPD
-- Ejecucion: una sola sentencia, sin PRECHECK ni dependencias, pensada para correrse de inmediato desde el panel del hosting. Es
--            idempotente: si la politica ya esta apagada o no existe, no hace nada. Por ser una EMERGENCIA no espera la aprobacion
--            previa de /alerta-bd: se avisa a quien corresponda despues.
-- ============================================================
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_tenant' AND schema_id = SCHEMA_ID(N'seg') AND is_enabled = 1)
BEGIN
    ALTER SECURITY POLICY seg.pol_tenant WITH (STATE = OFF);
    PRINT N'Politica seg.pol_tenant APAGADA.';
END
ELSE
    PRINT N'Nada que hacer: la politica no existe o ya estaba apagada.';

SELECT p.name AS politica, p.is_enabled AS encendida FROM sys.security_policies p WHERE p.name = N'pol_tenant';
