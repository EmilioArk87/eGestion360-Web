# Skill DB y Documentacion de Vistas

Objetivo: estandarizar documentacion de vistas y control de cambios SQL.

## Instrucciones

Ejecuta siempre las siguientes tareas en este orden:

1. Documentar cada vista Razor (Pages/*.cshtml + Pages/*.cshtml.cs) en 1 - Documetacion/Vistas/ usando la plantilla _PlantillaVista.md.
2. Revisar la estructura de 2 - Script SQL/Estructura BD.sql y reportar hallazgos de consistencia y diferencias con modelos C# en 1 - Documetacion/REVISION_ESTRUCTURA_BD.md.
3. Crear un script SQL por cada modificacion con nombre NNN_descripcion_corta.sql y registrarlo en 1 - Documetacion/INDICE_SCRIPTS_SQL.md con el siguiente orden disponible.

## Reglas

- No omitir vistas.
- No modificar scripts historicos ya existentes.
- Mantener orden estricto de ejecucion por indice.
- Incluir PRECHECK, CAMBIO, POSTCHECK y ROLLBACK en scripts nuevos.
- Escribir en espanol.
