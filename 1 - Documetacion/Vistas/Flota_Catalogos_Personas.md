# Flota - Catálogos - Personal (Personas)

## Proposito
Alta, consulta, edición y bitácora de las personas que trabajan en la empresa (conductores, cobradores, mecánicos, supervisores y otros). La persona es **maestra y compartida entre empresas**: se guarda una sola vez (nombre, documento, datos personales) y cada empresa la vincula con su propia relación laboral (cargo, código de empleado, tarifa, fechas). Las pantallas trabajan siempre con la empresa de la sesión.

## Ruta
- Listado: `/Pages/Flota/Catalogos/Personas/Index`
- Nuevo: `/Pages/Flota/Catalogos/Personas/Create`
- Editar: `/Pages/Flota/Catalogos/Personas/Edit/{id}`
- Historial de cambios: `/Pages/Flota/Catalogos/Personas/Historial/{id}`
- Formulario compartido por Nuevo y Editar: `_PersonaFormulario.cshtml` + `wwwroot/js/personas-form.js`

## Funcionalidad
### Listado (Index)
- Filtros: texto (nombre, código de empleado o documento), cargo, estado y **perfil** (`Perfil incompleto`, `Nombre por revisar`).
- Cada fila muestra: nombre, insignia **Verificada/Pendiente** (tiene o no un documento de identidad), insignia **Perfil incompleto** (con lo que falta en el tooltip), documento **enmascarado** (`****-****-*2345`), código, cargo, teléfono, tarifa, ingreso y estado.
- Aviso superior cuando hay personas con el nombre por revisar (carga antigua con el nombre sin separar).
- Acciones por fila: editar, historial y activar/desactivar (estas dos últimas solo con el permiso correspondiente).
- Lista a quienes tienen **vínculo con la empresa de la sesión**, no a todas las personas del sistema.

### Nuevo (Create)
- Formulario por secciones: Identificación, Datos personales, Ubicación, Contacto, Datos laborales y (solo si el cargo es conductor) Licencia.
- Departamento → municipio en cascada; máscaras de DNI, teléfono y licencia en el navegador; edad calculada al escribir la fecha de nacimiento.
- Sin documento: se exige el **código de empleado** y la persona queda con identidad *pendiente*.
- **Antiduplicado:** si en la empresa ya hay alguien con el mismo nombre —o con el mismo primer nombre, primer apellido y fecha de nacimiento aunque falte o cambie el segundo apellido— se muestra el panel «¿Ya está registrada esta persona?» con enlaces a esas fichas y el botón «Es otra persona: registrar de todos modos».
- Si el documento ya existe en otra empresa, la persona se **vincula** a la ficha existente (verificación D8: primer apellido + fecha de nacimiento, con mensaje genérico y sin revelar nada de otras empresas).

### Editar (Edit)
- Solo permite abrir personas con vínculo en la empresa de la sesión; si no, `404` (no se distingue «no existe» de «es de otra empresa»).
- Muestra quién la registró y quién la modificó por última vez.
- **Nombre por revisar:** si la ficha viene de la carga antigua se muestra el texto original y se piden primer/segundo nombre y apellidos.
- **Concurrencia:** el token de la ficha viaja en el formulario; si otro usuario guardó antes, no se pisa su cambio.
- **Fusión:** al escribir un documento que ya pertenece a una persona de **otra empresa** y los datos coinciden, se ofrece fusionar las fichas («Sí, es la misma persona: fusionar»); nada se guarda mientras no se confirme. Si el documento ya está en otra ficha **de la misma empresa**, solo se informa «Ya existe una persona con ese documento en esta empresa».

### Historial (Historial)
- Bitácora por campo (antes → después) de la persona, sus documentos y, de la empresa de la sesión, su vínculo y ficha de empleado.
- Horas de Honduras (UTC-6). Los números de documento se muestran ocultos.
- Lo hecho desde **otra empresa** aparece como «otra empresa», sin detalle, y no se muestran sus datos laborales.

## Flujo tecnico
### GET
- `PersonaPaginaBase.Entrar(permiso)`: sesión autenticada → empresa de la sesión (si no hay, `MensajeBloqueo`) → permiso del módulo `flota` (ver / crear / editar).
- Index: `IPersonaConsultaService.ListarAsync` (con filtros), `CargosAsync` y conteo de nombres por revisar.
- Create: valores por defecto (`TipoDocumento = DNI`, país y moneda de `PersonaValidacionOptions`) y `CatalogosAsync`.
- Edit: `ObtenerParaEdicionAsync(empresa, id)` → `404` si la persona no es de la empresa; carga catálogos y token de concurrencia.
- Historial: `HistorialAsync(empresa, id)` → `404` si no es de la empresa.

