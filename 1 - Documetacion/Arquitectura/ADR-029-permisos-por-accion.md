# ADR-029 · Permisos por acción (F1.6)

- **Estado:** Aceptada por Emilio el 2026-11-03 (decisiones D57 a D65 del plan de la etapa F1.6, sin cambios sobre lo recomendado). F1.6a (catálogo, roles y evaluador) escrita y ensayada; script 041 aplicado y verificado en producción el 2026-11-09
- **Fecha:** 2026-11-03
- **Decidió:** Emilio (D57 a D65). Diseño y ejecución de Claude
- **Fase:** F1.6 (entrega F1.6a)
- **Desarrolla:** [ADR-003](ADR-003-identidad-y-membresias.md) (catálogo `modulo.recurso.accion`) y [ADR-008](ADR-008-autenticacion-cookie-claims.md) (una evaluación por petición para páginas, menú y botones)

## Contexto

Cada pantalla decide hoy a su manera quién entra: unas preguntan si es administrador (44 comprobaciones `IsAdmin(` en 29 archivos), otras si
tiene el módulo, y las de Flota casi ninguna pregunta qué puede hacer (2 de 46 páginas consultan `PuedeCrear` o `PuedeEditar`). Los permisos
son 4 casillas por módulo y se copian a la sesión al entrar. ADR-003 y ADR-008 ya fijaron el destino: un catálogo de permisos con la forma
`modulo.recurso.accion`, roles que los reciben y una sola pregunta, `Puede(permiso)`, evaluada en cada petición.

## Decisión

| # | Decisión | Cómo queda en F1.6a |
|---|----------|---------------------|
| D57 | **Catálogo fino**: un recurso por pantalla o grupo de pantallas, con ver, crear, editar y eliminar y acciones propias donde ya hacen falta (anular, aplicar, aprobar, configurar…) | 72 permisos: 17 de plataforma, 9 de administración de la empresa, 28 de flota, 8 de catálogos y 10 de facturación (el plan estimó «unos 50»; los hubs y los detalles no necesitan permiso propio, pero las pantallas de plataforma y los recursos de flota no se pueden agrupar más sin perder la separación que D57 pide) |
| D58 | **Dónde viven**: catálogo y plantillas en `saas` (globales, sin RLS) y los permisos de cada rol en `dbo.rol_permisos` (tabla de tenant con RLS) | `saas.permisos`, `saas.roles_plantilla`, `saas.roles_plantilla_permisos` y `dbo.rol_permisos`. La tabla vieja `empresa_rol_permisos` no se toca y se conserva hasta F1.10 (expandir y contraer) |
| D59 | **Cómo se aplican**: filtro de páginas con `[RequierePermiso]` y valores por convención del nombre de página, más una prueba que vigila que ninguna página quede sin permiso | Fuera de F1.6a: llega en F1.6b. El catálogo ya trae los permisos que esas páginas necesitarán |
| D60 | **Administradores y operadores**: el administrador de empresa recibe todos los permisos de sus módulos activos; el operador, los de plataforma; ningún permiso se concede por el texto del rol legado `admin` | El evaluador da al operador **todo el catálogo** (hoy `IsAdmin` pasa cualquier guarda, también dentro del tenant de soporte), al administrador de empresa su administración y los módulos habilitados, y a un usuario con rol lo que su rol concede. Un rol de empresa **nunca** recibe permisos de plataforma (restricción `CK_rol_permisos_ambito`) |
| D61 | **Por petición, con memoria de 30 s** e invalidación al guardar un rol | `EvaluadorDePermisos.SegundosEnMemoria = 30`; `Invalidar(idTenant)` cambia un número de versión por tenant, así que invalidar un tenant no toca la memoria de otro. La sesión deja de guardar permisos en F1.6e |
| D62 | **Roles de Flota**: aplicar lo que cada rol tiene configurado, tras revisar contigo los 7 roles uno por uno | Fuera de F1.6a: la revisión es antes de F1.6d |
| D63 | **Orden para retirar `IsAdmin(`**: plataforma, administración de la empresa, módulos; `AdminOnlyPageFilter` e `IsAdmin` se retiran en F1.6e | Fuera de F1.6a |
| D64 | **Roles plantilla** para tenants nuevos: tres por módulo (Administrador, Operador, Consulta) | 9 plantillas con 102 permisos. Administrador = todos los permisos del módulo; Operador = los de casilla ver, crear o editar (sin eliminar ni anular ni los permisos nuevos); Consulta = los de casilla ver. Una clave compuesta obliga a que un permiso de plantilla sea del módulo de la plantilla |
| D65 | **Textos dañados de los módulos**: corregir nombre y descripción de `dbo.modulos` en el script | El 041 corrige las filas de catálogos, facturación y bancos que tengan el daño (4 textos hoy); los códigos no cambian |

