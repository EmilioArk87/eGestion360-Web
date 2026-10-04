# Flujo de Operación Diaria — Módulo Flota

## Estructura del Módulo

```
/Flota
├── /Catalogos          ← Datos maestros (vehículos, rutas, personas, talleres)
│   └── /Vehiculos      ← CRUD de vehículos
├── /Operacion          ← Registros del día a día
│   ├── /ControlSalidas ← Salidas y entradas en garita (fuente de los KM)
│   ├── /CargasCombustible ← Cargas de combustible
│   └── /SalariosDiarios   ← Salarios de conductores y cobradores
└── /Gastos             ← Gastos periódicos (no diarios)
    ├── /Mantenimiento  ← Órdenes de taller
    ├── /Repuestos      ← Compra de repuestos
    └── /Seguros        ← Pólizas de seguro
```

---

## Prerequisito: Datos maestros

Antes de registrar operación diaria, deben existir en el sistema:

| Catálogo | Tabla BD | Mínimo requerido |
|----------|----------|------------------|
| Vehículos | `vehiculos` | Al menos 1 vehículo activo |
| Rutas | `rutas` | Opcional (destino sugerido en garita) |
| Personas | `personas` | Al menos 1 persona activa con cargo CONDUCTOR |
| Talleres | `talleres` | Al menos 1 taller activo (para mantenimiento) |
| Categorías de repuesto | `categorias_repuesto` | Al menos 1 categoría activa |

Todos los selects en formularios filtran por `id_empresa` de la sesión y `activo = true`.

---

## 1. Control de Salidas y Entradas (kilómetros)

**URL:** `/Flota/Operacion/ControlSalidas`  
**Tabla BD:** `control_salidas`  
**Propósito:** Registrar cada cruce del portón con su odómetro. Los km de cada viaje
(`km_recorridos = odometro_entrada - odometro_salida`) son el denominador del KPI.
Detalle de la pantalla en `Vistas/Flota_ControlSalidas.md`.

### Odómetro Diario (retirado 2026-09-27)

La captura manual en `/Flota/Operacion/OdometroDiario` se eliminó: los km salen ahora de
la garita. La tabla `odometro_diario` **se conserva como histórico** (8.066 filas al
2026-09-27, incluidas las de 2019–2021 de Transgar) y se sigue usando en dos lugares:

- **KPI:** para cada vehículo y día, si hay viajes cerrados en garita se usan esos km; si no,
  los de `odometro_diario`. Así los períodos viejos siguen calculando y no se cuenta dos veces
  el mismo día.
- **Garita:** la última lectura conocida del vehículo es la mayor entre `control_salidas` y
  `odometro_diario.km_final`.

---

## 2. Cargas de Combustible

**URL:** `/Flota/Operacion/CargasCombustible`  
**Tabla BD:** `cargas_combustible`  
**Propósito:** Registrar cada carga de combustible con su factura y costo.

### Flujo de registro

```
Operador accede a /Flota/Operacion/CargasCombustible/Create
        │
   Fecha se pre-llena con hoy; Moneda pre-llenada con HNL
        │
   Selecciona: Vehículo (obligatorio)
   Ingresa:    Nº Factura (obligatorio), Proveedor (opcional)
   Selecciona: Tipo combustible (DIESEL por defecto)
   Ingresa:    Cantidad en galones (GAL), Precio unitario, Moneda
   Ingresa:    KM odómetro al momento de la carga (opcional)
   Selecciona: Conductor (opcional)
   Ingresa:    Hora, Observaciones (ambos opcionales)
        │
        ▼
   Sistema calcula Total = Cantidad × Precio unitario (columna computada en BD)
   Registra: id_empresa, creado_por, fecha_creacion
        │
        ▼
   Guarda en `cargas_combustible` → redirige al listado
```

### Campos del formulario

| Campo | Obligatorio | Valor por defecto |
|-------|-------------|-------------------|
| Vehículo | Sí | — |
| Fecha | Sí | Hoy |
| Hora | No | — |
| Nº Factura | Sí | — |
| Proveedor | No | — |
| Tipo combustible | Sí | DIESEL |
| Unidad de medida | Sí | GAL |
| Cantidad | Sí | — (> 0) |
| Precio unitario | Sí | — (>= 0) |
| Moneda | Sí | HNL |
| KM odómetro | No | — |
| Conductor | No | — |
| Observaciones | No | — |

### Campo calculado en BD

`total = cantidad × precio_unitario` (columna computada).

---

## 3. Salarios Diarios

**URL:** `/Flota/Operacion/SalariosDiarios`  
**Tabla BD:** `salarios_diarios`  
**Propósito:** Registrar el pago diario al conductor o cobrador asignado a un vehículo.

### Flujo de registro

```
Operador accede a /Flota/Operacion/SalariosDiarios/Create
        │
   Fecha se pre-llena con hoy; Moneda pre-llenada con HNL
        │
   Selecciona: Vehículo (obligatorio)
   Selecciona: Persona — conductor o cobrador (obligatorio)
   Ingresa:    Fecha (obligatorio)
   Selecciona: Cargo (CONDUCTOR por defecto)
   Ingresa:    Monto (obligatorio), Moneda
   Ingresa:    Observaciones (opcional)
        │
        ▼
   Registra: id_empresa, creado_por, fecha_creacion
        │
        ▼
   Guarda en `salarios_diarios` → redirige al listado
```

