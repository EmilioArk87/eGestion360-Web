# 📊 KPI_Costo_Kilometro_Buses_v2.xlsx
## Documentación Técnica Completa

> **Archivo:** `KPI_Costo_Kilometro_Buses_v2.xlsx`  
> **Versión:** 2.0 · Abril 2026  
> **Propósito:** Control de costos por kilómetro para flota de 8 autobuses · San Pedro Sula, Honduras  
> **Moneda base:** Lempira hondureño (L) con conversión automática a USD  
> **Cobertura de datos:** Febrero 2026 – Marzo 2026 (datos operativos) · 2023–2026 (históricos)

---

## Tabla de Contenidos

- [1. Resumen General](#1-resumen-general)
- [2. Arquitectura del Archivo](#2-arquitectura-del-archivo)
- [3. Descripción Detallada de cada Hoja](#3-descripción-detallada-de-cada-hoja)
- [4. Relaciones entre Hojas](#4-relaciones-entre-hojas)
- [5. Fórmulas Clave y Lógica de Cálculo](#5-fórmulas-clave-y-lógica-de-cálculo)
- [6. Estructura de Base de Datos Equivalente](#6-estructura-de-base-de-datos-equivalente)
- [7. Roles y Permisos](#7-roles-y-permisos)
- [8. Convenciones de Formato y Color](#8-convenciones-de-formato-y-color)
- [9. Glosario](#9-glosario)

---

## 1. Resumen General

El archivo implementa un **sistema KPI de costos operativos** para una empresa de transporte de autobuses. Su función central es calcular el **costo en Lempiras por kilómetro recorrido (L/KM)** para cada unidad y cada ruta, consolidando todos los gastos operativos: combustible, salarios, repuestos, seguros y mantenimiento.

### Flota registrada

| Placa      | Marca / Modelo              | Color          | Llanta         | Año  | Ruta   |
|------------|-----------------------------|----------------|----------------|------|--------|
| ABC-1234   | Mercedes Benz O-500         | Blanco/Azul    | 275/70 R22.5   | 2018 | Ruta 1 |
| XYZ-5678   | Volvo B7R                   | Rojo/Blanco    | 295/80 R22.5   | 2019 | Ruta 2 |
| DEF-9012   | Scania K360                 | Amarillo       | 275/70 R22.5   | 2020 | Ruta 1 |
| GHI-3456   | Mercedes Benz O-400         | Blanco         | 295/80 R22.5   | 2017 | Ruta 3 |
| JKL-7890   | Volvo B9R                   | Azul/Blanco    | 275/70 R22.5   | 2021 | Ruta 2 |
| MNO-2345   | Scania F250                 | Verde/Blanco   | 295/80 R22.5   | 2019 | Ruta 3 |
| PQR-6789   | Mercedes Benz OF-1721       | Naranja/Blanco | 275/70 R22.5   | 2022 | Ruta 1 |
| STU-0123   | Volvo B8R                   | Blanco/Rojo    | 295/80 R22.5   | 2020 | Ruta 2 |

### Estadísticas del archivo

| Métrica                        | Valor                    |
|--------------------------------|--------------------------|
| Total de hojas                 | 13                       |
| Registros de operación diaria  | 240 (8 buses × 30 días)  |
| Registros de combustible       | 240 facturas             |
| Registros de salarios          | 240 (8 buses × 30 días)  |
| Registros de tasa de cambio    | 840 días hábiles         |
| Registros de precios SEN       | 85 períodos (2023–2026)  |
| Período operativo              | 16/02/2026 – 17/03/2026  |
| Rango histórico                | 02/01/2023 – 16/03/2026  |

---

## 2. Arquitectura del Archivo

### Mapa de hojas y colores de pestaña

```
┌─────────────────────────────────────────────────────────────────────┐
│  GRUPO NAVEGACIÓN          GRUPO OPERATIVO         GRUPO FINANCIERO  │
│  ┌─────────────┐           ┌──────────────┐        ┌─────────────┐  │
│  │ 🏠 Portada  │ ◄─Menú   │ Flota        │        │ Tasa Cambio │  │
│  │  (Azul osc.)│           │  (Azul)      │        │  (Azul osc.)│  │
│  └─────────────┘           ├──────────────┤        └─────────────┘  │
│  ┌─────────────┐           │ Registro     │                          │
│  │ 📊 KPIs    │ ◄─Result. │  Diario      │        GRUPO CONF.       │
│  │  (Gris osc.)│           │  (Verde)     │        ┌─────────────┐  │
│  └─────────────┘           ├──────────────┤        │ Ingresos 🔒 │  │
│                             │ Combustible  │        │  (Café)     │  │
│                             │  (Naranja)   │        ├─────────────┤  │
│                             ├──────────────┤        │ Rentabil. 🔒│  │
│                             │ Precios      │        │  (Café)     │  │
│                             │  Comb.       │        └─────────────┘  │
│                             │  (Naranja)   │                          │
│                             ├──────────────┤                          │
│                             │ Repuestos    │                          │
│                             │  & Llantas   │                          │
│                             │  (Morado)    │                          │
│                             ├──────────────┤                          │
│                             │ Personal &   │                          │
│                             │  Salarios    │                          │
│                             │  (Verde osc.)│                          │
│                             ├──────────────┤                          │
│                             │ Seguros      │                          │
│                             │  (Rojo)      │                          │
│                             ├──────────────┤                          │
│                             │ Mantenimiento│                          │
│                             │  (Gris)      │                          │
│                             └──────────────┘                          │
└─────────────────────────────────────────────────────────────────────┘
```

### Orden de las hojas

| # | Nombre de la Hoja       | Tipo          | Filas de datos | Columnas |
|---|-------------------------|---------------|:--------------:|:--------:|
| 1 | 🏠 Portada              | Menú visual   | —              | —        |
| 2 | 📊 KPIs                 | Dashboard     | 8 buses + tot. | 11       |
| 3 | Flota                   | Catálogo      | 8              | 12       |
| 4 | Registro Diario         | Transaccional | 240            | 10       |
| 5 | Combustible             | Transaccional | 240            | 11       |
| 6 | Precios Combustible     | Histórico     | 85             | 7        |
| 7 | Repuestos & Llantas     | Transaccional | 12             | 11       |
| 8 | Personal & Salarios     | Transaccional | 240            | 11       |
| 9 | Seguros                 | Transaccional | 10             | 9        |
|10 | Mantenimiento           | Transaccional | 10             | 11       |
|11 | Tasa de Cambio          | Referencia    | 840            | 6        |
|12 | Ingresos 🔒             | Confidencial  | 240            | 10       |
|13 | Rentabilidad 🔒         | Confidencial  | 4 rutas + tot. | 7        |

---

## 3. Descripción Detallada de cada Hoja

---

### 3.1 🏠 Portada — Menú de Navegación

**Propósito:** Pantalla de bienvenida y menú visual de acceso rápido a cada módulo del sistema.

**Contenido:**
- Título del sistema con fondo oscuro (`#0D1B2A`) y texto dorado
- **9 tarjetas de menú** con acceso a cada hoja principal:
  - Flota de Autobuses · Registro Diario · Combustible
  - Repuestos & Llantas · Personal & Salarios · Seguros & Accidentes
  - Tasa de Cambio · Ingresos (Conf.) · KPIs & Dashboard
- Pie de página con nota de confidencialidad

**No contiene datos calculados.** Es puramente navegacional.

---

### 3.2 📊 KPIs — Dashboard Principal

**Propósito:** Hoja central que consolida todos los costos y calcula el KPI principal `L/KM` por autobús y por ruta.

**Estructura de la hoja:**

#### Bloque 1 — Tarjetas KPI resumen (filas 4–9)

| Celda    | Indicador             | Fórmula                                                                                                   |
|----------|-----------------------|-----------------------------------------------------------------------------------------------------------|
| `B7`     | Costo Total Flota (L) | `=SUM(Combustible!F4:F243) + SUM('Repuestos & Llantas'!I4:I...) + SUM('Personal & Salarios'!H4:H...) + SUM(Seguros!F...) + SUM(Mantenimiento!H...)` |
| `D7`     | Km Total Flota        | `=IFERROR(SUM('Registro Diario'!D4:D243), 0)`                                                            |
| `F7`     | **Costo L/KM promedio** | `=IFERROR(B7/D7, 0)` ← **KPI PRINCIPAL**                                                              |
| `H7`     | Combustible L/KM      | `=IFERROR(SUM(Combustible!F4:F243) / SUM('Registro Diario'!D4:D243), 0)`                                |
| `J7`     | RRHH L/KM             | `=IFERROR(SUM('Personal & Salarios'!H4:H243) / SUM('Registro Diario'!D4:D243), 0)`                     |

#### Bloque 2 — Tabla desglosada por autobús (filas 12–21)

Para cada una de las 8 placas, las columnas calculan con `SUMIF` sobre la columna `Placa / Autobús` de cada hoja:

| Col | Indicador           | Fórmula (ejemplo para ABC-1234)                                                     |
|-----|---------------------|-------------------------------------------------------------------------------------|
| D   | Km Recorridos       | `=SUMIF('Registro Diario'!C:C, "ABC-1234", 'Registro Diario'!D:D)`                 |
| E   | Costo Combustible   | `=SUMIF(Combustible!C:C, "ABC-1234", Combustible!F:F)`                             |
| F   | Costo Repuestos     | `=SUMIF('Repuestos & Llantas'!C:C, "ABC-1234", 'Repuestos & Llantas'!I:I)`         |
| G   | Salarios            | `=SUMIF('Personal & Salarios'!C:C, "ABC-1234", 'Personal & Salarios'!H:H)`         |
| H   | Seguros             | `=SUMIF(Seguros!C:C, "ABC-1234", Seguros!F:F)`                                     |
| I   | Mantenimiento       | `=SUMIF(Mantenimiento!C:C, "ABC-1234", Mantenimiento!H:H)`                         |
| J   | **TOTAL COSTO**     | `=SUM(E:I)` para la fila del bus                                                    |
| K   | **$/KM (KPI)**      | `=IFERROR(J/D, "-")` ← **KPI INDIVIDUAL POR BUS**                                  |

#### Bloque 3 — Fila de totales (fila 21)

```
TOTALES FLOTA = SUM de cada columna D:J de los 8 buses
$/KM global   = Total Costo / Total KM
```

#### Bloque 4 — Comparativo mensual por ruta (filas 20–25)

Tabla manual para ingresar el $/KM mensual por ruta (Ruta 1, Ruta 2, Ruta 3) comparando hasta 9 meses.

---

### 3.3 Flota — Catálogo de Autobuses

**Propósito:** Registro maestro de los 8 autobuses de la flota. Es la tabla de referencia para todas las demás hojas.

**Columnas:**

| Col | Campo                           | Tipo       | Descripción                                      |
|-----|---------------------------------|------------|--------------------------------------------------|
| A   | `#`                             | Número     | Índice correlativo (1–8)                         |
| B   | `Placa`                         | Texto      | Identificador único del bus · Clave primaria      |
| C   | `Marca / Modelo`                | Texto      | Fabricante y modelo del vehículo                 |
| D   | `Color`                         | Texto      | Colores del bus                                  |
| E   | `Año`                           | Número     | Año de fabricación                               |
| F   | `Tamaño de Llanta`              | Texto      | Especificación técnica de la llanta              |
| G   | `Conductor Asignado (Semana)`   | Texto      | Nombre del conductor vigente esa semana          |
| H   | `Cobrador Asignado (Semana)`    | Texto      | Nombre del cobrador vigente esa semana           |
| I   | `Ruta Asignada`                 | Texto      | Ruta 1, Ruta 2 o Ruta 3                         |
| J   | `Estado`                        | Lista      | `Activo` / `Inactivo` / `En Mantenimiento` / `Baja` |
| K   | `Fecha Último Mantenimiento`    | Fecha      | Control de mantenimientos preventivos            |
| L   | `Observaciones`                 | Texto libre | Notas adicionales                               |

**Reglas de negocio:**
- La columna `Placa` es la **clave primaria** que vincula este catálogo con todas las hojas transaccionales.
- El campo `Estado` usa validación de datos (lista desplegable).
- `Conductor` y `Cobrador` se actualizan semanalmente.

---

### 3.4 Registro Diario — Kilometraje por Bus

**Propósito:** Registrar el kilometraje recorrido por cada autobús cada día operativo. Es la fuente del denominador en el cálculo `L/KM`.

**Volumen:** 240 registros (8 buses × 30 días: 16/02/2026 – 17/03/2026)

**Columnas:**

| Col | Campo                | Tipo      | Fórmula / Regla                                                    |
|-----|----------------------|-----------|--------------------------------------------------------------------|
| A   | `#`                  | Número    | Correlativo                                                        |
| B   | `Fecha`              | Fecha     | Ingreso manual (DD/MM/YYYY)                                        |
| C   | `Placa / Autobús`    | Texto     | FK → Flota.Placa                                                   |
| D   | `Km Recorridos`      | Calculado | `=F-E` (km final − km inicial) · **Denominador del KPI**          |
| E   | `Km Inicial`         | Número    | Lectura del odómetro al salir                                      |
| F   | `Km Final`           | Número    | Lectura del odómetro al regresar                                   |
| G   | `Ruta`               | Texto     | Ruta 1 / Ruta 2 / Ruta 3                                          |
| H   | `Tasa Cambio (L/$)`  | Calculado | `=VLOOKUP(Fecha, 'Tasa de Cambio'!A:B, 2, 0)` — automático       |
| I   | `Km en USD`          | Calculado | `=D/H` — KM expresados en equivalente USD                         |
| J   | `Observaciones`      | Texto     | Incidencias del día                                                |

**Rangos de km por ruta (datos de muestra):**

| Ruta   | Km/día base | Rango real      |
|--------|:-----------:|:---------------:|
| Ruta 1 | 185 km      | 165 – 210 km    |
| Ruta 2 | 160 km      | 140 – 185 km    |
| Ruta 3 | 145 km      | 125 – 170 km    |

---

### 3.5 Combustible — Facturas Diarias

**Propósito:** Registrar cada carga de diésel por bus, con su factura correspondiente. Es el **mayor componente del costo por kilómetro** (~44% del total).

**Volumen:** 240 facturas (1 por bus por día)

**Columnas:**

| Col | Campo                | Tipo      | Fórmula / Regla                                              |
|-----|----------------------|-----------|--------------------------------------------------------------|
| A   | `#`                  | Número    | Correlativo                                                  |
| B   | `Fecha`              | Fecha     | Fecha de la factura (DD/MM/YYYY)                            |
| C   | `Placa / Autobús`    | Texto     | FK → Flota.Placa                                             |
| D   | `No. Factura`        | Texto     | Número único de factura · Patrón: `F-MMDD-NN`               |
| E   | `Litros Cargados`    | Número    | Litros de diésel cargados en esa fecha                       |
| F   | `Costo Total (L)`    | Número    | Monto total de la factura en Lempiras                        |
| G   | `Precio Litro (L)`   | Calculado | `=F/E` — precio por litro                                   |
| H   | `Tasa Cambio (L/$)`  | Calculado | `=VLOOKUP(Fecha, 'Tasa de Cambio'!A:B, 2, 0)`              |
| I   | `Costo Total (USD)`  | Calculado | `=F/H` — equivalente en dólares                             |
| J   | `Km del Día`         | Número    | KM recorridos ese día (referencia manual o del reg. diario) |
| K   | `Rendimiento Km/L`   | Calculado | `=J/E` — eficiencia del bus en km por litro                 |

**Precio de referencia (vigente desde 16/03/2026):**

| Combustible   | Precio (L/galón) | Precio (L/litro) |
|---------------|:----------------:|:----------------:|
| Diésel SPS    | L 96.84          | L 25.59          |
| Regular SPS   | L 98.54          | —                |
| Súper SPS     | L 110.44         | —                |

**Rendimiento promedio de los buses:** 2.5 – 3.2 Km/L (diésel)

---

### 3.6 Precios Combustible — Histórico SEN

**Propósito:** Tabla de referencia con los precios oficiales de combustible en San Pedro Sula desde enero 2023, publicados por la **Secretaría de Energía de Honduras (sen.hn)**. Los precios se actualizan cada ~15 días.

**Volumen:** 85 períodos (02/01/2023 – 16/03/2026)

**Columnas:**

| Col | Campo                | Tipo      | Fórmula / Fuente                        |
|-----|----------------------|-----------|-----------------------------------------|
| A   | `Fecha Vigencia`     | Fecha     | Fecha desde la que rige el nuevo precio |
| B   | `Súper (L/Gal)`      | Número    | Precio oficial gasolina súper           |
| C   | `Regular (L/Gal)`    | Número    | Precio oficial gasolina regular         |
| D   | `Diésel (L/Gal)`     | Número    | **Precio diésel** — referencia principal|
| E   | `Kerosene (L/Gal)`   | Número    | Precio kerosene                         |
| F   | `Diésel (L/Litro)`   | Calculado | `=D/3.785` — conversión galón a litro  |
| G   | `Variación Diésel`   | Calculado | `=D_actual - D_anterior`               |

**Bloque resumen (al final de la hoja):**

```
Precio Diésel Actual  → =D88  (último registro)
Precio Mínimo 2023–26 → =MIN(D4:D200)
Precio Máximo 2023–26 → =MAX(D4:D200)
Precio Promedio       → =AVERAGE(D4:D200)
```

**Tendencia histórica del diésel SPS:**

| Período        | Rango (L/Gal)     | Tendencia  |
|----------------|:-----------------:|:----------:|
| 2023 H1        | L 104.5 – L 124.3 | Bajista    |
| 2023 H2        | L 90.6 – L 106.5  | Alcista    |
| 2024           | L 82.2 – L 102.1  | Bajista    |
| 2025           | L 86.3 – L 100.9  | Alcista    |
| 2026 (a marzo) | L 96.84 (oficial) | Alcista    |

---

### 3.7 Repuestos & Llantas — Facturas de Compras

**Propósito:** Registrar todas las compras de llantas, repuestos y accesorios para la flota, con su categorización y conversión a USD.

**Volumen:** 12 registros (período Feb–Mar 2026)

**Columnas:**

| Col | Campo                  | Tipo      | Fórmula / Regla                                              |
|-----|------------------------|-----------|--------------------------------------------------------------|
| A   | `#`                    | Número    | Correlativo                                                  |
| B   | `Fecha`                | Fecha     | Fecha de compra / factura                                    |
| C   | `Placa / Autobús`      | Texto     | FK → Flota.Placa                                             |
| D   | `No. Factura`          | Texto     | Número de factura del proveedor · Patrón: `RP-NNNN`         |
| E   | `Descripción / Artículo`| Texto    | Nombre del repuesto o llanta                                 |
| F   | `Categoría`            | Lista     | Ver categorías abajo                                         |
| G   | `Cantidad`             | Número    | Unidades compradas                                           |
| H   | `Precio Unitario (L)`  | Número    | Precio por unidad en Lempiras                                |
| I   | `Total (L)`            | Calculado | `=G*H` — total de la línea                                  |
| J   | `Tasa Cambio`          | Calculado | `=VLOOKUP(Fecha, 'Tasa de Cambio'!A:B, 2, 0)`              |
| K   | `Total (USD)`          | Calculado | `=I/J` — equivalente en dólares                             |

**Categorías disponibles (validación de datos):**

```
Llanta | Repuesto Motor | Frenos | Suspensión
Eléctrico | Carrocería | Aceite/Lubricante | Otro
```

**Compras registradas (muestra):**

| Artículo                  | Categoría        | Precio Unit. | Cant. |
|---------------------------|------------------|:------------:|:-----:|
| Llanta 275/70 R22.5       | Llanta           | L 1,950      | 2     |
| Llanta 295/80 R22.5       | Llanta           | L 2,100      | 2     |
| Kit de embrague           | Repuesto Motor   | L 4,500      | 1     |
| Amortiguador delantero    | Suspensión       | L 2,200      | 2     |
| Batería 12V 150Ah         | Eléctrico        | L 3,200      | 1     |
| Pastillas de freno        | Frenos           | L 1,350      | 1     |

---

### 3.8 Personal & Salarios — Planilla Diaria

**Propósito:** Registrar el salario diario de conductor y cobrador asignados a cada bus, generando la planilla diaria automáticamente.

**Volumen:** 240 registros (8 buses × 30 días)

**Columnas:**

| Col | Campo                          | Tipo      | Fórmula / Regla                                              |
|-----|--------------------------------|-----------|--------------------------------------------------------------|
| A   | `#`                            | Número    | Correlativo                                                  |
| B   | `Semana / Fecha`               | Fecha     | Fecha del registro                                           |
| C   | `Placa / Autobús`              | Texto     | FK → Flota.Placa                                             |
| D   | `Conductor`                    | Texto     | Nombre completo del conductor                                |
| E   | `Salario Diario Conductor (L)` | Número    | L 650.00 fijo por día                                        |
| F   | `Cobrador`                     | Texto     | Nombre completo del cobrador                                 |
| G   | `Salario Diario Cobrador (L)`  | Número    | L 450.00 fijo por día                                        |
| H   | `Total Diario (L)`             | Calculado | `=E+G` — total por bus por día (L 1,100.00)                 |
| I   | `Tasa Cambio`                  | Calculado | `=VLOOKUP(Fecha, 'Tasa de Cambio'!A:B, 2, 0)`              |
| J   | `Total Diario (USD)`           | Calculado | `=H/I` — equivalente en dólares                             |
| K   | `Días Trabajados`              | Número    | Días trabajados en el período (1 por registro)              |

**Tarifas salariales registradas:**

| Rol        | Salario Diario | Salario Mensual aprox. (25 días) |
|------------|:--------------:|:--------------------------------:|
| Conductor  | L 650.00       | L 16,250.00                      |
| Cobrador   | L 450.00       | L 11,250.00                      |
| **Total/bus** | **L 1,100.00** | **L 27,500.00**              |

**Personal registrado:**

| Bus       | Conductor         | Cobrador        |
|-----------|-------------------|-----------------|
| ABC-1234  | Juan Pérez        | Carlos López    |
| XYZ-5678  | Mario García      | Luis Torres     |
| DEF-9012  | Pedro Martínez    | José Ramírez    |
| GHI-3456  | Roberto Díaz      | Miguel Flores   |
| JKL-7890  | Andrés Vargas     | David Morales   |
| MNO-2345  | Carlos Reyes      | Oscar Medina    |
| PQR-6789  | Luis Hernández    | Fernando Cruz   |
| STU-0123  | Miguel Álvarez    | Héctor Ruiz     |

---

### 3.9 Seguros — Pólizas y Siniestros

**Propósito:** Registrar pólizas de seguro vigentes y costos por accidentes o siniestros de la flota.

**Volumen:** 10 registros (8 pólizas anuales + 2 siniestros)

**Columnas:**

| Col | Campo           | Tipo      | Fórmula / Regla                                              |
|-----|-----------------|-----------|--------------------------------------------------------------|
| A   | `#`             | Número    | Correlativo                                                  |
| B   | `Fecha`         | Fecha     | Fecha de la póliza o del siniestro                           |
| C   | `Placa`         | Texto     | FK → Flota.Placa                                             |
| D   | `Descripción`   | Texto     | Detalle de la póliza o del evento                            |
| E   | `Tipo`          | Lista     | Ver tipos abajo                                              |
| F   | `Costo (L)`     | Número    | Monto de la prima o del costo del siniestro                  |
| G   | `Tasa Cambio`   | Calculado | `=VLOOKUP(Fecha, 'Tasa de Cambio'!A:B, 2, 0)`              |
| H   | `Costo (USD)`   | Calculado | `=F/G`                                                      |
| I   | `Observaciones` | Texto     | Notas adicionales, fecha de vencimiento                      |

**Tipos de registro (validación de datos):**

```
Póliza Anual | Prima Mensual | Accidente
Robo | Daños a Terceros | Otro
```

**Registros actuales:**

| Tipo          | Descripción                    | Costo (L)   | Buses         |
|---------------|--------------------------------|:-----------:|---------------|
| Póliza Anual  | Todo riesgo 2026               | L 28,000 c/u| Los 8 buses   |
| Accidente     | Colisión con vehículo menor    | L 9,500     | DEF-9012      |
| Daños a Terceros | Acuerdo extrajudicial (moto) | L 4,200     | MNO-2345      |

---

### 3.10 Mantenimiento — Órdenes de Trabajo

**Propósito:** Registrar todos los trabajos de mantenimiento realizados en talleres, desglosando mano de obra y repuestos.

**Volumen:** 10 registros (período Feb–Mar 2026)

**Columnas:**

| Col | Campo                   | Tipo      | Fórmula / Regla                                              |
|-----|-------------------------|-----------|--------------------------------------------------------------|
| A   | `#`                     | Número    | Correlativo                                                  |
| B   | `Fecha`                 | Fecha     | Fecha del trabajo                                            |
| C   | `Placa / Autobús`       | Texto     | FK → Flota.Placa                                             |
| D   | `Taller / Proveedor`    | Texto     | Nombre del taller                                            |
| E   | `Descripción del Trabajo`| Texto    | Detalle de la intervención                                   |
| F   | `Mano de Obra (L)`      | Número    | Costo de la mano de obra                                     |
| G   | `Repuestos (L)`         | Número    | Costo de repuestos usados en el taller                       |
| H   | `Total (L)`             | Calculado | `=F+G`                                                      |
| I   | `Tasa Cambio`           | Calculado | `=VLOOKUP(Fecha, 'Tasa de Cambio'!A:B, 2, 0)`              |
| J   | `Total (USD)`           | Calculado | `=H/I`                                                      |
| K   | `No. Factura`           | Texto     | Número de factura del taller · Patrón: `MT-NNNN`            |

**Talleres registrados:**

| Taller               | Especialidad                    |
|----------------------|---------------------------------|
| Taller Express SPS   | Mantenimiento general           |
| Auto Service Sula    | Frenos, suspensión, eléctrico   |
| Mecánica Diesel HN   | Motor y transmisión diesel      |

---

### 3.11 Tasa de Cambio — Histórico BCH

**Propósito:** Tabla maestra de referencia que almacena la tasa de cambio oficial del Banco Central de Honduras (BCH) para cada día hábil. **Todas las conversiones L → USD del archivo dependen de esta hoja.**

**Volumen:** 840 días hábiles (02/01/2023 – 21/03/2026)

**Columnas:**

| Col | Campo             | Tipo      | Fórmula / Fuente                                            |
|-----|-------------------|-----------|-------------------------------------------------------------|
| A   | `Fecha`           | Fecha     | Fecha del día hábil (lunes a viernes)                       |
| B   | `Tasa (L/$)`      | Número    | Tasa oficial BCH en Lempiras por 1 USD                      |
| C   | `Variación (L)`   | Calculado | `=B_actual - B_anterior`                                   |
| D   | `Variación (%)`   | Calculado | `=(B_actual - B_anterior) / B_anterior`                    |
| E   | `Fuente`          | Texto     | `BCH` (Banco Central de Honduras)                           |
| F   | `Notas`           | Texto     | Observaciones (feriados, eventos cambiarios)                |

**Bloque de estadísticas (al final de la hoja):**

```
Tasa Actual   → =LOOKUP(2, 1/(B4:B369<>""), B4:B369)  ← último valor no vacío
Tasa Mínima   → =MIN(B4:B369)
Tasa Máxima   → =MAX(B4:B369)
Tasa Promedio → =AVERAGEIF(B4:B369, ">0")
```

**Evolución histórica:**

| Año  | Tasa inicio (L/$) | Tasa fin (L/$) | Variación anual |
|------|:-----------------:|:--------------:|:---------------:|
| 2023 | L 26.50           | L 26.90        | +1.5%           |
| 2024 | L 26.90           | L 27.50        | +2.2%           |
| 2025 | L 27.50           | L 28.20        | +2.5%           |
| 2026 | L 28.20           | L 28.41 (mar.) | +0.7% (parcial) |

**Fórmula de lookup usada en todas las hojas transaccionales:**

```excel
=IFERROR(VLOOKUP(Fecha, 'Tasa de Cambio'!A:B, 2, 0), 28.40)
```
> El valor `28.40` es el fallback si no existe la tasa del día.

---

### 3.12 Ingresos 🔒 — Recaudación Diaria (CONFIDENCIAL)

**Propósito:** Registrar la recaudación diaria por pasajeros de cada bus en su ruta. Esta hoja es **confidencial** y solo debe ser accedida por gerencia para el análisis de rentabilidad.

**Volumen:** 240 registros (8 buses × 30 días)

**Columnas:**

| Col | Campo                  | Tipo      | Fórmula / Regla                                              |
|-----|------------------------|-----------|--------------------------------------------------------------|
| A   | `#`                    | Número    | Correlativo                                                  |
| B   | `Fecha`                | Fecha     | Fecha del registro                                           |
| C   | `Placa / Autobús`      | Texto     | FK → Flota.Placa                                             |
| D   | `Ruta`                 | Texto     | Ruta 1 / Ruta 2 / Ruta 3                                   |
| E   | `No. Pasajeros`        | Número    | Total pasajeros transportados en el día                      |
| F   | `Tarifa (L)`           | Número    | Precio del pasaje según la ruta                             |
| G   | `Ingreso Bruto (L)`    | Calculado | `=E*F` — recaudación total del día                          |
| H   | `Tasa Cambio`          | Calculado | `=VLOOKUP(Fecha, 'Tasa de Cambio'!A:B, 2, 0)`              |
| I   | `Ingreso Bruto (USD)`  | Calculado | `=G/H`                                                      |
| J   | `Observaciones`        | Texto     | Feriados, huelgas, eventos especiales                        |

**Tarifas por ruta:**

| Ruta   | Tarifa (L) | Pasajeros típicos/día |
|--------|:----------:|:---------------------:|
| Ruta 1 | L 12.00    | ~100 – 125            |
| Ruta 2 | L 10.00    | ~75 – 95              |
| Ruta 3 | L 11.00    | ~85 – 100             |

---

### 3.13 Rentabilidad 🔒 — Análisis de Margen (CONFIDENCIAL)

**Propósito:** Calcular la rentabilidad neta por ruta comparando ingresos totales con costos totales. Esta hoja es el **indicador final de la salud financiera del negocio**.

**Estructura:**

| Col | Campo                  | Fórmula                                                                                           |
|-----|------------------------|---------------------------------------------------------------------------------------------------|
| B   | `Ruta / Autobús`       | Ruta 1 / Ruta 2 / Ruta 3 / TOTAL FLOTA                                                          |
| C   | `Ingresos Totales (L)` | `=SUMIF('Ingresos 🔒'!D:D, Ruta, 'Ingresos 🔒'!G:G)`                                           |
| D   | `Costos Totales (L)`   | `=SUMIF('Registro Diario'!G:G, Ruta, 'Combustible'!F:F) + SUMIF(...'Repuestos & Llantas'!I:I)` |
| E   | `Utilidad Bruta (L)`   | `=C-D`                                                                                            |
| F   | `Margen %`             | `=IFERROR(E/C, 0)`                                                                                |
| G   | `Costo $/KM`           | `=IFERROR(VLOOKUP(Ruta, '📊 KPIs'!B13:K17, 10, 0), "-")`  ← trae el KPI de la hoja principal  |

---

## 4. Relaciones entre Hojas

### Diagrama completo de relaciones

```
                    ┌─────────────────┐
                    │  Tasa de Cambio │
                    │  (A:B)          │
                    │  Fecha → Tasa   │
                    └────────┬────────┘
                             │ VLOOKUP por Fecha
           ┌─────────────────┼─────────────────┐
           │                 │                  │
           ▼                 ▼                  ▼
  ┌────────────────┐  ┌──────────────┐  ┌──────────────────┐
  │ Combustible    │  │ Repuestos &  │  │ Personal &       │
  │ Col H: Tasa    │  │ Llantas      │  │ Salarios         │
  │ Col I: USD     │  │ Col J: Tasa  │  │ Col I: Tasa      │
  └───────┬────────┘  │ Col K: USD   │  │ Col J: USD       │
          │           └──────┬───────┘  └────────┬─────────┘
          │                  │                   │
          │  ┌───────────────┘                   │
          │  │         ┌─────────────────────────┘
          │  │         │
          ▼  ▼         ▼
  ┌─────────────────────────────────────────┐
  │              📊 KPIs                    │
  │                                         │
  │  SUMIF(Placa → Combustible!F)           │
  │  SUMIF(Placa → Repuestos!I)             │
  │  SUMIF(Placa → Salarios!H)  → L/KM     │
  │  SUMIF(Placa → Seguros!F)               │
  │  SUMIF(Placa → Mantenimiento!H)         │
  │                                         │
  │  Denominador: SUMIF(Placa → Reg.Diario!D)│
  └────────────────────┬────────────────────┘
                       │ VLOOKUP(Ruta, KPIs!B13:K17, 10)
                       ▼
  ┌────────────────────────────┐
  │     Rentabilidad 🔒        │
  │                            │
  │  Ingresos: SUMIF(Ruta →   │
  │    Ingresos🔒!G)           │
  │  Costos: SUMIF(Ruta →     │
  │    múltiples hojas)        │
  │  Margen: Ingresos - Costos │
  └────────────────────────────┘

  ┌────────────────────────┐
  │ Flota (Catálogo)       │   ← Referencia visual / navegación
  │ Placa (clave única)    │   ← NO tiene VLOOKUP hacia ella
  └────────────────────────┘   (la Placa se ingresa manualmente)

  ┌────────────────────────┐
  │ Precios Combustible    │   ← Referencia para alertas manuales
  │ (sin fórmulas hacia    │   (no hay VLOOKUP automático desde
  │  otras hojas)          │    otras hojas, es consulta visual)
  └────────────────────────┘
```

### Tabla de dependencias por hoja

| Hoja origen        | Hoja destino  | Campo de unión  | Función usada  | Propósito                              |
|--------------------|---------------|:---------------:|:--------------:|----------------------------------------|
| Registro Diario    | Tasa de Cambio| `Fecha`         | `VLOOKUP`      | Obtener tasa del día para convertir    |
| Combustible        | Tasa de Cambio| `Fecha`         | `VLOOKUP`      | Convertir costo L → USD                |
| Repuestos & Llantas| Tasa de Cambio| `Fecha`         | `VLOOKUP`      | Convertir total L → USD                |
| Personal & Salarios| Tasa de Cambio| `Fecha`         | `VLOOKUP`      | Convertir salario L → USD              |
| Seguros            | Tasa de Cambio| `Fecha`         | `VLOOKUP`      | Convertir costo L → USD                |
| Mantenimiento      | Tasa de Cambio| `Fecha`         | `VLOOKUP`      | Convertir total L → USD                |
| Ingresos 🔒        | Tasa de Cambio| `Fecha`         | `VLOOKUP`      | Convertir ingreso L → USD              |
| 📊 KPIs            | Registro Diario| `Placa`        | `SUMIF`        | Sumar km recorridos por bus            |
| 📊 KPIs            | Combustible   | `Placa`         | `SUMIF`        | Sumar costo combustible por bus        |
| 📊 KPIs            | Repuestos     | `Placa`         | `SUMIF`        | Sumar costo repuestos por bus          |
| 📊 KPIs            | Personal      | `Placa`         | `SUMIF`        | Sumar costo salarios por bus           |
| 📊 KPIs            | Seguros       | `Placa`         | `SUMIF`        | Sumar costo seguros por bus            |
| 📊 KPIs            | Mantenimiento | `Placa`         | `SUMIF`        | Sumar costo mantenimiento por bus      |
| Rentabilidad 🔒    | Ingresos 🔒   | `Ruta`          | `SUMIF`        | Sumar ingresos por ruta                |
| Rentabilidad 🔒    | 📊 KPIs       | `Ruta`          | `VLOOKUP`      | Traer KPI $/KM calculado por ruta      |

---

## 5. Fórmulas Clave y Lógica de Cálculo

### 5.1 Cálculo del KPI principal L/KM

```
                    Σ Combustible (L)
                  + Σ Repuestos (L)
                  + Σ Salarios (L)          ← SUMIF por Placa
                  + Σ Seguros (L)
                  + Σ Mantenimiento (L)
L/KM por bus = ──────────────────────────
                    Σ Km Recorridos          ← SUMIF por Placa
```

**En Excel (ejemplo para bus ABC-1234):**

```excel
KPI = ( SUMIF(Combustible!C:C, "ABC-1234", Combustible!F:F)
      + SUMIF('Repuestos & Llantas'!C:C, "ABC-1234", 'Repuestos & Llantas'!I:I)
      + SUMIF('Personal & Salarios'!C:C, "ABC-1234", 'Personal & Salarios'!H:H)
      + SUMIF(Seguros!C:C, "ABC-1234", Seguros!F:F)
      + SUMIF(Mantenimiento!C:C, "ABC-1234", Mantenimiento!H:H)
      ) / SUMIF('Registro Diario'!C:C, "ABC-1234", 'Registro Diario'!D:D)
```

### 5.2 Conversión de moneda (patrón uniforme)

```excel
=IFERROR(VLOOKUP(B{fila}, 'Tasa de Cambio'!A:B, 2, 0), 28.40)
```

- Busca la **tasa del mismo día** que el registro
- Si no encuentra la fecha, usa `28.40` como valor de respaldo
- Se aplica en todas las hojas transaccionales (columna `Tasa Cambio`)

### 5.3 Rendimiento de combustible

```excel
Rendimiento (Km/L) = Km del Día / Litros Cargados
                   = Combustible!J / Combustible!E
```

### 5.4 Precio por litro de diésel (desde histórico SEN)

```excel
Diésel (L/Litro) = Diésel (L/Galón) / 3.785
                 = 'Precios Combustible'!D / 3.785
```

### 5.5 Rentabilidad por ruta

```excel
Utilidad Bruta = Ingresos Totales - Costos Totales
               = SUMIF('Ingresos 🔒'!D:D, Ruta, 'Ingresos 🔒'!G:G)
               - SUMIF(múltiples hojas por Ruta)

Margen % = Utilidad Bruta / Ingresos Totales
```

### 5.6 Variación de tasa de cambio

```excel
Variación (L)  = Tasa_hoy - Tasa_ayer     → 'Tasa de Cambio'!C
Variación (%)  = (Tasa_hoy - Tasa_ayer) / Tasa_ayer → 'Tasa de Cambio'!D
```

---

## 6. Estructura de Base de Datos Equivalente

El siguiente esquema representa cómo se migraría el archivo Excel a una base de datos relacional normalizada.

### Diagrama entidad-relación

```
┌───────────────┐         ┌───────────────────┐
│     buses     │         │   tasa_cambio     │
├───────────────┤         ├───────────────────┤
│ PK placa      │         │ PK id             │
│    marca      │         │ UK fecha          │
│    modelo     │         │    tasa_lempira   │
│    color      │         │    variacion      │
│    anio       │         │    variacion_pct  │
│    llanta     │         │    fuente         │
│    ruta       │         └────────┬──────────┘
│    estado     │                  │ FK tasa_id
└──────┬────────┘                  │
       │ FK bus_id (placa)         │
       │    ┌──────────────────────┘
       │    │
       ▼    ▼
┌─────────────────────┐    ┌─────────────────────┐
│   registro_diario   │    │    combustible       │
├─────────────────────┤    ├─────────────────────┤
│ PK id               │    │ PK id               │
│ FK bus_id           │    │ FK bus_id           │
│ FK tasa_id          │    │ FK tasa_id          │
│    fecha            │    │    fecha            │
│    km_inicial       │    │    no_factura       │
│    km_final         │    │    litros           │
│    km_recorridos*   │    │    costo_total_l    │
│    ruta             │    │    precio_litro_l*  │
└─────────────────────┘    │    km_dia           │
                            │    rendimiento*     │
                            └─────────────────────┘

┌──────────────────┐   ┌──────────────────┐   ┌──────────────────┐
│    repuestos     │   │    salarios      │   │    seguros       │
├──────────────────┤   ├──────────────────┤   ├──────────────────┤
│ PK id            │   │ PK id            │   │ PK id            │
│ FK bus_id        │   │ FK bus_id        │   │ FK bus_id        │
│ FK tasa_id       │   │ FK tasa_id       │   │ FK tasa_id       │
│    fecha         │   │    fecha         │   │    fecha         │
│    no_factura    │   │    conductor     │   │    tipo          │
│    descripcion   │   │    sal_cond_l    │   │    descripcion   │
│    categoria     │   │    cobrador      │   │    costo_l       │
│    cantidad      │   │    sal_cob_l     │   │    vencimiento   │
│    precio_u_l    │   │    total_l*      │   └──────────────────┘
│    total_l*      │   └──────────────────┘
└──────────────────┘

┌──────────────────┐   ┌──────────────────────┐   ┌────────────────────┐
│  mantenimiento   │   │ precios_combustible  │   │   ingresos 🔒      │
├──────────────────┤   ├──────────────────────┤   ├────────────────────┤
│ PK id            │   │ PK id                │   │ PK id              │
│ FK bus_id        │   │    fecha_vigencia    │   │ FK bus_id          │
│ FK tasa_id       │   │    super_l           │   │ FK tasa_id         │
│    fecha         │   │    regular_l         │   │    fecha           │
│    taller        │   │    diesel_l          │   │    ruta            │
│    descripcion   │   │    kerosene_l        │   │    pasajeros       │
│    mano_obra_l   │   │    diesel_litro*     │   │    tarifa_l        │
│    repuestos_l   │   │    fuente            │   │    ingreso_bruto_l*│
│    total_l*      │   └──────────────────────┘   └────────────────────┘
│    no_factura    │
└──────────────────┘

* campo calculado (GENERATED ALWAYS AS en PostgreSQL)
```

### DDL — Tablas principales

```sql
-- Tabla maestra de buses (equivalente a hoja Flota)
CREATE TABLE buses (
  placa       VARCHAR(20)  PRIMARY KEY,
  marca       VARCHAR(100),
  modelo      VARCHAR(100),
  color       VARCHAR(60),
  anio        SMALLINT,
  llanta      VARCHAR(30),
  ruta        VARCHAR(20)  CHECK (ruta IN ('Ruta 1','Ruta 2','Ruta 3')),
  estado      VARCHAR(30)  DEFAULT 'Activo'
                           CHECK (estado IN ('Activo','Inactivo','En Mantenimiento','Baja')),
  conductor   VARCHAR(150),
  cobrador    VARCHAR(150),
  f_ult_mant  DATE,
  obs         TEXT
);

-- Tasa de cambio BCH (equivalente a hoja Tasa de Cambio)
CREATE TABLE tasa_cambio (
  id             SERIAL       PRIMARY KEY,
  fecha          DATE         UNIQUE NOT NULL,
  tasa_l_usd     DECIMAL(10,4) NOT NULL,
  variacion_l    DECIMAL(10,4),
  variacion_pct  DECIMAL(8,6),
  fuente         VARCHAR(10)  DEFAULT 'BCH'
);

-- Registro diario de kilometraje (equivalente a hoja Registro Diario)
CREATE TABLE registro_diario (
  id              SERIAL   PRIMARY KEY,
  fecha           DATE     NOT NULL,
  bus_placa       VARCHAR(20) REFERENCES buses(placa),
  km_inicial      INT      NOT NULL,
  km_final        INT      NOT NULL,
  km_recorridos   INT      GENERATED ALWAYS AS (km_final - km_inicial) STORED,
  ruta            VARCHAR(20),
  tasa_id         INT      REFERENCES tasa_cambio(id),
  observaciones   TEXT,
  UNIQUE (fecha, bus_placa)
);

-- Facturas de combustible (equivalente a hoja Combustible)
CREATE TABLE combustible (
  id             SERIAL        PRIMARY KEY,
  fecha          DATE          NOT NULL,
  bus_placa      VARCHAR(20)   REFERENCES buses(placa),
  no_factura     VARCHAR(30)   UNIQUE,
  litros         DECIMAL(10,2),
  costo_total_l  DECIMAL(12,2),
  precio_litro_l DECIMAL(10,4) GENERATED ALWAYS AS
                   (costo_total_l / NULLIF(litros, 0)) STORED,
  km_dia         INT,
  rendimiento_kml DECIMAL(8,4) GENERATED ALWAYS AS
                   (km_dia::DECIMAL / NULLIF(litros, 0)) STORED,
  tasa_id        INT           REFERENCES tasa_cambio(id)
);

-- Precios combustible SEN (equivalente a hoja Precios Combustible)
CREATE TABLE precios_combustible (
  id              SERIAL        PRIMARY KEY,
  fecha_vigencia  DATE          UNIQUE NOT NULL,
  super_l         DECIMAL(10,2),
  regular_l       DECIMAL(10,2),
  diesel_l        DECIMAL(10,2),
  kerosene_l      DECIMAL(10,2),
  diesel_litro    DECIMAL(10,4) GENERATED ALWAYS AS (diesel_l / 3.785) STORED,
  fuente          VARCHAR(30)   DEFAULT 'sen.hn'
);

-- Repuestos y llantas (equivalente a hoja Repuestos & Llantas)
CREATE TABLE repuestos (
  id          SERIAL        PRIMARY KEY,
  fecha       DATE          NOT NULL,
  bus_placa   VARCHAR(20)   REFERENCES buses(placa),
  no_factura  VARCHAR(30),
  descripcion TEXT,
  categoria   VARCHAR(40)   CHECK (categoria IN (
                'Llanta','Repuesto Motor','Frenos','Suspensión',
                'Eléctrico','Carrocería','Aceite/Lubricante','Otro')),
  cantidad    INT,
  precio_u_l  DECIMAL(12,2),
  total_l     DECIMAL(14,2) GENERATED ALWAYS AS (cantidad * precio_u_l) STORED,
  tasa_id     INT           REFERENCES tasa_cambio(id)
);

-- Salarios diarios (equivalente a hoja Personal & Salarios)
CREATE TABLE salarios (
  id           SERIAL        PRIMARY KEY,
  fecha        DATE          NOT NULL,
  bus_placa    VARCHAR(20)   REFERENCES buses(placa),
  conductor    VARCHAR(150),
  sal_cond_l   DECIMAL(10,2) DEFAULT 650.00,
  cobrador     VARCHAR(150),
  sal_cob_l    DECIMAL(10,2) DEFAULT 450.00,
  total_l      DECIMAL(12,2) GENERATED ALWAYS AS (sal_cond_l + sal_cob_l) STORED,
  tasa_id      INT           REFERENCES tasa_cambio(id),
  UNIQUE (fecha, bus_placa)
);

-- Seguros y siniestros (equivalente a hoja Seguros)
CREATE TABLE seguros (
  id               SERIAL        PRIMARY KEY,
  fecha            DATE          NOT NULL,
  bus_placa        VARCHAR(20)   REFERENCES buses(placa),
  tipo             VARCHAR(40)   CHECK (tipo IN (
                     'Póliza Anual','Prima Mensual','Accidente',
                     'Robo','Daños a Terceros','Otro')),
  descripcion      TEXT,
  costo_l          DECIMAL(14,2),
  fecha_vencimiento DATE,
  tasa_id          INT           REFERENCES tasa_cambio(id),
  observaciones    TEXT
);

-- Mantenimiento (equivalente a hoja Mantenimiento)
CREATE TABLE mantenimiento (
  id           SERIAL        PRIMARY KEY,
  fecha        DATE          NOT NULL,
  bus_placa    VARCHAR(20)   REFERENCES buses(placa),
  taller       VARCHAR(150),
  descripcion  TEXT,
  mano_obra_l  DECIMAL(12,2),
  repuestos_l  DECIMAL(12,2),
  total_l      DECIMAL(14,2) GENERATED ALWAYS AS (mano_obra_l + repuestos_l) STORED,
  no_factura   VARCHAR(30),
  tasa_id      INT           REFERENCES tasa_cambio(id)
);

-- Ingresos confidenciales (equivalente a hoja Ingresos 🔒)
CREATE TABLE ingresos (
  id              SERIAL        PRIMARY KEY,
  fecha           DATE          NOT NULL,
  bus_placa       VARCHAR(20)   REFERENCES buses(placa),
  ruta            VARCHAR(20),
  pasajeros       INT,
  tarifa_l        DECIMAL(8,2),
  ingreso_bruto_l DECIMAL(14,2) GENERATED ALWAYS AS (pasajeros * tarifa_l) STORED,
  tasa_id         INT           REFERENCES tasa_cambio(id),
  observaciones   TEXT,
  UNIQUE (fecha, bus_placa)
);
-- Activar seguridad a nivel fila:
ALTER TABLE ingresos ENABLE ROW LEVEL SECURITY;
```

### Vista SQL equivalente al KPI principal

```sql
-- Vista: costo L/KM por bus (equivalente a la hoja 📊 KPIs)
CREATE VIEW v_kpi_por_bus AS
SELECT
  b.placa,
  b.ruta,
  SUM(rd.km_recorridos)                              AS km_total,
  SUM(c.costo_total_l)                               AS costo_combustible_l,
  SUM(r.total_l)                                     AS costo_repuestos_l,
  SUM(s.total_l)                                     AS costo_salarios_l,
  SUM(seg.costo_l)                                   AS costo_seguros_l,
  SUM(m.total_l)                                     AS costo_mantenimiento_l,
  (SUM(c.costo_total_l) + SUM(r.total_l) + SUM(s.total_l)
   + SUM(seg.costo_l)  + SUM(m.total_l))             AS costo_total_l,
  ROUND(
    (SUM(c.costo_total_l) + SUM(r.total_l) + SUM(s.total_l)
     + SUM(seg.costo_l)  + SUM(m.total_l))
    / NULLIF(SUM(rd.km_recorridos), 0), 4
  )                                                   AS lempiras_por_km  -- KPI
FROM buses b
LEFT JOIN registro_diario rd ON rd.bus_placa = b.placa
LEFT JOIN combustible      c  ON c.bus_placa  = b.placa
LEFT JOIN repuestos        r  ON r.bus_placa  = b.placa
LEFT JOIN salarios         s  ON s.bus_placa  = b.placa
LEFT JOIN seguros         seg ON seg.bus_placa = b.placa
LEFT JOIN mantenimiento    m  ON m.bus_placa  = b.placa
GROUP BY b.placa, b.ruta
ORDER BY lempiras_por_km ASC;
```

---

## 7. Roles y Permisos

El archivo no implementa control de acceso técnico (es un Excel), pero se establecen los siguientes **acuerdos organizacionales** sobre quién debe acceder y modificar cada hoja.

### Definición de perfiles

| Perfil       | Descripción                                                          |
|--------------|----------------------------------------------------------------------|
| **Admin**    | Administrador del sistema. Acceso total. Responsable de la integridad del archivo. |
| **Operador** | Personal operativo. Ingresa datos diarios (km, combustible, facturas). |
| **Gerencia** | Dirección de la empresa. Acceso de solo lectura al KPI y acceso total al módulo confidencial. |

### Matriz de permisos por hoja

| Hoja                     | Admin        | Operador         | Gerencia         | Observaciones                                 |
|--------------------------|:------------:|:----------------:|:----------------:|-----------------------------------------------|
| 🏠 Portada               | ✅ Ver       | ✅ Ver           | ✅ Ver           | Solo navegación, no se edita                  |
| 📊 KPIs                  | ✅ Ver       | ✅ Ver           | ✅ Ver           | Solo lectura — fórmulas automáticas           |
| Flota                    | ✅ CRUD      | ✏️ Editar asign. | 👁️ Solo ver     | Operador puede cambiar conductor/cobrador      |
| Registro Diario          | ✅ CRUD      | ✅ CRUD          | 👁️ Solo ver     | Ingreso diario por operador                   |
| Combustible              | ✅ CRUD      | ✅ CRUD          | 👁️ Solo ver     | Ingreso diario por operador                   |
| Precios Combustible      | ✅ Editar    | 👁️ Solo ver     | 👁️ Solo ver     | Actualizar cuando SEN publica nuevos precios   |
| Repuestos & Llantas      | ✅ CRUD      | ✅ CRUD          | 👁️ Solo ver     | Registrar facturas de compra                  |
| Personal & Salarios      | ✅ CRUD      | ❌ Sin acceso    | 👁️ Solo ver     | Datos salariales — acceso restringido         |
| Seguros                  | ✅ CRUD      | 👁️ Solo ver     | 👁️ Solo ver     | Admin registra pólizas y siniestros           |
| Mantenimiento            | ✅ CRUD      | ✏️ Crear         | 👁️ Solo ver     | Operador puede crear OT, Admin aprueba        |
| Tasa de Cambio           | ✅ Editar    | 👁️ Solo ver     | 👁️ Solo ver     | Actualizar diariamente con dato BCH           |
| 🔒 Ingresos              | ✅ CRUD      | ❌ Sin acceso    | ✅ CRUD          | **CONFIDENCIAL** — solo Admin y Gerencia      |
| 🔒 Rentabilidad          | ✅ Ver       | ❌ Sin acceso    | ✅ Ver           | **CONFIDENCIAL** — fórmulas automáticas       |

### Leyenda de permisos

```
✅ CRUD    → Crear, Leer, Editar y Eliminar filas
✅ Ver     → Puede abrir y consultar la hoja
✏️ Editar  → Puede editar solo campos específicos
👁️ Solo ver → Puede ver pero NO modificar
❌ Sin acceso → No debe abrir esta hoja
```

### Recomendaciones de protección

Para implementar los permisos en Excel:

```
1. Proteger hojas confidenciales con contraseña:
   → Ingresos 🔒   : contraseña conocida solo por Admin y Gerencia
   → Rentabilidad 🔒: misma contraseña

2. Proteger hojas de fórmulas con contraseña (sin restricción de lectura):
   → 📊 KPIs       : contraseña para que nadie rompa las fórmulas
   → Tasa de Cambio: proteger columnas de fórmulas (C, D)

3. Para hojas operativas (Registro Diario, Combustible, etc.):
   → Bloquear celdas de encabezado y fórmulas
   → Permitir edición solo en columnas de ingreso de datos

4. Usar "Proteger libro" para impedir mover / eliminar hojas
```

---

## 8. Convenciones de Formato y Color

### Colores de pestaña (tabs)

| Color hex  | Hojas                          | Significado            |
|:----------:|--------------------------------|------------------------|
| `#0D1B2A`  | 🏠 Portada                     | Portada / Navegación   |
| `#404040`  | 📊 KPIs                        | Dashboard principal    |
| `#2E75B6`  | Flota                          | Datos maestros         |
| `#16712A`  | Registro Diario                | Operación diaria       |
| `#ED7D31`  | Combustible                    | Combustible            |
| `#B85C00`  | Precios Combustible            | Precios históricos     |
| `#7030A0`  | Repuestos & Llantas            | Compras                |
| `#375623`  | Personal & Salarios            | RRHH                   |
| `#C00000`  | Seguros                        | Riesgo / Alertas       |
| `#595959`  | Mantenimiento                  | Técnico                |
| `#1F3864`  | Tasa de Cambio                 | Financiero             |
| `#7B2D00`  | Ingresos 🔒 · Rentabilidad 🔒  | Confidencial           |

### Colores de celda por tipo de dato

| Color de fondo | Hex       | Significado                                        |
|:--------------:|:---------:|----------------------------------------------------|
| Azul oscuro    | `#1F3864` | Encabezados principales de sección                 |
| Azul medio     | `#2E75B6` | Encabezados de columnas en hojas de datos          |
| Azul claro     | `#DEEAF1` | Filas pares (alternancia) en hojas azules          |
| Verde claro    | `#E2EFDA` | Filas pares en Registro Diario y Personal          |
| Amarillo claro | `#FFF2CC` | Totales, celdas de atención · Hojas confidenciales |
| Dorado         | `#C9A227` | Acento dorado en portada                           |
| Rojo claro     | `#FCE4D6` | Filas pares en Seguros                             |
| Morado claro   | `#EAD1DC` | Filas pares en Repuestos                           |
| Gris claro     | `#F2F2F2` | Filas pares en Mantenimiento                       |
| Blanco         | `#FFFFFF` | Filas impares (contraste)                          |

### Convenciones numéricas

| Dato            | Formato Excel         | Ejemplo              |
|-----------------|-----------------------|----------------------|
| Moneda Lempiras | `"L"#,##0.00`         | L 2,034.50           |
| Moneda USD      | `"$"#,##0.00`         | $71.60               |
| Kilómetros      | `#,##0"KM"`           | 205KM                |
| Rendimiento     | `#,##0.00"Km/L"`      | 2.58Km/L             |
| Tasa de cambio  | `L#,##0.0000`         | L28.4100             |
| Porcentaje      | `0.0%`                | 12.5%                |
| KPI $/KM        | `"L"#,##0.0000`       | L8.4231              |

---

## 9. Glosario

| Término          | Definición                                                                               |
|------------------|------------------------------------------------------------------------------------------|
| **KPI**          | Key Performance Indicator — Indicador Clave de Desempeño                                |
| **L/KM**         | Lempiras por Kilómetro — métrica central del sistema                                    |
| **BCH**          | Banco Central de Honduras — fuente oficial de la tasa de cambio                         |
| **SEN**          | Secretaría de Energía de Honduras — fuente oficial de precios de combustible             |
| **Placa**        | Identificador único del vehículo — clave primaria en el modelo de datos                 |
| **Ruta**         | Trayecto fijo asignado a cada bus (Ruta 1, Ruta 2, Ruta 3)                             |
| **Odómetro**     | Instrumento que mide la distancia recorrida. Se registra al salir (inicial) y regresar  |
| **VLOOKUP**      | Función Excel para buscar la tasa de cambio por fecha en la hoja Tasa de Cambio         |
| **SUMIF**        | Función Excel para sumar costos filtrando por placa del bus en la hoja KPIs             |
| **Fallback**     | Valor por defecto (L 28.40) usado si no se encuentra la tasa del día en el VLOOKUP      |
| **OT**           | Orden de Trabajo — registro de mantenimiento en un taller                               |
| **Siniestro**    | Evento de daños cubierto o no por la póliza de seguro (accidente, robo, etc.)          |
| **RRHH**         | Recursos Humanos — componente de salarios en el costo total                             |
| **RLS**          | Row Level Security — seguridad a nivel de fila en bases de datos relacionales           |
| **Confidencial** | Hojas Ingresos y Rentabilidad — acceso restringido exclusivo a Gerencia y Admin         |

---

*Documentación generada automáticamente a partir de la estructura de `KPI_Costo_Kilometro_Buses_v2.xlsx`*  
*RouteMaster KPI · San Pedro Sula, Honduras · Abril 2026*