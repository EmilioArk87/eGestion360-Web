# Flujo de Operación Diaria — Módulo Flota

## Estructura del Módulo

```
/Flota
├── /Catalogos          ← Datos maestros (vehículos, rutas, personas, talleres)
│   └── /Vehiculos      ← CRUD de vehículos
├── /Operacion          ← Registros del día a día
│   ├── /OdometroDiario ← Kilómetros recorridos por vehículo
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
| Rutas | `rutas` | Al menos 1 ruta activa (para odómetro) |
| Personas | `personas` | Al menos 1 persona activa con cargo CONDUCTOR |
| Talleres | `talleres` | Al menos 1 taller activo (para mantenimiento) |
| Categorías de repuesto | `categorias_repuesto` | Al menos 1 categoría activa |

Todos los selects en formularios filtran por `id_empresa` de la sesión y `activo = true`.

---

## 1. Odómetro Diario

**URL:** `/Flota/Operacion/OdometroDiario`  
**Tabla BD:** `odometro_diario`  
**Propósito:** Registrar los kilómetros recorridos por cada vehículo en el día.

### Flujo de registro

```
Operador accede a /Flota/Operacion/OdometroDiario/Create
        │
   Fecha se pre-llena con hoy
        │
   Selecciona: Vehículo (obligatorio)
   Ingresa:    KM inicial, KM final
   Selecciona: Ruta (opcional), Conductor (opcional)
   Ingresa:    Observaciones (opcional)
        │
        ▼
   Validación: KM final >= KM inicial
        │
        ▼
   Sistema calcula KM recorridos = KM final - KM inicial (columna computada en BD)
   Registra: id_empresa (de sesión), creado_por (username de sesión), fecha_creacion (UTC)
        │
        ▼
   Guarda en `odometro_diario` → redirige al listado con mensaje de confirmación
```

### Campos del formulario

| Campo | Obligatorio | Validación |
|-------|-------------|------------|
| Vehículo | Sí | Lista de vehículos activos de la empresa |
| Fecha | Sí | Pre-llenada con hoy |
| KM inicial | Sí | Número >= 0 |
| KM final | Sí | Número >= 0; debe ser >= KM inicial |
| Ruta | No | Lista de rutas activas |
| Conductor | No | Lista de personas con cargo CONDUCTOR |
| Observaciones | No | Texto libre, max 500 caracteres |

### Campo calculado en BD

`km_recorridos = km_final - km_inicial` (columna computada, no se ingresa manualmente).

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
| Persona | Sí | Lista de personas activas |
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
| `id_empresa` | Tomado de `Session["EmpresaId"]` (defecto: 1) |
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
| Odómetro Diario | `odometro_diario` | Denominador (KM) |
| Cargas Combustible | `cargas_combustible` | Numerador (costo combustible) |
| Salarios Diarios | `salarios_diarios` | Numerador (costo personal) |
| Mantenimiento | `ordenes_mantenimiento` | Numerador (costo taller) |
| Repuestos | `gastos_repuestos` | Numerador (costo partes) |
| Seguros | `polizas_seguros` | Numerador (costo diario prorrateado) |
