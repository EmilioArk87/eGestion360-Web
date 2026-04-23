Módulo KPI de Costo por Kilómetro — Resumen Funcional
Leí la documentación en 1 - Documetacion/Kpi excel documentacion tecnica.md. Acá te resumo cómo funciona el módulo y cómo generalizarlo para cualquier tipo de vehículo (no solo buses).

🎯 Objetivo central del módulo
Calcular el costo operativo real por kilómetro recorrido (L/KM o $/KM) de cada vehículo de la flota, consolidando todos los gastos asociados:


                Combustible + Repuestos + Salarios + Seguros + Mantenimiento
   L/KM =  ───────────────────────────────────────────────────────────────────
                               Kilómetros Recorridos
Este indicador permite responder preguntas clave del negocio:

¿Cuál vehículo es más rentable?
¿Qué ruta/asignación tiene mayor costo operativo?
¿Dónde se está perdiendo plata (mal rendimiento, salarios altos, mantenimientos excesivos)?
🧩 Arquitectura en 4 capas lógicas
Capa	Propósito	Hojas/Tablas involucradas
1. Catálogo	Registro maestro de vehículos	Flota
2. Operación diaria	Captura de datos transaccionales	Registro Diario, Combustible, Personal & Salarios
3. Gastos periódicos	Costos no diarios pero imputables	Repuestos & Llantas, Seguros, Mantenimiento
4. Referencias	Datos externos que alimentan conversiones	Tasa de Cambio (BCH), Precios Combustible (SEN)
5. Análisis (Dashboard)	KPIs y rentabilidad	📊 KPIs, 🔒 Ingresos, 🔒 Rentabilidad
🔄 Flujo de datos

   Vehículo (Placa)  ─┐
                      │
   Fecha (diaria)  ───┼──►  Km Recorridos (odómetro inicial/final)
                      │    Combustible (L/USD, litros, rendimiento)
                      │    Salarios (conductor + cobrador/ayudante)
                      │
                      └──►  SUMIF por Placa  ──►  Total Costos por vehículo
                                                           │
   Tasa BCH (VLOOKUP por fecha) ─────────────── Conversión L ↔ USD
                                                           │
                                                           ▼
                                              📊 KPI  =  Costo / KM
Todas las conversiones de moneda se resuelven con un VLOOKUP a la hoja Tasa de Cambio por fecha — un patrón muy limpio.

🚙 Generalización a "cualquier tipo de vehículo"
El modelo tal como está, si se eliminan los supuestos específicos de buses urbanos, es directamente aplicable a cualquier flota. Solo hay que ajustar lo siguiente:

Concepto actual (buses)	Generalización universal
Placa (bus urbano)	Placa (auto, camión, moto, maquinaria) — sin cambios
Ruta 1/2/3 asignada fija	Ruta opcional + campo libre (viaje, obra, cliente)
Conductor + Cobrador	Conductor (1–N) · Cobrador opcional según tipo de vehículo
Tarifa × Pasajeros (ingreso)	Ingreso flexible: por viaje / por carga / por hora / por servicio
Rendimiento Km/L (diésel)	Aplica igual para gasolina, GLP, eléctrico (Km/kWh)
Llantas 275/70 R22.5 fijas	Catálogo libre de especificaciones
Pasajeros como unidad	Opcional — reemplazable por carga_kg, horas_servicio, etc.
Cambios mínimos al modelo relacional
Tabla vehiculos (renombrar buses) con un campo tipo_vehiculo (Auto / Camión / Bus / Moto / Maquinaria / Otro).
Tabla ingresos flexible: en vez de pasajeros × tarifa, permitir unidad_medida (pasajero, kg, viaje, hora) + cantidad × tarifa.
Personal: permitir 0, 1 o N personas por vehículo (algunas flotas no tienen cobrador; maquinaria puede tener solo operador).
Ruta → opcional: para flotas sin rutas fijas, el campo se usa libremente (destino, cliente, obra).
💡 Puntos fuertes del diseño original
✅ KPI único y accionable (L/KM) que consolida todo el costo operativo
✅ Separación clara entre captura (transaccional) y análisis (dashboard)
✅ Conversión de moneda centralizada en una sola tabla de referencia
✅ Columnas calculadas (GENERATED ALWAYS AS) en SQL evitan inconsistencias
✅ Datos confidenciales aislados (Ingresos, Rentabilidad) con control de acceso
✅ Trazabilidad por factura en todas las hojas de gasto
⚠️ Puntos a considerar al migrar a la app web
Hoy está todo vinculado por Placa como clave primaria — en la BD conviene usar id interno y mantener placa como campo único indexado, para soportar cambios de placa.
La "semana" del conductor/cobrador está en el catálogo Flota, pero en un sistema real conviene una tabla histórica asignaciones_personal con fecha_desde/fecha_hasta.
El fallback de tasa 28.40 en VLOOKUP debe convertirse en una política clara: ¿qué pasa si no hay tasa ese día? (¿tomar la última conocida? ¿bloquear el registro?)
El catálogo de categorías de repuestos está hardcodeado en el Excel — conviene tabla de categorías configurables.
🎯 Recomendación de próximos pasos
Si querés incorporar este módulo al sistema web de eGestión360, te sugiero este orden:

Modelo de datos: crear las tablas Vehiculos, TasaCambio, RegistroDiario, Combustible, Salarios, etc. (adaptando el DDL de la sección 6 del documento)
Catálogos maestros: UI para gestionar vehículos, conductores, talleres, rutas, tasas BCH, precios SEN
Captura transaccional: formularios diarios de km, combustible, facturas
Dashboard KPI: vista con el cálculo L/KM por vehículo, ruta, período — con filtros y comparativos
Módulo confidencial: ingresos y rentabilidad con control de rol (Gerencia/Admin únicamente)