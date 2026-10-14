# ADR-025 · Tenant de soporte del administrador general (D41 a D43)

- **Estado:** Aceptada (implementada en F1.4c; confirmada por Emilio el 2026-10-14)
- **Fecha:** 2026-10-13
- **Decidió:** Claude las propuso al implementar F1.4c del plan de la Fase 1; Emilio las aceptó el 2026-10-14
- **Fase:** F1
- **Completa:** [ADR-023](ADR-023-politica-rls-por-tenant.md) (D36: la plataforma no lee datos de tenant) y
  [ADR-024](ADR-024-contexto-de-sesion-por-conexion.md) (el contexto de sesión por conexión). Anticipa la consola del operador
  de F1.9 («acceso de soporte temporal con motivo, vencimiento, aviso al tenant y bitácora»).

## Contexto

Con la política RLS encendida la base solo entrega las filas del tenant del contexto, y el ámbito de plataforma no ve filas de
tenants (D36). El administrador general, que es el operador de la plataforma (D23), no pertenece a ningún tenant, así que sus
pantallas de datos de tenant (personas del sistema, módulos y roles de las empresas, usuarios) quedaban vacías y crear una empresa
fallaba. El recorrido de F1.4b sobre una copia con la política encendida lo mostró: `/Admin/Personas/Index` pasó de 26 a 2 filas y
el alta de una empresa daba error 500 por el bloqueo de `empresas`.

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| D41 | El administrador general elige un **tenant de soporte** y trabaja dentro de él | `/Admin/Soporte` lista los tenants (menos los cancelados); al elegir uno, el servidor lo guarda en la sesión (`TenantDeSoporte`) y `TenantMiddleware` lo convierte en el tenant de cada petición **solo** si la sesión es de un administrador sin empresa (con empresa manda la de la empresa; el tenant nunca sale de la URL ni de un formulario). Cada entrada y salida queda en el log con el usuario, el tenant y su estado, y las demás líneas de log de esas peticiones llevan `Soporte:True`. El motivo, el vencimiento, el aviso al tenant y la bitácora persistente quedan para F1.9. |
| D42 | Las pantallas de datos de un tenant exigen el tenant de soporte y conservan su filtro | Módulos y roles de las empresas, roles y permisos, personas del sistema y usuarios (`/Admin/EmpresaModulos`, `EmpresaRoles`, `RolesPermisos`, `Personas`, `Usuarios`) salen de la lista de pantallas de plataforma: corren como las de un administrador de ese tenant, con el filtro de EF y la política de la base. Sin tenant de soporte, `TenantDeSoporteRequeridoPageFilter` lleva a elegirlo y después devuelve a la pantalla pedida. Editar un usuario cuya empresa es de otro tenant pide cambiar de tenant en lugar de dejar la empresa en blanco. La lista de plataforma queda en: empresas (directorio), usuarios (`UserManagement`), tasas de cambio y las herramientas de la plataforma (módulos, outbox, correo, restablecer clave, soporte). |
| D43 | Crear, editar y eliminar una empresa fijan el tenant de esa empresa antes de guardar, sin pedir tenant de soporte | La política deja leer el directorio de empresas pero solo escribir dentro del tenant de la fila (D35). `Empresas/Create` fija el tenant elegido en el formulario, y `Edit` y `Delete` el de la empresa que acaban de leer. En la lista de empresas, «Módulos» y «Roles» entran al tenant de la empresa y abren su pantalla. |

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| D41 | Ámbito de plataforma que lee todos los tenants | Es lo que D36 descartó: un error en una pantalla de plataforma abriría los datos de todos. |
| D41 | Pedir ya un motivo y un vencimiento | Es la consola del operador (F1.9), con su tabla de auditoría; aquí solo hace falta que el soporte funcione y deje rastro en el log. |
| D41 | El tenant de soporte en la URL o en un campo | ADR-001: el tenant se resuelve en el servidor, nunca desde la petición. |
| D42 | Dejar esas pantallas como de plataforma y que cada una fije el tenant | Son muchas, con varios handlers cada una; olvidar uno deja datos vacíos o de otro tenant. El filtro por prefijo cubre las pantallas actuales y futuras. |
| D43 | Exigir el tenant de soporte también para las empresas | Fricción sin ganancia: crear una empresa ya elige su tenant en el formulario, y las otras dos acciones parten de una fila cuyo tenant es conocido. |

## Consecuencias

- Las «Personas del sistema» pasan a ser las del tenant de soporte (una a la vez), como ya pedía el plan: «herramienta de soporte con
  acceso auditado por tenant». Ya no hay una lista de personas de todos los tenants.
- Lo que el administrador general ve y guarda con un tenant de soporte lo ve y guarda como cualquier administrador de ese tenant;
  la base lo vigila igual. Con otro tenant elegido, las pantallas de una empresa de un tenant distinto responden 404; un envío armado
  a mano contra otro tenant falla en la base (sin cambios de datos).
- Es el último requisito para encender la política: el script 037 puede aplicarse después de publicar F1.4b y F1.4c.
- Observación fuera de F1.4c: algunas pantallas de administración de empresa (`EmpresaRoles`, `RolesPermisos`) aceptan un
  `empresaId` del formulario sin comprobar que sea la empresa del administrador de empresa; entre tenants lo impide el filtro de EF y
  la base, entre empresas del mismo tenant lo resolverán los permisos por acción de F1.6.

## Cómo se verifica

- `Services/Tenancy/TenantDeSoporteTests`: el tenant de soporte es solo del administrador sin empresa; el middleware lo fija y lo
  anota en el log, nunca desde la petición; el filtro lleva a elegirlo, vuelve a la pantalla pedida y no interviene en las demás.
- `Paginas/SoporteTests`: `/Admin/Soporte` solo para el administrador general, solo tenants que existen y no están cancelados,
  redirección solo a rutas locales, entrar desde la lista de empresas y salir.
- `SqlServer/Rls/SoporteConRlsSqlServerTests` (LocalDB, política encendida): el administrador crea, edita y elimina una empresa de un
  tenant sin tenant en su sesión (y sin fijarlo la base lo rechaza); con tenant de soporte ve solo los datos de ese tenant, sin él
  ninguno, y cambiarlo cambia lo que entrega la base.
- Recorrido de la aplicación sobre una copia con 025 a 037 y la política encendida: la redirección, el cambio entre tres tenants
  (26 y 18 filas de personas en Transgar y Demo), la edición de usuarios de un tenant y de otro, el alta, edición y baja de una
  empresa, y que los usuarios de empresa siguen igual y no entran al soporte.
