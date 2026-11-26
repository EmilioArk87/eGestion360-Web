# Plan Secundario — Menú Configurable por Usuario (`eGestión360`)

> **Tipo:** Subplan independiente del roadmap principal F0–F9 ([Arquitectura/README.md](Arquitectura/README.md)).  
> **Estado:** Implementado en código (rama `claude/project-thread-i9gplk`). Script `048_cuentas_estilo_menu.sql` **pendiente de aplicar** en `eBD_SPD` (requiere `/alerta-bd`); `dotnet test` pendiente de correr.  
> **Fecha:** 2026-11-21  
> **Referencia visual:** `1 - Documetacion/Prototipos/menu-prototipos.html` (no se publica con el sitio)

---

## 1. Objetivo

Mejorar la experiencia de navegación de **eGestión360** reduciendo la cantidad de clics para llegar a cada pantalla y permitiendo que **cada usuario elija el estilo de menú** con el que trabaja mejor, manteniendo una única fuente de verdad para módulos, permisos y contexto multitenant.

---

## 2. Decisiones ya tomadas

| Tema | Decisión |
|---|---|
| **Persistencia de preferencia** | Columna `estilo_menu` en `saas.cuentas` (cuenta global de identidad, ADR-003 / ADR-027). |
| **Estilo predeterminado** | **`hub`** — Prototipo 4 (*Hub Renovado + Búsqueda y Accesos Frecuentes*). |
| **Estilos habilitados en v1** | 1. `hub` (Hub Renovado + Búsqueda — **default**)<br>2. `sidebar` (Sidebar Lateral Colapsable + `Ctrl+K`)<br>3. `topbar` (Topbar con Mega-Menú Desplegable, ancho completo) |
| **Estilos opcionales futuros** | `doble_rail` (Prototipo 2) y `launcher` (Prototipo 5), conectables sin duplicar lógica de navegación. |
| **Control de cambios en BD** | Antes de ejecutar cualquier script en `eBD_SPD`, disparar `/alerta-bd` y esperar aprobación explícita. |

---

## 3. Arquitectura propuesta

### 3.1. Catálogo único de navegación
En lugar de duplicar enlaces y reglas en cada diseño de menú, se centraliza la definición en un servicio C# (`Services/Navegacion/`):

- **`ModeloNavegacion`**: define grupos (`Operación`, `Empresa`, `Plataforma`), módulos (Flota, Facturación, Catálogos, Psicología, etc.) y pantallas hijas (ruta Razor, título, descripción corta, icono, color, estado `proximamente`).
- **`INavegacionService` / `CatalogoDeNavegacion`**: construye el árbol visible para la petición actual aplicando:
  1. Módulos contratados/activos de la empresa actual (`EmpresaModulo` / derechos de uso, ADR-026).
  2. Permisos por acción del usuario (evaluador de permisos, ADR-029 — mismo criterio que `eg-si-puede-abrir`).
  3. Contexto de plataforma y tenant de soporte (ADR-025).

Así, al agregar un módulo nuevo (Bancos, Contabilidad, Inventario, Ventas, Reportes), se registra en **un solo lugar** y aparece automáticamente en todos los estilos.

### 3.2. Preferencia en `saas.cuentas` + Claim en cookie
- Se agrega `estilo_menu VARCHAR(20) NOT NULL CONSTRAINT DF_cuentas_estilo_menu DEFAULT 'hub'` con `CHECK (estilo_menu IN ('hub', 'sidebar', 'topbar'))` en `saas.cuentas`.
- Se expone en `Models/Plataforma/Identidad.cs` (`Cuenta.EstiloMenu`).
- Se incluye como claim en la cookie de autenticación (`ADR-027`) para leerlo desde `AuthHelper.GetEstiloMenu(HttpContext)` sin consultar la BD en cada renderizado de `_Layout.cshtml`.
- Fallback seguro: si el claim falta o contiene un valor no reconocido, se usa siempre `'hub'`.

---

## 4. Etapas de implementación

### Etapa 1 — Base de datos y capa de identidad
- [x] Crear script de migración en `2 - Script SQL/`: `048_cuentas_estilo_menu.sql` (el 046 es de F1.8c y el 047 del motor de reportes) + fila en `1 - Documetacion/INDICE_SCRIPTS_SQL.md` + `verificar_despues_f15.sql` acepta la columna nueva (`con048`).
- [ ] Ejecutar `/alerta-bd` antes de aplicar el script en `eBD_SPD`.
- [x] Agregar `EstiloMenu` a `Cuenta` (`Models/Plataforma/Identidad.cs`) con valor por defecto `"hub"`.
- [x] Agregar el claim de estilo de menú en `ReclamosDeIdentidad`, emisión de cookie en login/cambio de contexto y helper `AuthHelper.GetEstiloMenu(HttpContext)`.

### Etapa 2 — Servicio central de navegación y buscador `Ctrl+K`
- [x] Crear `Services/Navegacion/ModeloNavegacion.cs`, `INavegacionService.cs` y `CatalogoDeNavegacion.cs`.
- [x] Migrar las definiciones actuales de `Pages/MainMenu.cshtml`, `Pages/Flota/Index.cshtml`, `Pages/Catalogos/Index.cshtml`, `Pages/Facturacion/Index.cshtml` y `Pages/Psicologia/Index.cshtml` al catálogo central (sin eliminar las páginas índice existentes para mantener compatibilidad con enlaces y bookmarks).
- [x] Exponer los datos filtrados para el buscador rápido (`Ctrl+K`) respetando los permisos del usuario.

