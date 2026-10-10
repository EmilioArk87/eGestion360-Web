# Usuarios y su persona

## Proposito
Crear y editar los usuarios del sistema y vincular cada uno con la persona que lo usa (script 021). Es el paso intermedio
hasta F1, donde el vínculo pasa a la membresía de la cuenta en cada tenant (anexo de ADR-003).

## Ruta
/Pages/UserManagement (lista), /Pages/Admin/Usuarios/Create, /Pages/Admin/Usuarios/Edit

## Funcionalidad
- Lista (solo el administrador general): columna «Persona» con el nombre, o el aviso «Sin persona» en los usuarios activos que todavía no la tienen.
- Crear: la persona es obligatoria. Se elige una que ya está registrada (búsqueda por nombre o documento) o se crea con los datos mínimos: documento, primer y segundo nombre, primer y segundo apellido y fecha de nacimiento.
- Editar: sección «Persona» con la actual (nombre, documento enmascarado y, si tiene más de uno, sus otros usuarios), búsqueda para vincular otra, «Crear la persona» y «Quitar».
- Los datos personales no se editan aquí: se corrigen en Personal o en Personas del sistema.

## Flujo tecnico
### GET
- Edit: carga el usuario; `IUsuarioPersonaService.PersonaDelUsuarioAsync` para la persona actual y, si viene `BuscarPersona`, `BuscarPersonasAsync`.
- Create `?handler=BuscarPersonas&texto=`: devuelve JSON (id, nombre, documento enmascarado, empresas, usuarios) para `wwwroot/js/usuarios-persona.js`.

### POST
- Create: en una transacción crea el usuario y llama `VincularAsync` (persona elegida) o `CrearPersonaYVincularAsync` (persona nueva). Si la persona no se puede vincular, se revierte todo y el usuario no se crea.
- Edit `?handler=Vincular`, `?handler=QuitarPersona`, `?handler=CrearPersona`: llaman al servicio y vuelven a la misma pantalla con el resultado.
- Edit (guardar): si el usuario cambia de empresa, se asegura que su persona quede también dentro de la empresa nueva.

## Dependencias
- Servicios inyectados: `IUsuarioPersonaService` (`Services/Personas/UsuarioPersonaService.cs`), `IPasswordService` (Create).
- DbContext/Repositorios: `ApplicationDbContext` con `AuditoriaCambiosInterceptor`.
- Modelos/DTOs: `User.PersonaId`/`User.Persona`, `PersonaParaUsuario`, `QuienOperaUsuarios`, `ResultadoUsuarioPersona`, `PersonaNuevaForm`.
- Configuración: `Plataforma:IdEmpresaPropia` (por defecto 1, SIP).

## Reglas de negocio
- El administrador general ve y vincula cualquier persona. Un administrador de empresa solo ve personas con relación con su empresa y solo trabaja con usuarios de su empresa; lo demás da `404`, aunque se fuerce por URL.
- La persona queda dentro de la empresa del usuario: si no tiene ningún vínculo vigente con ella, se le registra (o reabre) un vínculo de tipo `usuario`. Para un usuario sin empresa se usa la empresa dueña de la plataforma.
- Una persona puede tener varios usuarios hasta F1 (`admin` y `egaray`). Quitar la persona de un usuario cierra su vínculo de `usuario` solo si no le queda otro usuario en esa empresa; los vínculos de empleado o cliente no se tocan, y nada se borra.
- Crear la persona exige documento y fecha de nacimiento. Si el documento ya es de una persona de la empresa (o, para el administrador general, de cualquiera), pide buscarla y vincularla. Si es de otra empresa, un administrador de empresa solo la vincula si coinciden primer nombre y primer apellido (D8), con el mensaje genérico de siempre si no. Avisa de personas parecidas antes de crear.
- Bitácora: vincular, cambiar o quitar la persona deja una fila (entidad `Users`, campo `PersonaId`). De un usuario no se registra nada más: ni la contraseña ni el resto de la cuenta. En el historial de la persona se ve como «Persona vinculada del usuario X».

## Manejo de errores
- Validaciones de formulario: las reglas de la persona las aplica el servicio y vuelven al campo (`PersonaNueva.<Campo>`); en Create, «Elige la persona que usará este usuario, o créala».
- Excepciones controladas: un error al guardar revierte la transacción y muestra «No se pudo guardar».
- Redirecciones de error: sin sesión → `/Login`; sin rol de administrador → `/MainMenu`; usuario o persona fuera del alcance → `404` o `/MainMenu`.

## Notas
- Riesgos tecnicos: `Users` (plataforma) apunta a `personas` (tenant) hasta F1; documentado en el anexo de ADR-003.
- Deuda tecnica: en F1 `admin` y `egaray` pasan a ser una sola cuenta y el vínculo se mueve a la membresía.
