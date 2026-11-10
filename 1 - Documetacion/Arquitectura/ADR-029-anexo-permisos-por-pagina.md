# ADR-029 · Anexo: permiso que pide cada pantalla (F1.6b)

Complementa el [ADR-029](ADR-029-permisos-por-accion.md). Lista, pantalla por pantalla, el permiso del catálogo (`saas.permisos`) que declara
`[RequierePermiso("…")]` en su clase y los handlers que piden otro. Lo vigila `eGestion360Web.Tests/Arquitectura/PermisosDePaginasTests.cs`:
si una pantalla nueva no declara su permiso, o declara uno que no está en el catálogo, la prueba falla. Para cambiar un permiso se cambia el atributo
de la clase; esta tabla se rehace desde el código.

**86 pantallas piden un permiso y 24 no piden ninguno** (públicas, de la propia cuenta y centros de navegación). En total, 110 pantallas.

## Raíz

| Pantalla | Permiso de la pantalla | Handlers que piden otro permiso |
|---|---|---|
| `/DebugUsers` | `plataforma.diagnostico.ver` |  |
| `/ResetCodesHistory` | `plataforma.diagnostico.ver` |  |
| `/UserManagement` | `plataforma.usuarios.ver` | `OnPostAsync` → `plataforma.usuarios.restablecer_clave` |
| `/ValidarEmails` | `plataforma.diagnostico.ver` |  |

## Admin

| Pantalla | Permiso de la pantalla | Handlers que piden otro permiso |
|---|---|---|
| `/Admin/EmailConfig` | `plataforma.correo.ver` | `OnPostAsync` → `plataforma.correo.editar`<br>`OnPostTestAsync` → `plataforma.correo.editar`<br>`OnPostSetDefaultAsync` → `plataforma.correo.editar`<br>`OnPostToggleActiveAsync` → `plataforma.correo.editar`<br>`OnPostDeleteAsync` → `plataforma.correo.editar` |
| `/Admin/EmpresaModulos/Index` | `plataforma.modulos.ver` | `OnPostAsync` → `plataforma.modulos.editar` |
| `/Admin/EmpresaRoles/Index` | `administracion.roles.ver` | `OnPostCrearAsync` → `administracion.roles.crear`<br>`OnPostToggleAsync` → `administracion.roles.editar` |
| `/Admin/EmpresaRoles/Permisos` | `administracion.roles.ver` | `OnPostAsync` → `administracion.roles.editar` |
| `/Admin/Invitaciones/Create` | `administracion.invitaciones.crear` |  |
| `/Admin/Invitaciones/Index` | `administracion.invitaciones.ver` | `OnPostReenviarAsync` → `administracion.invitaciones.crear`<br>`OnPostAnularAsync` → `administracion.invitaciones.anular` |
| `/Admin/Modulos/Index` | `plataforma.modulos.ver` | `OnPostAsync` → `plataforma.modulos.editar` |
| `/Admin/Outbox/Details` | `plataforma.outbox.ver` | `OnPostRetryAsync` → `plataforma.outbox.gestionar`<br>`OnPostMarkProcessedAsync` → `plataforma.outbox.gestionar` |
| `/Admin/Outbox/Index` | `plataforma.outbox.ver` | `OnPostResetDeadAsync` → `plataforma.outbox.gestionar` |
| `/Admin/Personas/Edit` | `plataforma.personas.editar` |  |
| `/Admin/Personas/Historial` | `plataforma.personas.ver` |  |
| `/Admin/Personas/Index` | `plataforma.personas.ver` |  |
| `/Admin/ResetPassword` | `plataforma.usuarios.restablecer_clave` |  |
| `/Admin/RolesPermisos/Index` | `administracion.roles.ver` | `OnPostCrearAsync` → `administracion.roles.crear`<br>`OnPostEditarAsync` → `administracion.roles.editar`<br>`OnPostToggleAsync` → `administracion.roles.editar` |
| `/Admin/Soporte/Index` | `plataforma.soporte.ver` | `OnPostEntrarAsync` → `plataforma.soporte.entrar`<br>`OnPostEntrarEnEmpresaAsync` → `plataforma.soporte.entrar` |
| `/Admin/Usuarios/Create` | `administracion.usuarios.crear` |  |
| `/Admin/Usuarios/Edit` | `administracion.usuarios.editar` |  |

## Catálogos

