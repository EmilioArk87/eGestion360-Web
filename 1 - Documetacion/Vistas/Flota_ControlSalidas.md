# Flota - Control de Salidas y Entradas

## Proposito
Captura y monitoreo en tiempo real del cruce de vehículos por el portón o garita de la empresa. Detección automática del tipo de movimiento (salida o entrada), validación del odómetro y cálculo automático de kilometraje recorrido y tiempo fuera del plantel.

## Ruta
- Vista en Vivo: `/Pages/Flota/Operacion/ControlSalidas/Index`
- Historial y Auditoría: `/Pages/Flota/Operacion/ControlSalidas/Historial`

## Funcionalidad
- **Detección automática de modo:**
  - Si el vehículo está en plantel ("en sitio"), el sistema despliega el panel de **SALIDA** solicitando odómetro de salida, conductor y ruta/destino.
  - Si el vehículo tiene una salida abierta ("fuera"), el sistema despliega el panel de **ENTRADA** mostrando los datos del viaje abierto y solicitando el odómetro de entrada.
- **Validaciones en vivo:**
  - El odómetro de salida no puede ser menor a la última lectura registrada del vehículo.
  - El odómetro de entrada no puede ser menor al de salida.
  - Alerta ante saltos sospechosos (> 1.200 km en un solo viaje).
- **Tablas operativas:**
  - Listado de vehículos actualmente fuera del sitio con acción rápida para registrar su retorno.
  - Listado de los últimos movimientos del día.
  - KPIs en vivo: En plantel, Fuera ahora, Movimientos hoy, Km recorridos hoy.
- **Historial:**
  - Filtros por fecha (Desde / Hasta), vehículo y estado (Abierto, Cerrado, Anulado).
  - Posibilidad de anular movimientos erróneos.

## Flujo tecnico
### GET
- Verifica autenticación (`AuthHelper.IsAuthenticated`).
- Filtra datos por `IdEmpresa` de la sesión.
- Carga vehículos activos, viajes abiertos (`estado = 'ABIERTO'`), movimientos del día y catálogos de conductores y rutas.
- Calcula contadores de KPIs.

### POST
- `OnPostSalidaAsync`: Valida que no exista salida abierta previa para el vehículo, valida continuidad del odómetro, crea registro con `estado = 'ABIERTO'` en `control_salidas`.
- `OnPostEntradaAsync`: Localiza la salida abierta, valida que el odómetro de entrada sea mayor o igual al de salida, actualiza fecha/hora de entrada, odómetro de entrada y cambia estado a `'CERRADO'`.
- `OnGetVehiculoInfoAsync`: Endpoint JSON para actualización dinámica del DOM del cliente sin recargar la página completa.

## Dependencias
- DbContext: `ApplicationDbContext`
- Modelos: `ControlSalida`, `Vehiculo`, `Persona`, `Ruta`
- Tabla BD: `dbo.control_salidas` (Script `012_control_salidas_entradas.sql`)

## Reglas de negocio
- Un vehículo solo puede tener **un único viaje abierto** a la vez (`UX_control_salidas_vehiculo_abierto`).
- Odómetros validados contra última lectura conocida y contra odómetro de salida.
- Multitenant estricto por `id_empresa`.
- Auditoría estándar con `creado_por`, `fecha_creacion`, `modificado_por`, `fecha_modificacion`, `token_concurrencia`.
