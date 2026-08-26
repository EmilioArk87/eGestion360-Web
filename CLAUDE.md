# eGestion360-Web

ASP.NET Core 8 Razor Pages + SQL Server. Base de datos: `eBD_SPD` (snake_case en español).

- Scripts SQL de migración: `2 - Script SQL/` con patrón `NNN_descripcion.sql`.
- Índice de scripts: `1 - Documetacion/INDICE_SCRIPTS_SQL.md`.
- Documentación de vistas: `1 - Documetacion/Vistas/`.
- Páginas Razor: `Pages/` (cada vista = `.cshtml` + `.cshtml.cs`).
- Responder siempre en **español**.

## Regla de oro — cambios a la base de datos

**Antes de ejecutar cualquier cambio contra la base de datos, dispara `/alerta-bd` y detente.**

Antes de ejecutar (o de pedir a otra herramienta/agente que ejecute) cualquier `ALTER`,
`CREATE`, `DROP`, `TRUNCATE`, `RENAME`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, o cualquier
script de `2 - Script SQL/` sobre una BD real, es **obligatorio** primero:

1. Clasificar los cambios en 4 colores (agrega/modifica/elimina/impacto).
2. Desplegar el widget de aprobación siguiendo el skill `.claude/commands/alerta-bd.md`.
3. **Detenerse** y esperar la aprobación explícita del usuario en el chat.

Nunca ejecutes cambios a `eBD_SPD` sin esa aprobación. Consultas de solo lectura
(`SELECT`, `EXPLAIN`) y crear/editar el **archivo** `.sql` en disco no requieren aprobación;
**ejecutar** el script sí.