| Pantalla | Permiso de la pantalla | Handlers que piden otro permiso |
|---|---|---|
| `/Catalogos/Clientes/Create` | `catalogos.clientes.crear` |  |
| `/Catalogos/Clientes/Delete` | `catalogos.clientes.eliminar` |  |
| `/Catalogos/Clientes/Edit` | `catalogos.clientes.editar` |  |
| `/Catalogos/Clientes/Index` | `catalogos.clientes.ver` |  |
| `/Catalogos/TiposCambio/Bitacora` | `catalogos.tasas_cambio.ver` |  |
| `/Catalogos/TiposCambio/Configuracion` | `catalogos.tasas_cambio.configurar` |  |
| `/Catalogos/TiposCambio/Historico` | `catalogos.tasas_cambio.ver` |  |
| `/Catalogos/TiposCambio/Index` | `catalogos.tasas_cambio.ver` | `OnPostRegistrarAsync` → `catalogos.tasas_cambio.crear`<br>`OnPostAprobarAsync` → `catalogos.tasas_cambio.aprobar`<br>`OnPostRechazarAsync` → `catalogos.tasas_cambio.aprobar`<br>`OnPostEjecutar` → `catalogos.tasas_cambio.configurar` |
| `/Catalogos/TiposCambio/Tendencias` | `catalogos.tasas_cambio.ver` |  |

## Empresas

| Pantalla | Permiso de la pantalla | Handlers que piden otro permiso |
|---|---|---|
| `/Empresas/Create` | `plataforma.empresas.crear` |  |
| `/Empresas/Delete` | `plataforma.empresas.eliminar` |  |
| `/Empresas/Details` | `plataforma.empresas.ver` |  |
| `/Empresas/Edit` | `plataforma.empresas.editar` |  |
| `/Empresas/Index` | `plataforma.empresas.ver` |  |

## Facturación

| Pantalla | Permiso de la pantalla | Handlers que piden otro permiso |
|---|---|---|
| `/Facturacion/Anular` | `facturacion.factura.anular` |  |
| `/Facturacion/Create` | `facturacion.factura.crear` |  |
| `/Facturacion/CxC/Index` | `facturacion.cxc.ver` |  |
| `/Facturacion/Details` | `facturacion.factura.ver` |  |
| `/Facturacion/Index` | `facturacion.factura.ver` |  |
| `/Facturacion/Notas/Create` | `facturacion.nota.crear` |  |
| `/Facturacion/Notas/Index` | `facturacion.nota.ver` |  |
| `/Facturacion/Pagos/Aplicar` | `facturacion.pago.aplicar` |  |
| `/Facturacion/Pagos/Create` | `facturacion.pago.crear` |  |
| `/Facturacion/Pagos/Details` | `facturacion.pago.ver` | `OnPostAnularAsync` → `facturacion.pago.anular` |
| `/Facturacion/Pagos/Index` | `facturacion.pago.ver` |  |

## Flota

| Pantalla | Permiso de la pantalla | Handlers que piden otro permiso |
|---|---|---|
| `/Flota/Catalogos/Cargos/Create` | `flota.catalogos.crear` |  |
| `/Flota/Catalogos/Cargos/Edit` | `flota.catalogos.editar` |  |
| `/Flota/Catalogos/Cargos/Index` | `flota.catalogos.ver` |  |
| `/Flota/Catalogos/CategoriasRepuesto/Create` | `flota.catalogos.crear` |  |
| `/Flota/Catalogos/CategoriasRepuesto/Edit` | `flota.catalogos.editar` |  |
| `/Flota/Catalogos/CategoriasRepuesto/Index` | `flota.catalogos.ver` |  |
| `/Flota/Catalogos/Personas/Create` | `flota.personas.crear` |  |
| `/Flota/Catalogos/Personas/Edit` | `flota.personas.editar` |  |
| `/Flota/Catalogos/Personas/Historial` | `flota.personas.ver` |  |
| `/Flota/Catalogos/Personas/Index` | `flota.personas.ver` | `OnPostToggleAsync` → `flota.personas.editar` |
| `/Flota/Catalogos/Rutas/Create` | `flota.catalogos.crear` |  |
| `/Flota/Catalogos/Rutas/Edit` | `flota.catalogos.editar` |  |
| `/Flota/Catalogos/Rutas/Index` | `flota.catalogos.ver` |  |
| `/Flota/Catalogos/Talleres/Create` | `flota.catalogos.crear` |  |
| `/Flota/Catalogos/Talleres/Edit` | `flota.catalogos.editar` |  |
| `/Flota/Catalogos/Talleres/Index` | `flota.catalogos.ver` |  |
| `/Flota/Catalogos/TiposVehiculo/Create` | `flota.catalogos.crear` |  |
| `/Flota/Catalogos/TiposVehiculo/Edit` | `flota.catalogos.editar` |  |
| `/Flota/Catalogos/TiposVehiculo/Index` | `flota.catalogos.ver` |  |
| `/Flota/Catalogos/Vehiculos/Create` | `flota.vehiculos.crear` |  |
| `/Flota/Catalogos/Vehiculos/Delete` | `flota.vehiculos.eliminar` |  |
| `/Flota/Catalogos/Vehiculos/Edit` | `flota.vehiculos.editar` |  |
| `/Flota/Catalogos/Vehiculos/Index` | `flota.vehiculos.ver` |  |
| `/Flota/Gastos/Mantenimiento/Create` | `flota.mantenimiento.crear` |  |
| `/Flota/Gastos/Mantenimiento/Index` | `flota.mantenimiento.ver` |  |
| `/Flota/Gastos/Repuestos/Create` | `flota.repuestos.crear` |  |
| `/Flota/Gastos/Repuestos/Index` | `flota.repuestos.ver` |  |
| `/Flota/Gastos/Seguros/Create` | `flota.seguros.crear` |  |
| `/Flota/Gastos/Seguros/Index` | `flota.seguros.ver` |  |
| `/Flota/KPI/Dashboard` | `flota.kpi.ver` |  |
| `/Flota/KPI/Graficos` | `flota.kpi.ver` |  |
| `/Flota/Operacion/CargasCombustible/Create` | `flota.combustible.crear` |  |
| `/Flota/Operacion/CargasCombustible/Index` | `flota.combustible.ver` |  |
| `/Flota/Operacion/ControlSalidas/Historial` | `flota.salidas.ver` | `OnPostAnularAsync` → `flota.salidas.anular` |
| `/Flota/Operacion/ControlSalidas/Index` | `flota.salidas.ver` | `OnPostSalidaAsync` → `flota.salidas.crear`<br>`OnPostEntradaAsync` → `flota.salidas.editar` |
| `/Flota/Operacion/Peajes/Create` | `flota.peajes.crear` |  |
| `/Flota/Operacion/Peajes/Edit` | `flota.peajes.editar` |  |
| `/Flota/Operacion/Peajes/Index` | `flota.peajes.ver` |  |
| `/Flota/Operacion/SalariosDiarios/Create` | `flota.salarios.crear` |  |
| `/Flota/Operacion/SalariosDiarios/Index` | `flota.salarios.ver` |  |

