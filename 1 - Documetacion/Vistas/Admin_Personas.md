# Administración - Personas del sistema

## Proposito
Pantalla **solo para el administrador general** (rol `admin`, el que no trabaja con ninguna empresa). Muestra a **todas las personas del sistema, de todas las empresas**, con las empresas a las que está asignada cada una y el rol que tiene en cada una (empleado, cliente, proveedor...), y permite corregir sus **datos personales**. Es la contraparte global de la pantalla de Personal, que solo ve a quienes tienen un vínculo con la empresa de la sesión.

## Ruta
- Listado: `/Pages/Admin/Personas/Index` (tarjeta «Personas del sistema» en el menú principal, solo para el administrador general)
- Ver y editar: `/Pages/Admin/Personas/Edit/{id}`
- Historial de cambios: `/Pages/Admin/Personas/Historial/{id}`
- Formulario: el mismo de Personal (`_PersonaFormulario.cshtml` + `wwwroot/js/personas-form.js`) en su variante «solo datos personales».

## Funcionalidad
### Listado (Index)
- Filtros: texto (nombre sin tildes ni mayúsculas, documento, código de empleado y código de cliente), **empresa**, **rol**, estado del vínculo (`Con vínculo vigente` / `Sin vínculo vigente`) y perfil (`Perfil incompleto`, `Nombre por revisar`). Empresa y rol se piden juntos: «cliente en la empresa X».
- Cada fila: nombre, insignia **Verificada/Pendiente**, **Perfil incompleto** (con lo que falta en el tooltip), documento **enmascarado**, teléfono y la lista de **empresas y roles** como insignias (`Empresa · Rol`). Un vínculo vigente va de color según el rol; uno terminado o inactivo va apagado, y el tooltip dice el estado y el cargo o código.
- Incluye a personas sin ningún vínculo. No incluye a las eliminadas ni a las fusionadas en otra ficha.
- Paginado de 25 en 25, ordenado por apellidos y nombres.
- Acciones por fila: ver y editar, e historial. No hay activar/desactivar ni vincular: eso lo hace cada empresa.

### Ver y editar (Edit)
- Arriba, **Empresas y roles** en solo lectura: empresa, rol, detalle (cargo y código del empleado; código del cliente), desde, hasta y estado, con los vigentes primero. Incluye los vínculos terminados.
- Debajo, el formulario de datos personales: identificación y documento, datos personales, ubicación, contacto y licencia. **No hay sección laboral**: cargo, código de empleado, fechas y tarifa son datos de cada empresa y se editan desde su pantalla de Personal. La licencia se muestra siempre (es un dato de la persona).
- Un documento que ya tiene otra persona, de cualquier empresa, es un error con el número de esa persona («Ese documento ya pertenece a otra persona registrada (n.º 2)»). Aquí no se ofrece fusión.
- Concurrencia: el token de la ficha viaja en el formulario; si otro usuario guardó antes, no se pisa su cambio.
- Al cambiar el nombre se actualiza la razón social de los clientes naturales de esa persona en cualquier empresa.

### Historial (Historial)
- Bitácora completa de la persona: datos personales, documentos y, **de todas las empresas**, vínculos, fichas de empleado y fichas de cliente, con una columna **Empresa** que dice desde cuál se hizo cada cambio. No se oculta nada de otras empresas.
- **No se muestran salarios:** los cambios de tarifa diaria y de moneda de la tarifa aparecen como «(no se muestra)» y el resumen de un alta de empleado omite la tarifa.
- Horas de Honduras (UTC-6). Los números de documento van ocultos.

## Flujo tecnico
### GET
- `PersonaAdminPaginaBase.Entrar()`: sin sesión → `/Login`; con sesión pero sin rol `admin` (incluido `empresa_admin`) → `/MainMenu`. Se exige en cada handler, GET y POST.
- Index: `IPersonaAdminConsultaService.EmpresasAsync` + `ListarAsync(filtro, pagina, 25)`. Una página que ya no existe lleva a la última.
- Edit: `ObtenerAsync` (vínculos y documentos) + `ObtenerParaEdicionAsync` (datos para el formulario, sin `Empleado` y con `IdEmpresa = 0`) → `404` si la persona no existe, está eliminada o fue fusionada. Catálogos con `CatalogosAsync(0)` (sin cargos).
- Historial: `HistorialAsync(id)` → `404` si la persona no existe o está eliminada.

### POST (Edit)
- La persona sale de la ruta; `Datos.IdEmpresa` se fuerza a 0 y `Datos.Empleado` a nulo. Llama `IPersonaService.ActualizarComoAdministradorAsync` con el token de concurrencia. Resultados: `Actualizada` (redirige al listado con mensaje y avisos), `NoEncontrada` (`404`) o errores de validación (clave `Datos.<Campo>`).
- Valida con `ModoValidacionPersona.EdicionAdministrador`: igual que la edición normal, sin exigir empresa y con el duplicado de documento global.
- La bitácora (interceptor) registra el cambio con el usuario administrador y **sin empresa**, porque la sesión no tiene una.

## Dependencias
- Servicios: `IPersonaAdminConsultaService` (consultas), `IPersonaService` (escritura), `IPersonaConsultaService` (catálogos), `IOptions<PersonaValidacionOptions>`, `TimeProvider`.
- Ayudas: `EtiquetasVinculo` (nombre y color de cada rol), `EtiquetasBitacora` (etiquetas del historial; `Resumir` acepta columnas a omitir).
- Modelos: `Persona`, `PersonaDocumento`, `PersonaEmpresa`, `Empleado`, `Cliente`, `BitacoraCambio`, `Empresa`.

## Reglas de negocio
- Solo el administrador general entra. El administrador de una empresa y el resto vuelven al menú: ven a las personas de su empresa en Personal, no las de otras.
- El administrador edita **solo datos personales**. Los vínculos (alta, baja, rol) y los datos de empleo siguen siendo de cada empresa.
- Nada se borra desde aquí. Una persona sin vínculos sigue apareciendo (por ejemplo, quien fue cliente y ya no lo es).
- La decisión D8 (una empresa solo ve a las personas con las que tiene vínculo) sigue valiendo para las empresas; esta pantalla es la excepción deliberada del administrador general.

## Manejo de errores
- Validaciones: las mismas que Personal, con mensajes por campo en el formulario.
- Persona inexistente, eliminada o fusionada: `404`.
- Conflicto de concurrencia o error de guardado: mensaje del servicio, sin pantalla de error.

## Notas
- **Proveedores:** hoy ninguna persona tiene vínculo de proveedor (la tabla de proveedores todavía no se enlaza con `persona_empresa`). El filtro de rol ya lo ofrece y aparecerá en cuanto existan; igual para `usuario` y `contacto`.
- **No incluido:** fusión de duplicados, vincular o dar de baja desde esta pantalla y edición de datos de empleo. La fusión existe en `IPersonaService.FusionarAsync` pero con las reglas de la empresa.
- Pruebas: `PersonaAdminConsultaServiceTests` y las de `ActualizarComoAdministradorAsync` en `PersonaServiceTests` (ver [PRUEBAS_AUTOMATIZADAS.md](../PRUEBAS_AUTOMATIZADAS.md)).