### Campos del formulario

| Campo | Obligatorio | Valor por defecto |
|-------|-------------|-------------------|
| Vehículo | Sí | — |
| Persona | Sí | Lista del personal activo de la empresa (vínculo de empleado vigente) |
| Fecha | Sí | Hoy |
| Cargo | Sí | CONDUCTOR |
| Monto | Sí | — (>= 0) |
| Moneda | Sí | HNL |
| Observaciones | No | — |

---

## 4. Órdenes de Mantenimiento

**URL:** `/Flota/Gastos/Mantenimiento`  
**Tabla BD:** `ordenes_mantenimiento`  
**Propósito:** Registrar trabajos de taller (preventivo o correctivo) con su costo.

### Campos del formulario

| Campo | Obligatorio | Notas |
|-------|-------------|-------|
| Vehículo | Sí | — |
| Taller | Sí | Lista de talleres activos |
| Fecha | Sí | Hoy por defecto |
| Nº Factura | Sí | — |
| Tipo mantenimiento | Sí | PREVENTIVO (defecto) / CORRECTIVO |
| Descripción del trabajo | Sí | Max 500 caracteres |
| Mano de obra | No | Monto >= 0 |
| Repuestos | No | Monto >= 0 |
| Otros | No | Monto >= 0 |
| Moneda | Sí | HNL por defecto |
| KM odómetro | No | — |
| Observaciones | No | — |

**Campo calculado:** `total = mano_obra + repuestos + otros` (columna computada en BD).

---

## 5. Gastos de Repuestos

**URL:** `/Flota/Gastos/Repuestos`  
**Tabla BD:** `gastos_repuestos`  
**Propósito:** Registrar compras directas de repuestos (sin taller).

### Campos del formulario

| Campo | Obligatorio | Notas |
|-------|-------------|-------|
| Vehículo | Sí | — |
| Categoría | Sí | Lista de categorías activas |
| Fecha | Sí | Hoy por defecto |
| Nº Factura | No | — |
| Proveedor | No | — |
| Descripción | Sí | Max 250 caracteres |
| Cantidad | Sí | > 0; defecto: 1 |
| Precio unitario | Sí | >= 0 |
| Moneda | Sí | HNL por defecto |
| KM odómetro | No | — |
| Observaciones | No | — |

**Campo calculado:** `subtotal = cantidad × precio_unitario` (columna computada en BD).

---

## 6. Pólizas de Seguro

**URL:** `/Flota/Gastos/Seguros`  
**Tabla BD:** `polizas_seguros`  
**Propósito:** Registrar pólizas de seguro vehicular para el cálculo de costo diario prorrateado.

### Campos del formulario

| Campo | Obligatorio | Notas |
|-------|-------------|-------|
| Vehículo | Sí | — |
| Nº Póliza | Sí | Max 50 caracteres |
| Aseguradora | Sí | Max 150 caracteres |
| Tipo de cobertura | Sí | AMPLIA (defecto) / RESPONSABILIDAD CIVIL / etc. |
| Fecha inicio | Sí | — |
| Fecha fin | Sí | — |
| Prima total | Sí | >= 0 |
| Moneda | Sí | HNL por defecto |
| Observaciones | No | — |

**Campo calculado:** `costo_diario = prima_total / días_vigencia` (columna computada en BD).  
Este valor es usado por el módulo KPI para prorratear el costo del seguro por día.

---

## Auditoría automática

Todos los registros de operación guardan automáticamente:

| Campo | Valor |
|-------|-------|
| `id_empresa` | Tomado de `Session["EmpresaId"]`; sin empresa en la sesión no se guarda nada (ver «Protección de Páginas» en `FLUJO_AUTENTICACION.md`) |
| `creado_por` | Tomado de `Session["Username"]` |
| `fecha_creacion` | `DateTime.UtcNow` al momento del guardado |
| `modificado_por` | Actualizado en ediciones posteriores |
| `fecha_modificacion` | `DateTime.UtcNow` en ediciones |
| `eliminado` | `false` al crear; `true` en borrado lógico |
| `token_concurrencia` | Rowversion SQL Server para optimistic locking |

El borrado es **lógico** en todos los módulos — los registros nunca se eliminan físicamente, solo se marca `eliminado = true`.

---

## Relación con el módulo KPI

Los datos capturados en la operación diaria alimentan directamente el cálculo de **Costo por Kilómetro (L/KM)**:

```
L/KM = (Combustible + Repuestos + Salarios + Seguros + Mantenimiento)
       ─────────────────────────────────────────────────────────────
                         Kilómetros recorridos
```

| Módulo | Tabla | Contribuye a |
|--------|-------|-------------|
| Control de Salidas | `control_salidas` | Denominador (KM): viajes CERRADOS, por día de entrada |
| Odómetro Diario (histórico) | `odometro_diario` | Denominador (KM) sólo en días sin viajes de garita |
| Cargas Combustible | `cargas_combustible` | Numerador (costo combustible) |
| Salarios Diarios | `salarios_diarios` | Numerador (costo personal) |
| Mantenimiento | `ordenes_mantenimiento` | Numerador (costo taller) |
| Repuestos | `gastos_repuestos` | Numerador (costo partes) |
| Seguros | `polizas_seguros` | Numerador (costo diario prorrateado) |
