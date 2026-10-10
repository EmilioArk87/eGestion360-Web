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
| 009 | 009_integracion_almacen_contable.sql | Integracion contable del almacen de repuestos (bandera activo_contable + precision monto) | 2026-08-06 | Pendiente |
| 010 | 010_ct_nucleo_contable.sql | Nucleo del modulo contable: ct_cuentas, ct_ejercicios, ct_periodos, ct_centros_costo, ct_asientos, ct_asiento_movimientos. Revisado el 2026-10-06 antes de aplicarse: claves foraneas compuestas (id, id_empresa) para que la BD impida mezclar datos de empresas; una sola transaccion; POSTCHECK completo; moneda CHAR(3) con FK a monedas; CHECK de fechas en ejercicios y periodos; token_concurrencia en cuentas, ejercicios y periodos. Aborta si existe una instalacion a medias; idempotente | 2026-08-06 | Pendiente |
| 010 (reversa) | 010_ct_nucleo_contable_reversa.sql | Reversa del 010: elimina las 6 tablas ct_* solo si estan vacias y nada ajeno al modulo depende de ellas. Solo se ejecuta para revertir | 2026-10-06 | No ejecutada (solo para revertir) |
| 011 | 011_configurar_correo_notificaciones.sql | Alta del perfil SMTP notificaciones@siptecnologia.somee.com en EmailConfigurations (tabla que lee la app) y saneamiento de contrasenas en claro en la tabla huerfana EmailConfiguration | 2026-08-30 | Aplicado 2026-08-30 |
| 012 | 012_control_salidas_entradas.sql | Control de salidas y entradas de vehiculos (operacion de porteria/garita en tiempo real) | 2026-09-24 | Aplicado 2026-09-27 |
| 013 | 013_datos_demo_control_salidas.sql | Datos demo de salidas y entradas de garita para la empresa Demo (14 al 27 de septiembre de 2026; filas marcadas creado_por = 'demo_seed') | 2026-09-27 | Aplicado 2026-09-27 |
| 014 | 014_personas_maestra_estructura.sql | Personas maestras (persona unica compartida por varias empresas): amplia dbo.personas y crea persona_documentos, persona_empresa, empleados, bitacora_cambios (inmutable), 4 catalogos y el enlace de clientes con personas; solo estructura, sin migrar datos | 2026-09-30 | Aplicado 2026-09-30 |
| 015 | 015_catalogos_base_honduras.sql | Carga de catalogos base de Honduras: 18 departamentos, 298 municipios (codigos SINIT/OCHA confirmados con SGJD), tipos de documento (DNI, RTN, pasaporte, carne de residente) y de licencia, cargos base para empresas sin cargos; restricciones de integridad en catalogo_paises; corrige el tipo de persona_documentos.numero_normalizado; agrega columna fuente a los catalogos | 2026-09-30 | Aplicado 2026-09-30 |
| 016 | 016_seed_catalogo_paises.sql | Semilla reproducible del catalogo de paises ISO 3166-1 (249 filas, verificadas contra UNSD M49); idempotente, no modifica los existentes. Los datos ya existian en la BD pero el repositorio no tenia script que los cargara | 2026-09-30 | Aplicado 2026-09-30 |
| 017 | 017_monedas_iso4217_y_fk.sql | Catalogo de monedas con datos de ISO 4217 (SIX, 2026-09-17): codigo numerico y decimales; marca inactivas ANG, BGN y ZWL; agrega KYD, SSP, VED, XCG y ZWG; monedas.codigo_iso pasa a CHAR(3); 12 claves foraneas desde columnas de moneda CHAR(3) (quedan 8 de otro tipo para un paso posterior) | 2026-09-30 | Aplicado 2026-09-30 |
| 018 | 018_personas_migrar_a_maestra.sql | Migra las personas actuales a la persona maestra: crea el vinculo de empleado (persona_empresa) y la ficha (empleados) de cada persona, registra los DNI en persona_documentos, separa nombres y apellidos cuando el reparto es seguro, calcula el nombre normalizado, marca la identidad y escribe la bitacora (origen script:018); crea la copia respaldo_personas_018; no toca las columnas viejas | 2026-09-30 | Aplicado 2026-10-01 |
| 019 | 019_personas_retirar_columnas.sql | Retira de dbo.personas las 8 columnas viejas (id_empresa, documento, tipo_documento, cargo, tarifa_diaria, moneda_tarifa, fecha_ingreso, fecha_baja) con sus 2 claves foraneas y 2 indices; antes comprueba que nada se pierda (vinculos, documentos y codigos de empleado ya estan en las tablas nuevas); crea la copia respaldo_personas_019 para la reversa. Los nombres por revisar ya no lo bloquean (decision del 2026-10-03) | 2026-10-03 | Aplicado 2026-10-03 |
| 020 | 020_tasas_cambio_v2.sql | Tasas de cambio v2 para el job automatico que reemplaza a la app WinForms APICambioAHNL: recrea dbo.tasas_cambio (debe estar vacia) con tasa oficial (id_empresa NULL) o propia de empresa, tipo COMPRA/VENTA/REFERENCIA, fecha de vigencia, fuente, tasa derivada e historico versionado (estado, version, id_tasa_anterior); crea la bitacora dbo.tasas_cambio_ejecuciones y su detalle dbo.tasas_cambio_ejecuciones_detalle; retira dbo.tipos_cambio (F0) si existe vacia. Aborta sin cambiar nada si una tabla a eliminar tiene filas; idempotente; POSTCHECK columna por columna dentro de la transaccion; sin datos semilla. Ver TASAS_CAMBIO.md. El 2026-10-05 se corrigieron el PRECHECK (no cuenta los CHECK de la propia tabla como dependencias) y el POSTCHECK (intercalacion de sys.objects.type) | 2026-10-04 | Aplicado 2026-10-05 |
| 020 (reversa) | 020_tasas_cambio_v2_reversa.sql | Reversa del 020, solo si las 3 tablas nuevas estan vacias (si tienen datos, aborta): las elimina y recrea dbo.tasas_cambio con la estructura de KPI_02 y las FK del 017; opcionalmente recrea dbo.tipos_cambio (@recrear_tipos_cambio = 1). Solo se ejecuta para revertir | 2026-10-04 | No ejecutada (solo para revertir) |
| 021 | 021_usuarios_persona.sql | Vincula cada usuario (dbo.Users) con la persona que lo usa: agrega Users.PersonaId INT NULL con FK_Users_personas_PersonaId (sin cascada) e IX_Users_PersonaId; solo estructura, las 7 filas quedan sin persona. Paso intermedio hasta F1 (anexo de ADR-003): ahi el vinculo pasa a la membresia. Va ANTES de publicar la version que lo usa (sin la columna fallaria el inicio de sesion) | 2026-10-10 | Pendiente |
| 022 | 022_personas_activo_sin_vinculo_activo.sql | Apaga personas.activo de las personas activas sin ningun vinculo activo (los clientes de prueba de Demo dados de baja: ids 97, 99 y 101), con su fila de bitacora (origen script:022); la aplicacion ya lo hace al dar de baja a un cliente. Tope de 20 personas | 2026-10-10 | Pendiente |
| 023 | 023_retirar_tablas_legado.sql | Paso F0.7 del plan de arquitectura: mueve al esquema retirado (no borra) las tablas que la aplicacion no usa: usuarios y usuarios_empresas (gemela vieja de Users, 0 filas), paises (gemela de catalogo_paises) y EmailConfiguration (gemela de EmailConfigurations) con su trigger, sus checks y los 5 procedimientos que escribian en ella (SP_ConfigurarHostingerEmail, sp_GetActiveEmailConfiguration, sp_SetDefaultEmailConfiguration, sp_SetDefaultEmailConfigurationSafe, sp_UpdateEmailTestStats); borra dbo.respaldo_personas_018 (copia con datos personales; la 019 se conserva hasta F1). PRECHECK aborta ante dependencias no previstas; idempotente; ROLLBACK devuelve todo a dbo. Requiere publicada la version sin /ConfigurarHostinger. Ensayado en LocalDB (aplicar, repetir, revertir y 3 casos de aborto) y con PRECHECK real y NOEXEC contra eBD_SPD | 2026-10-10 | Pendiente |
| 024 | NNN_descripcion_corta.sql | Plantilla para proximo cambio | YYYY-MM-DD | Pendiente |

