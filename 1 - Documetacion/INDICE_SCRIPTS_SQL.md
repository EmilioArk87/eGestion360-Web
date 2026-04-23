# Indice de Scripts SQL - eGestion360

Orden de ejecucion recomendado para entorno limpio.

## Scripts existentes (base actual)

| Orden | Archivo | Tipo | Proposito | Estado |
|---|---|---|---|---|
| 001 | Estructura BD.sql | Base | Estructura principal de tablas, constraints e indices | Disponible |
| 002 | KPI_01_Catalogos.sql | Datos/Base | Carga de catalogos base | Disponible |
| 003 | KPI_02_Referencias.sql | Datos/Base | Carga de referencias del modulo KPI | Disponible |
| 004 | KPI_03_OperativoDiario.sql | Datos/Base | Estructura/objetos del operativo diario KPI | Disponible |
| 005 | KPI_04_Periodico.sql | Datos/Base | Estructura/objetos periodicos KPI | Disponible |
| 006 | KPI_05_Confidencial.sql | Datos/Base | Objetos del segmento confidencial KPI | Disponible |
| 007 | KPI_06_SenBoletines.sql | Datos/Base | Objetos para SEN boletines | Disponible |
| 008 | AddRequirePasswordChangeColumn.sql | Cambio | Agrega columna de cambio obligatorio de contrasena | Disponible |

## Nuevos scripts de modificacion

Regla de nombre: NNN_descripcion_corta.sql (NNN incremental de 3 digitos).

| Orden | Archivo | Proposito | Fecha | Estado |
|---|---|---|---|---|
| 009 | NNN_descripcion_corta.sql | Plantilla para proximo cambio | YYYY-MM-DD | Pendiente |

## Reglas de ejecucion

1. Ejecutar en orden ascendente de la columna Orden.
2. No cambiar ni reutilizar un numero de orden ya asignado.
3. Todo script nuevo debe registrarse inmediatamente en este indice.
4. Todo script nuevo debe incluir PRECHECK, CAMBIO, POSTCHECK y ROLLBACK.