### POST
- Create: fuerza `Datos.IdEmpresa` desde la sesión y `IdPersona = null`; llama `IPersonaService.CrearAsync`. Resultados: `Creada`/`Vinculada` (redirige al listado con mensaje y avisos), `RequiereConfirmacion` (muestra parecidas) o errores de validación (clave `Datos.<Campo>`).
- Edit: empresa e id salen de la sesión y de la ruta, nunca del formulario; llama `IPersonaService.ActualizarAsync` con el token de concurrencia y `ConfirmarFusion`. Resultados: `Actualizada`, `Fusionada`, `RequiereFusion`, `NoEncontrada` (`404`) o errores.
- Index `?handler=Toggle`: `IPersonaService.CambiarEstadoAsync` (activa/desactiva el vínculo de la empresa; no borra nada) y redirige conservando los filtros.

## Dependencias
- Servicios inyectados: `IPersonaService`, `IPersonaConsultaService`, `IPersonaValidacionService` (dentro de los anteriores), `IOptions<PersonaValidacionOptions>` (sección `Personas:Validacion`), `TimeProvider`, `IContextoAuditoria` (usuario/IP para la bitácora).
- DbContext: `ApplicationDbContext` con `AuditoriaCambiosInterceptor` (escribe `bitacora_cambios`).
- Modelos: `Persona`, `PersonaDocumento`, `PersonaEmpresa`, `Empleado`, `BitacoraCambio`, catálogos (`CatalogoDepartamento`, `CatalogoMunicipio`, `CatalogoTipoDocumento`, `CatalogoTipoLicencia`, `Pais`, `Moneda`, `Cargo`).
- Tablas BD: `personas`, `persona_documentos`, `persona_empresa`, `empleados`, `bitacora_cambios` (scripts `014`–`018`).

## Reglas de negocio
- La empresa **siempre** sale de la sesión; sin empresa (p. ej. un administrador global) la pantalla se bloquea con un mensaje. No se asume ninguna empresa por defecto.
- Permisos del módulo `flota`: `ver` para listado e historial, `crear` para Nuevo, `editar` para Editar y activar/desactivar. Los botones se ocultan y el servidor lo vuelve a exigir.
- Edad mínima 16 años (18 para conductor); licencia obligatoria para el cargo conductor, con aviso cuando vence en 30 días o menos (valores en `PersonaValidacionOptions`).
- Documentos: DNI con 13 dígitos y estructura validada; el número se guarda normalizado y se muestra enmascarado.
- Identidad **verificada** = tiene al menos un documento; **pendiente** = solo código de empleado.
- Esta pantalla es del **personal** (vínculo de empleado). Una persona que solo es cliente de la empresa no aparece en el listado ni se abre en Editar (`404`); si coincide con quien se está registrando, el aviso de parecidas la muestra sin enlace («Aún no es personal de la empresa»). Registrar como empleado a alguien que ya es cliente de la empresa reutiliza su ficha (`IVinculoService` hace lo inverso).
- Nada se borra: desactivar cambia el estado del vínculo y toda modificación queda en la bitácora (inmutable por trigger).
- Los datos de empleo viven solo en `empleados` (cargo, tarifa, moneda), `persona_empresa` (ingreso y baja) y `persona_documentos` (documento). Desde F6 el servicio ya **no escribe** las columnas antiguas de `personas` (`id_empresa`, `documento`, `tipo_documento`, `cargo`, `tarifa_diaria`, `moneda_tarifa`, `fecha_ingreso`, `fecha_baja`): la entidad `Persona` ya no las tiene y quedan en la BD, vacías en las personas nuevas, hasta que el script `019` las retire.
- Las pantallas de operación (salarios, peajes, combustible y control de salidas) leen al personal con `IPersonaConsultaService.PersonalParaSeleccionAsync`: empleados con vínculo vigente en la empresa de la sesión, filtrados por cargo cuando hace falta (conductores). Quien solo es cliente no aparece ahí.

## Manejo de errores
- Validaciones de formulario: en el navegador (`personas-form.js`) solo las reglas esenciales; el servidor es la autoridad y devuelve los errores por campo.
- Excepciones controladas: conflicto de concurrencia y duplicados se informan como mensajes del servicio, sin pantalla de error.
- Redirecciones de error: sin sesión → `/Login`; persona de otra empresa o inexistente → `404`; sin permiso o sin empresa → misma página con `MensajeBloqueo`.

## Notas
- Riesgos tecnicos: el código de F6 debe estar publicado **antes** de ejecutar la `019` (si no, las pantallas viejas fallarían al no encontrar las columnas). Después de publicarlo, volver a la versión anterior de la aplicación dejaría fuera de las listas viejas a las personas creadas desde entonces (sus columnas antiguas quedan vacías); por eso la `019` se ejecuta solo cuando la nueva versión ya se verificó, y conserva la copia `respaldo_personas_018`. Los scripts que aún usan las columnas antiguas están listados en `INDICE_SCRIPTS_SQL.md`.
- Deuda tecnica: el conteo de «nombres por revisar» del listado recorre la lista otra vez; si el volumen crece, pasarlo a un `COUNT` en el servicio.
- Pruebas: `eGestion360Web.Tests` (ver [PRUEBAS_AUTOMATIZADAS.md](../PRUEBAS_AUTOMATIZADAS.md)).
