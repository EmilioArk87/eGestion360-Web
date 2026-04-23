# /doc-db — Documentación de Vistas y Scripts SQL

Eres un asistente de desarrollo para el proyecto **eGestion360Web** (ASP.NET Core 8 Razor Pages + SQL Server). Cuando se invoca este comando, ejecutas las tres tareas siguientes en orden. Si el usuario pasa un argumento (p. ej. `/doc-db Login`), limita las tareas a la vista o tabla indicada; sin argumento, procesa todo el proyecto.

---

## Contexto del proyecto

- **Base de datos:** `eBD_SPD` (SQL Server). Esquema de referencia en `2 - Script SQL/Estructura BD.SQL`.
- **Convención de nombres BD:** snake_case en español (`id_empresa`, `nombre_usuario`, etc.).
- **Carpeta de scripts SQL:** `2 - Script SQL/`. Los archivos de migración siguen el patrón `NNN_descripcion.sql` donde `NNN` es un número de orden de 3 dígitos con cero a la izquierda (001, 002, …).
- **Carpeta de documentación:** `1 - Documetacion/`. El índice maestro de scripts vive en `1 - Documetacion/INDICE_SCRIPTS_SQL.md`.
- **Páginas Razor:** carpeta `Pages/`. Cada vista tiene un `.cshtml` y un `.cshtml.cs`.

---

## Tarea 1 — Documentar vistas (Razor Pages)

Para cada página `.cshtml` + `.cshtml.cs` del proyecto:

1. Lee ambos archivos.
2. Genera o actualiza el archivo `1 - Documetacion/Vistas/<NombrePagina>.md` con la siguiente estructura:

```
# <NombrePagina>

## Propósito
Una oración clara de para qué sirve esta página.

## Ruta
`/Pages/<ruta relativa>`

## Dependencias
- Servicios inyectados
- DbContext y DbSets utilizados
- Modelos de BD leídos / escritos

## Flujo principal
### GET
Descripción paso a paso de OnGet / OnGetAsync.

### POST
Descripción paso a paso de OnPost / OnPostAsync (si aplica).

## Validaciones y reglas de negocio
Lista de validaciones o lógica relevante.

## Redirecciones
| Condición | Destino |
|-----------|---------|
| ...       | ...     |

## Notas
Cualquier advertencia, deuda técnica o código de debug presente.
```

3. Si el archivo ya existe, actualízalo sin borrar secciones que hayas llenado previamente.

---

## Tarea 2 — Revisar estructura de BD

1. Lee `2 - Script SQL/Estructura BD.SQL`.
2. Para cada tabla encontrada, compara con los modelos C# en `Models/` y `Models/Flota/`.
3. Reporta las **discrepancias** en tres categorías:
   - **Columnas faltantes en el modelo C#** (existen en BD pero no en el modelo).
   - **Columnas extra en el modelo C#** (existen en el modelo pero no en la BD).
   - **Tipos incompatibles** (diferencia de tipo entre BD y propiedad C#).
4. Para cada discrepancia, indica el archivo del modelo y el nombre de columna/propiedad afectado.
5. No modifiques nada — solo reporta. El usuario decidirá qué corregir.

Tablas de referencia en `Estructura BD.SQL`:

| Tabla BD              | Modelo C#                          |
|-----------------------|------------------------------------|
| `usuarios`            | `Models/User.cs` (tabla mapeada)   |
| `empresas`            | `Models/Empresa.cs`                |
| `personas`            | `Models/Flota/Persona.cs`          |
| `rutas`               | `Models/Flota/Ruta.cs`             |
| `talleres`            | `Models/Flota/Taller.cs`           |
| `tipos_vehiculo`      | `Models/Flota/TipoVehiculo.cs`     |
| `vehiculos`           | `Models/Flota/Vehiculo.cs`         |
| `cargas_combustible`  | `Models/Flota/CargaCombustible.cs` |
| `categorias_repuesto` | `Models/Flota/CategoriaRepuesto.cs`|
| `gastos_repuesto`     | `Models/Flota/GastoRepuesto.cs`    |
| `odometros_diarios`   | `Models/Flota/OdometroDiario.cs`   |
| `ordenes_mantenimiento`| `Models/Flota/OrdenMantenimiento.cs`|
| `polizas_seguros`     | `Models/Flota/PolizaSeguro.cs`     |
| `salarios_diarios`    | `Models/Flota/SalarioDiario.cs`    |
| `EmailConfiguration`  | `Models/EmailConfiguration.cs`     |
| `PasswordResetCodes`  | `Models/PasswordResetCode.cs`      |

---

## Tarea 3 — Crear scripts SQL de modificación

Cuando detectes que se necesitan cambios en la BD (ya sea por discrepancias de la Tarea 2, o porque el usuario lo solicite explícitamente):

1. Crea un archivo `2 - Script SQL/<NNN>_<descripcion_corta>.sql` donde:
   - `NNN` = siguiente número disponible (lee el índice para saber cuál es).
   - `<descripcion_corta>` = snake_case, máximo 5 palabras.

2. Cada script debe incluir:
```sql
-- ============================================================
-- Script  : NNN_descripcion_corta.sql
-- Propósito: <una línea>
-- Autor   : eGestion360Web
-- Fecha   : YYYY-MM-DD
-- BD      : eBD_SPD
-- Notas   : Describir impacto, datos afectados, reversibilidad
-- ============================================================

-- REVERSIÓN (ejecutar si hay que deshacer):
-- <instrucciones de rollback>

-- CAMBIO:
<SQL aquí>
```

3. Después de crear el script, actualiza el índice `1 - Documetacion/INDICE_SCRIPTS_SQL.md`:

```markdown
# Índice de Scripts SQL — eGestion360

Orden de ejecución en entorno limpio (de arriba hacia abajo).

| # | Archivo | Propósito | Fecha | Estado |
|---|---------|-----------|-------|--------|
| 001 | `001_nombre.sql` | ... | YYYY-MM-DD | ✅ Aplicado / ⏳ Pendiente |
```

   - Agrega la nueva fila al final de la tabla.
   - Marca como `⏳ Pendiente` los scripts nuevos.
   - No modifiques el estado de scripts anteriores.

---

## Comportamiento general

- Trabaja en silencio: crea/actualiza archivos directamente sin pedir confirmación para operaciones de lectura y escritura de documentación.
- Antes de crear un script SQL que modifique datos de producción, **confirma con el usuario**.
- Si encuentras código de debug (p. ej. `ViewData["Debug"]`) en las vistas, menciónalo en la sección "Notas" de la documentación pero no lo elimines.
- Responde en **español**.
