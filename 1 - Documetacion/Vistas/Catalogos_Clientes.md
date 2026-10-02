# Catálogos - Clientes

## Proposito
Catálogo de clientes de la empresa de la sesión. Un cliente **natural con documento** se registra con su **ficha de persona**: la persona se guarda una sola vez en todo el sistema (la misma que puede ser empleado) y su razón social es su nombre. Un cliente jurídico o un consumidor final (natural sin documento) sigue como siempre, con su razón social y sin persona.

## Ruta
- Listado: `/Pages/Catalogos/Clientes/Index`
- Nuevo: `/Pages/Catalogos/Clientes/Create`
- Editar: `/Pages/Catalogos/Clientes/Edit/{id}`
- Eliminar: `/Pages/Catalogos/Clientes/Delete/{id}`
- Formularios compartidos: `_ClienteForm.cshtml` (datos comerciales) y `_ClientePersona.cshtml` (identificación de la persona, solo en Nuevo) + `wwwroot/js/clientes-persona.js`

## Funcionalidad
- **Listado:** busca por código, razón social, nombre comercial o RTN. Los clientes enlazados a una persona llevan la insignia «Con ficha».
- **Nuevo:**
  - *Natural con documento:* se pide tipo y número de documento y el nombre (primer nombre y primer apellido obligatorios; segundo nombre, segundo apellido y fecha de nacimiento opcionales). Con documento la razón social deja de pedirse.
  - Si el documento ya existe, se **reutiliza la persona**: de esta empresa con otro rol (por ejemplo, empleado) o de otra empresa si coinciden primer nombre y primer apellido (si no, un mensaje genérico que no revela nada). Sus datos personales no se modifican.
  - Si en la empresa hay personas parecidas, se muestra el aviso «¿Ya está registrada esta persona?» con el botón «Es otra persona: registrar de todos modos».
  - *Jurídico o natural sin documento:* igual que antes.
- **Editar:** de un cliente con ficha se muestra la persona (documento enmascarado, estado de identidad, desde cuándo es cliente, y la baja si la hay). La razón social y el tipo no se editan: la razón social cambia cuando cambia el nombre de la persona. Desactivar al cliente da de baja su relación; activarlo la reabre.
- **Eliminar:** además de marcar al cliente como eliminado (soft delete), cierra su relación como cliente. La persona y sus otros roles no se tocan.

## Flujo tecnico
### GET
- Verifica sesión, módulo `catalogos` y permiso (ver / crear / editar / eliminar). Filtra siempre por `IdEmpresa` de la sesión.
- Nuevo y Editar cargan monedas activas, condiciones de pago de la empresa y (Nuevo) los tipos de documento.

### POST
- Nuevo con ficha: `IVinculoService.RegistrarClienteNaturalAsync`. Resultados: `Creado`, `Vinculado` (persona reutilizada), `Reactivado` (cliente que volvía), `RequiereConfirmacion` (parecidas) o errores por campo con el prefijo `Persona.` o `Cliente.`.
- Nuevo sin ficha: se crea el `Cliente` directamente (siempre sin vínculo: un `IdPersonaEmpresa` que viniera del formulario se descarta).
- Editar: asigna solo los campos comerciales; si cambia el estado de un cliente con ficha, llama `TerminarClienteAsync` o `ReactivarClienteAsync`, que guardan también los demás cambios.
- Eliminar: si hay ficha, `TerminarClienteAsync` (motivo «Eliminado desde Clientes») y luego el soft delete.

## Dependencias
- Servicios inyectados: `IVinculoService`, `IPersonaConsultaService` (catálogos del formulario).
- DbContext: `ApplicationDbContext` con `AuditoriaCambiosInterceptor`: los cambios de un cliente enlazado quedan en `bitacora_cambios` y en el historial de la persona de su empresa.
- Modelos: `Cliente`, `PersonaEmpresa`, `Persona`, `PersonaDatosInput`.
- Tablas BD: `clientes`, `persona_empresa`, `personas`, `persona_documentos`, `bitacora_cambios`.

## Reglas de negocio
- Código de cliente único por empresa (el índice cuenta también a los eliminados).
- Un cliente con ficha exige documento; un solo cliente por vínculo (`UX_clientes_vinculo`).
- Ser cliente de una empresa es un dato privado de ella: otra empresa que comparte a la persona no lo ve, ni en su historial.
- La razón social de un cliente natural con ficha se actualiza en todas las empresas cuando el nombre de la persona cambia o se fusionan dos fichas.

## Manejo de errores
- Validaciones de formulario: en el navegador solo lo esencial; el servidor es la autoridad.
- Los errores del servicio se muestran junto al campo (`Persona.Documento`, `Cliente.Codigo`...).
- Sin sesión → `/Login`; sin módulo o sin permiso → `/Catalogos/Index`; cliente que no es de la empresa → de vuelta al listado.

## Notas
- Corregido al conectar la pantalla: `Cliente.Empresa` (propiedad de navegación no nulable) hacía que MVC rechazara siempre el formulario sin mostrar por qué, y los campos del formulario compartido salían sin el prefijo `Cliente.`, así que Editar tampoco los enlazaba. Ahora `Empresa` lleva `[ValidateNever]` y el parcial usa el prefijo.
- Riesgos tecnicos: las escrituras se probaron con SQLite y en pantalla sobre una base en memoria, no contra SQL Server real.
- Deuda tecnica: no hay pantalla para corregir los datos personales de una persona que solo es cliente (un error de tipeo en su nombre); hoy solo se editan desde Personal, que abre a empleados.