## Scripts de datos de Demo sin numero

Se ejecutan a mano contra la empresa Demo. No forman parte de la cadena de creacion de una BD limpia y no usan un
numero de orden (el 019 retiro las columnas viejas de `dbo.personas`).

| Archivo | Proposito | Fecha | Estado |
|---|---|---|---|
| seed_demo_catalogos_cliente_demo.sql | Activa el modulo Catalogos para la empresa Demo (hoy solo la pantalla de Clientes) y da ver, crear y editar, sin eliminar, al rol Flota, que solo usa cliente_demo; con PRECHECK, POSTCHECK y ROLLBACK | 2026-10-02 | Aplicado 2026-10-02 |

## Scripts que asumen el esquema anterior a la 019

El script 019 (aplicado el 2026-10-03; retiro las columnas viejas de `dbo.personas`: `id_empresa`, `documento`, `tipo_documento`, `cargo`,
`tarifa_diaria`, `moneda_tarifa`, `fecha_ingreso`, `fecha_baja`) dejo sin funcionar a los scripts de abajo, que las
leen o las escriben. No son parte de la aplicacion: son datos de ejemplo o cargas ya hechas. Cada uno lleva un aviso
al inicio. **Solo funcionan en una BD anterior a la 019**; ahora los datos de personas se cargan con la pantalla de Personal
(o con la logica nueva: persona maestra + `persona_empresa` + `empleados` + `persona_documentos`).