## Pantallas que no piden permiso

| Pantalla | Motivo |
|---|---|
| `/About` | Información pública del producto |
| `/AceptarInvitacion` | Se entra con el enlace de la invitación, antes de tener sesión |
| `/ChangePassword` | Cada persona cambia su propia clave |
| `/Contact` | Información pública de contacto |
| `/Error` | Página de error |
| `/ForgotPassword` | Recuperar la clave, antes de tener sesión |
| `/Index` | Portada pública |
| `/Login` | Inicio de sesión |
| `/Logout` | Cerrar la propia sesión |
| `/MainMenu` | Centro de navegación: cada opción pide su propio permiso |
| `/Mfa` | Segundo factor del inicio de sesión |
| `/MfaAlta` | Alta del segundo factor de la propia cuenta |
| `/Products` | Información pública (hoy oculta: sin ruta) |
| `/Register` | Registro, antes de tener sesión |
| `/ResetPassword` | Restablecer la clave con un código, antes de tener sesión |
| `/SeleccionarContexto` | Elegir con qué empresa se trabaja |
| `/SinAcceso` | Explica por qué no se puede entrar |
| `/Catalogos/Index` | Centro de navegación del módulo: cada opción pide su propio permiso |
| `/Flota/Catalogos/Index` | Centro de navegación del módulo: cada opción pide su propio permiso |
| `/Flota/Gastos/Index` | Centro de navegación del módulo: cada opción pide su propio permiso |
| `/Flota/Index` | Centro de navegación del módulo: cada opción pide su propio permiso |
| `/Flota/MantenimientoDatos/Index` | Centro de navegación del módulo: cada opción pide su propio permiso |
| `/Flota/Operacion/Index` | Centro de navegación del módulo: cada opción pide su propio permiso |
| `/Flota/SinEmpresa` | Explica por qué Flota no abre sin empresa |

Un centro de navegación no pide permiso porque solo ofrece enlaces: cada pantalla a la que lleva pide el suyo. `/Products` está hoy oculta (no tiene ruta) y queda marcada para que, si se vuelve a mostrar, ya tenga decidido su permiso.

## Dos huecos del catálogo que esta lista deja a la vista

- **Eliminar un peaje** (`OnPostDeleteAsync` de `/Flota/Operacion/Peajes/Edit`) no tiene permiso propio en el catálogo: hoy cuelga de `flota.peajes.editar`. Si se quiere separar, hace falta un permiso nuevo y un script.
- **Ver los usuarios de la empresa** (`administracion.usuarios.ver`) no lo pide ninguna pantalla: el directorio de usuarios con todas las cuentas es `/UserManagement`, que es de la plataforma. Se decide cuando se revisen las pantallas de administración (F1.6c).