### Etapa 3 — Vistas parciales por estilo y `_Layout.cshtml`
- [x] Crear estilos dedicados en `wwwroot/css/menu.css` y comportamiento en `wwwroot/js/menu.js` (colapsado del sidebar en `localStorage`, apertura de mega-menú, atajo `Ctrl+K`, cierre con `ESC`), usando la paleta oficial de eGestión360 (`#1B63C9`, `#12B8C6`, `#F5A524`).
- [x] Implementar vistas parciales en `Pages/Shared/Menu/`:
  - `_MenuHub.cshtml` (barra superior limpia + renovación de `Pages/MainMenu.cshtml` con buscador en vivo, chips de frecuentes, filtros por categoría y tarjetas con sub-enlaces directos).
  - `_MenuSidebar.cshtml` (sidebar colapsable, acordeones por módulo, topbar con breadcrumb y `Ctrl+K`).
  - `_MenuTopbarMega.cshtml` (topbar horizontal con mega-menú por columnas y barra secundaria del módulo activo).
- [x] Adaptar `Pages/Shared/_Layout.cshtml` para renderizar el parcial según `AuthHelper.GetEstiloMenu(Context)`.

### Etapa 4 — Selector de estilo de menú para el usuario
- [x] Crear endpoint/página handler protegido con `[ValidateAntiForgeryToken]` (ej. `Pages/Preferencias/EstiloMenu.cshtml.cs` o handler POST en un endpoint dedicado).
- [x] Validar que el usuario esté autenticado y actualizar **únicamente** su propia cuenta (`AuthHelper.GetCuentaId(HttpContext)`).
- [x] Validar el valor recibido contra la lista blanca (`hub`, `sidebar`, `topbar`).
- [x] Persistir en `saas.cuentas.estilo_menu`, re-emitir la cookie de autenticación con el nuevo claim (sin cerrar sesión) y redirigir a `ReturnUrl` (validado con `Url.IsLocalUrl`).
- [x] Agregar el selector **"Estilo de menú"** dentro del menú desplegable de usuario en `_NavigationPartial.cshtml` (y en la vista de Perfil cuando se habilite).

### Etapa 5 — Pruebas y verificación
- [x] Pruebas unitarias en `eGestion360Web.Tests` (escritas; falta correr `dotnet test`):
  - `CatalogoDeNavegacion` solo devuelve módulos activos y pantallas permitidas por `ADR-029`.
  - Fallback a `hub` ante valores nulos o inválidos.
  - Cambio de `estilo_menu` actualiza solo la cuenta del usuario autenticado y rechaza valores fuera de catálogo.
- [ ] Verificación visual y funcional:
  - Probar con `operador` (plataforma y con tenant de soporte activo), `empresa_admin` y `empresa_user`.
  - Verificar que las vistas existentes (`container` y `container-fluid`, tablas paginadas, Control de Salidas, Facturas) se vean correctamente en los 3 estilos y en móvil/tablet.

---

## 4b. Notas de la implementación

- **Ejecutar 048 antes de publicar.** El modelo `Cuenta` mapea `estilo_menu`; sin la columna, el inicio de sesión falla.
- **Migración de centros:** `Pages/Catalogos/Index.cshtml` y `Pages/Psicologia/Index.cshtml` pintan sus tarjetas desde el catálogo (`Pages/Shared/Menu/_TarjetasDeModulo.cshtml`). `Pages/Flota/Index.cshtml` se dejó igual (sus tarjetas llevan a subcentros, no a pantallas) y `Pages/Facturacion/Index.cshtml` es la lista de facturas, no un menú.
- **Buscador Ctrl+K:** no hay endpoint; el layout escribe en la página (`#egm-datos`) solo las pantallas que la persona puede abrir.
- **Frecuentes del hub:** `localStorage` por cuenta (`eg360.frecuentes.<id_cuenta>`), cruzado siempre con la lista permitida; sin tabla nueva.
- **Perfil:** el enlace «Perfil y preferencias» del menú de usuario abre `/Preferencias/EstiloMenu`.

## 5. Riesgos y cuidados técnicos

1. **Seguridad en `saas.cuentas`:** al ser tabla del plano de control sin RLS (ADR-003), el `UPDATE` debe filtrar obligatoriamente por `id_cuenta` obtenido de la cookie autenticada, nunca por un ID enviado desde el formulario.
2. **Compatibilidad con pruebas SQLite (`eGestion360Web.Tests`):** asegurar que la propiedad `EstiloMenu` tenga valor por defecto en el modelo C# y en `OnModelCreating` para que los tests en memoria no fallen por nulos.
3. **Coexistencia con `MainMenu.cshtml`:** en los estilos `sidebar` y `topbar`, entrar a `/MainMenu` debe seguir funcionando como pantalla de bienvenida/resumen (no quedar rota ni vacía).
4. **Accesos frecuentes en `hub`:** en v1 pueden calcularse a partir de las pantallas permitidas más comunes del usuario (o guardarse en `localStorage` del navegador), evitando crear otra tabla en BD en esta primera iteración.

---

## 6. Criterio de cierre

El subplan se considera completado cuando:
- Cualquier cuenta nueva o existente arranca automáticamente con el estilo **`hub`** (Hub Renovado + Búsqueda).
- Cada usuario puede cambiar entre **`hub`**, **`sidebar`** y **`topbar`** desde su menú de usuario, el cambio aplica al instante y persiste al volver a iniciar sesión o cambiar de dispositivo.
- Todos los estilos respetan exactamente los mismos permisos (`ADR-029`), módulos activos por contrato y el indicador de tenant de soporte.
- `dotnet test` pasa en verde.