### Reglas del evaluador y de la migración

1. **Cada permiso de módulo lleva `accion_legada`**: la casilla de antes (ver, crear, editar o eliminar) que lo concede. La migración convierte cada casilla marcada
   de `empresa_rol_permisos` en todos los permisos de ese módulo con esa casilla; así **nadie gana ni pierde permisos al migrar**. Los permisos nuevos que antes
   no existían como casilla (aprobar y configurar tasas de cambio) no se conceden por migración: los tiene el administrador, y el rol que los necesite los recibe a mano.
   `anular` factura, anular pago y anular una salida cuelgan de «eliminar», porque hoy esas pantallas piden `PuedeEliminar`.
2. **El administrador de empresa se decide por el texto del rol, no por la casilla `es_admin` del rol de empresa.** Hoy `AuthHelper` ya decide así. En producción los
   usuarios del rol «Administrador» de Transgar (`es_admin = 1`) entran como `empresa_user`: tratarlos como administradores les daría administración de usuarios,
   roles e invitaciones que hoy no tienen. Se resuelve en la revisión de los 7 roles (antes de F1.6d): o esos usuarios pasan a administrador de empresa, o el rol deja de ser `es_admin`.
3. **Los permisos de módulo solo valen si el módulo está habilitado** (el plan lo concede y la empresa lo tiene activado), igual que `HasModulo`. Los de administración de la empresa no dependen de módulos.
4. **Sin rol o sin tenant, ningún permiso**, salvo el operador.
5. **Tres barreras de tenant** al leer los permisos de un rol: el filtro de EF, la política RLS y la condición explícita `id_tenant` de la consulta; la clave de la memoria lleva el tenant.

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| 1 | Seguir con 4 casillas por módulo | No permite «anular» ni «aplicar» por separado, ni dar «aprobar tasas» sin dar «eliminar». |
| 2 | `AuthorizeFolder` por carpeta de ASP.NET Core | Más corto, pero no distingue crear de editar de eliminar dentro de una carpeta. |
| 3 | Guardar los permisos en la cookie o en la sesión | Un cambio de rol no valdría hasta el siguiente inicio de sesión (el defecto que ADR-008 quiere quitar). |
| 4 | Reemplazar `empresa_rol_permisos` en el mismo script | Rompería la versión publicada y la reversa; con expandir y contraer, la tabla vieja sigue sirviendo hasta F1.10. |
| 5 | Dar `es_admin` el poder de administrador desde el primer día | Daría poderes nuevos a cuentas que hoy no los tienen (ver regla 2). |

## Consecuencias

- **F1.6a no cambia lo que ve ni puede hacer nadie**: el evaluador queda registrado (`IEvaluadorDePermisos`) y nada lo llama todavía; las tablas nuevas las ignora el código publicado.
- **Orden obligatorio para publicar**: aplicar el script 041 (con `/alerta-bd`) y comprobarlo con `verificar_despues_041.sql`; el código que lee las tablas nuevas va después (F1.6b).
- Los verificadores de F1.5 y F1.7 (`verificar_despues_f15.sql`, `verificar_despues_f17.sql`) aceptan las cifras con o sin el 041; la comprobación diaria corre además `verificar_despues_041.sql` cuando el 041 está aplicado.
- Si algo falla: el bloque ROLLBACK del script quita las 4 tablas y los 5 predicados (se detiene si alguien ya editó permisos de un rol), y la aplicación publicada sigue funcionando porque no usa nada de esto.
- Hallazgos para la revisión de roles: el rol «Flota» de SIP (id 2) no tiene permisos (sigue sin ellos tras migrar); los 7 roles de producción tienen 7 filas de casillas (no 6, como estimó el plan), que se convierten en 143 permisos.