| Script | Que hace | Columnas viejas que usa |
|--------|----------|-------------------------|
| 013_datos_demo_control_salidas.sql | Datos demo de garita (busca conductores en `personas`) | `id_empresa`, `cargo` |
| seed_combustible_mensual.sql | Semilla de combustible del mes | `id_empresa`, `cargo` |
| seed_odometro_mensual.sql | Semilla de odometro del mes | `id_empresa`, `cargo` |
| seed_salarios_mensual.sql | Semilla de salarios del mes | `id_empresa`, `cargo`, `tarifa_diaria` |
| seed_demo_flota_extra.sql | Datos demo de flota (inserta y busca personas por documento) | `id_empresa`, `documento`, `tipo_documento`, `cargo`, `tarifa_diaria` |
| Transgar/30_transgar_personas.sql | Carga de 68 empleados de Transgar | `id_empresa`, `documento`, `tipo_documento`, `cargo` |
| Transgar/40_transgar_cargas_combustible.sql | Cargas de combustible de Transgar (cruza el conductor por documento) | `id_empresa`, `documento` |
| Transgar/60_transgar_odometro_diario.sql | Odometros de Transgar (cruza el conductor por documento) | `id_empresa`, `documento` |

`KPI_01_Catalogos.sql` crea `dbo.personas` con las columnas viejas, pero es parte de la cadena de creacion de una BD
nueva (despues corren la 014, la 018 y la 019), asi que no lleva aviso.

Lo mismo pasa con las tasas de cambio: `KPI_02_Referencias.sql` crea `dbo.tasas_cambio` con la estructura vieja y
`F0_Catalogos_Transversales.sql` crea `dbo.tipos_cambio`; en una BD nueva el 020 reemplaza la primera por la v2 y retira
la segunda (las dos estan vacias en ese momento).

## Reglas de ejecucion

1. Ejecutar en orden ascendente de la columna Orden.
2. No cambiar ni reutilizar un numero de orden ya asignado.
3. Todo script nuevo debe registrarse inmediatamente en este indice.
4. Todo script nuevo debe incluir PRECHECK, CAMBIO, POSTCHECK y ROLLBACK.
