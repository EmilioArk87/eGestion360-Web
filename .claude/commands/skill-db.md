# /skill-db - Skill de documentacion de vistas y control de cambios SQL

Eres un asistente de desarrollo para eGestion360-Web (ASP.NET Core Razor Pages + SQL Server).
Cuando se invoque este skill, ejecuta siempre estas 3 tareas en orden.

## Contexto base

- Archivo de estructura BD: 2 - Script SQL/Estructura BD.sql
- Carpeta de scripts SQL: 2 - Script SQL/
- Carpeta de documentacion: 1 - Documetacion/
- Carpeta de vistas Razor: Pages/
- Carpeta de salida de docs de vistas: 1 - Documetacion/Vistas/
- Indice maestro SQL: 1 - Documetacion/INDICE_SCRIPTS_SQL.md

## Tarea 1 - Documentar cada vista con su funcionalidad

Para cada par de archivos .cshtml y .cshtml.cs en Pages/ (incluyendo subcarpetas):

1. Crear o actualizar el documento correspondiente en:
   - 1 - Documetacion/Vistas/<ruta_relativa_sin_extension>.md
2. Usar esta estructura minima:

# <Nombre Vista>

## Proposito
Descripcion funcional corta de la vista.

## Ruta
Ruta de navegacion de la pagina.

## Funcionalidad
- Que hace el usuario en esta vista.
- Que datos consulta.
- Que datos guarda/modifica.

## Flujo tecnico
### GET
Pasos de carga de informacion.

### POST
Pasos de validacion y guardado (si aplica).

## Dependencias
Servicios inyectados, DbContext, modelos/entidades, helpers.

## Reglas de negocio
Validaciones, permisos, reglas de estado, restricciones.

## Manejo de errores
Mensajes al usuario, excepciones controladas y redirecciones.

## Notas
Hallazgos de debug, deuda tecnica o riesgos detectados.

Regla obligatoria: no omitir vistas; documentar todas las vistas detectadas.

## Tarea 2 - Revisar estructura de base de datos

1. Leer el archivo 2 - Script SQL/Estructura BD.sql.
2. Revisar consistencia estructural (nombres, tablas duplicadas, constraints, FKs, indices, defaults y checks).
3. Comparar la estructura contra modelos C# en Models/ y Models/Flota/.
4. Reportar hallazgos en:
   - 1 - Documetacion/REVISION_ESTRUCTURA_BD.md
5. Clasificar hallazgos en:
   - Diferencias de nombres (tabla/columna)
   - Columnas faltantes en modelo
   - Columnas extra en modelo
   - Tipos incompatibles
   - Riesgos de integridad o mantenimiento

Regla obligatoria: no modificar la BD en esta tarea; solo analisis y reporte.

## Tarea 3 - Crear script por cada modificacion y mantener indice de ejecucion

Cuando se defina una modificacion de BD:

1. Crear un archivo SQL nuevo en 2 - Script SQL/ con nombre:
   - NNN_descripcion_corta.sql
   - NNN de 3 digitos incrementales (001, 002, 003...)
2. Incluir encabezado estandar:

-- ============================================================
-- Script   : NNN_descripcion_corta.sql
-- Proposito: descripcion breve
-- Autor    : eGestion360-Web
-- Fecha    : YYYY-MM-DD
-- BD       : eBD_SPD
-- Requiere : precondiciones o dependencias
-- Rollback : descripcion breve de reversa
-- ============================================================

3. Incluir secciones:
   - PRECHECK (consultas de validacion previas)
   - CAMBIO (DDL/DML)
   - POSTCHECK (consultas de verificacion)
   - ROLLBACK (si es reversible)

4. Registrar el script en el indice:
   - 1 - Documetacion/INDICE_SCRIPTS_SQL.md
5. Agregar una fila nueva al final con estado inicial:
   - Pendiente

## Criterios de calidad

- Mantener trazabilidad entre hallazgo -> script -> indice.
- No sobrescribir scripts historicos ya ejecutados.
- Mantener orden de ejecucion explicito y cronologico.
- Escribir en espanol y con formato claro.
