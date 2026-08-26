# Estándares del ERP — eGestion360-Web

> Documento canónico de convenciones globales del ERP. Toda funcionalidad nueva (empezando por el módulo
> contable) debe seguir estos estándares. Está derivado del **proyecto real**, no de un diseño ideal en papel:
> refleja lo que hoy existe en el repositorio y corrige suposiciones que circulan en prompts previos.

**Última revisión:** 2026-08-06 · **BD:** `eBD_SPD` (SQL Server) · **Rama de referencia:** `Modulos_y_contabilidad_2.0`

---

## 1. Propósito y alcance

Mantener el ERP ordenado a medida que crece (cientos de tablas, servicios y páginas). Este documento fija:

- El **stack real** y la **arquitectura por capas** que ya usa el proyecto.
- Las **convenciones de nombres** de base de datos y código.
- El estándar de **auditoría** y **multitenant**.
- El **proceso de control de cambios** de esquema (compatible con las skills `/doc-db` y `/skill-db`).

Si algún prompt, plantilla o documento contradice esto, **manda este documento**.

---

## 2. Stack tecnológico real

| Capa | Tecnología real |
|---|---|
| Runtime | **.NET 8** (`net8.0`, `Microsoft.NET.Sdk.Web`, `Nullable` + `ImplicitUsings` on) |
| Web | **ASP.NET Core Razor Pages** (`Pages/`, code-behind `PageModel`). **No hay `Controllers/`.** |
| ORM | **Entity Framework Core 9.0.9** (LINQ, `ApplicationDbContext`, Migrations). **No usa Dapper.** |
| BD | **SQL Server** (`eBD_SPD`, hosting somee.com). Provider secundario: SQLite. |
| UI | **Bootstrap 5**, **jQuery** + `jquery-validation(-unobtrusive)`, **Font Awesome** |
| Seguridad | Sesión propia + **BCrypt.Net-Next 4.1.0** (no ASP.NET Identity) |
| Correo | **MailKit 4.16.0** |
| Excel | **ClosedXML 0.102.2** |

> ⚠️ **Corrección importante.** Prompts antiguos asumen *ASP.NET Core MVC + Dapper + Repository/UnitOfWork*.
> **Eso no aplica aquí.** El proyecto es Razor Pages + EF Core. No introducir Dapper, controladores MVC ni un
> Repository/UnitOfWork paralelo: `DbContext` + `SaveChangesAsync` ya actúa como Unit of Work.

---

## 3. Arquitectura por capas (real)

Solución de **un solo proyecto** (`eGestion360Web.csproj`). Las "capas" son carpetas:

| Carpeta | Rol |
|---|---|
| `Data/ApplicationDbContext.cs` | Único `DbContext`: `DbSet<>`, `OnModelCreating` (claves, índices únicos, precisión decimal, FKs, `HasData` de seeds). |
| `Models/` | Entidades EF por dominio: `Catalogos/`, `Facturacion/`, `Flota/`, `Eventos/`. Algunos ViewModels sueltos. |
| `Services/` | Lógica de negocio/infra: interfaz + implementación, registradas por **DI** en `Program.cs`. Subcarpetas `Eventos/`, `Facturacion/`. |
| `Pages/<Modulo>/` | Razor Pages por módulo (`Admin/`, `Catalogos/`, `Facturacion/`, `Flota/`, `Empresas/`, `Shared/`). |
| `Migrations/` | Migraciones EF Core (C#). |
| `wwwroot/` | Estáticos. |

**Patrón de acceso:** `PageModel` inyecta `ApplicationDbContext` y/o `Services`. La lógica de negocio no trivial
(emisión, cálculos, generación de asientos) vive en un **Service** con interfaz, no en el code-behind ni en SQL.

**Eventos de dominio / Outbox:** `Services/Eventos/` implementa un outbox transaccional sobre la tabla
`domain_events`, con `OutboxDispatcherBackgroundService` que entrega eventos a los `IDomainEventHandler`
registrados en DI. Es la vía oficial para reacciones entre módulos (p. ej. contabilidad reaccionando a facturación).

---

## 4. Multitenant (multiempresa)

Modelo **single-DB / shared-schema**, discriminado por columna:

- Toda tabla de negocio lleva `id_empresa INT NOT NULL` con **FK a `dbo.empresas(id_empresa)`**.
- El tenant activo se guarda en **sesión** y se lee con `Services/AuthHelper.cs` (`GetEmpresaId`, `SetSesionTenant`).
- **Filtrado manual obligatorio:** cada consulta debe incluir `.Where(x => x.IdEmpresa == empresaId)`. Hoy **no** hay
  Global Query Filters (`HasQueryFilter`); el aislamiento depende de que cada página filtre. ⚠️ Nunca devolver datos sin
  filtrar por empresa.
- **Índices por `id_empresa`:** todo índice de consulta debe empezar por `id_empresa` (ej. `IX_facturas_empresa_fecha`).
- **Sucursales:** hoy **no existen** (`id_sucursal` no está en el modelo). Queda como columna **opcional/futura**;
  no inventar tablas de sucursal salvo que se decida explícitamente.

---

## 5. Convención de nombres de base de datos

Regla base observada en `2 - Script SQL/F0_*.sql` y `F1_*.sql`:

- **Tablas:** `snake_case` en **español**, en **plural**, esquema **`dbo`**. Ej.: `facturas`, `factura_detalle`, `pagos`, `clientes`.
- **Columnas:** `snake_case`. **PK** = `id_<entidad>` (`id_factura`, `id_empresa`). **FK** = `id_<referencia>`.
- **Estados:** columna `estado NVARCHAR(...)` + `CHECK (estado IN ('...','...'))`. No usar tablas de estado ni enums numéricos.
- **Objetos** dentro de `CREATE TABLE`:

  | Objeto | Patrón | Ejemplo |
  |---|---|---|
  | Primary Key | `PK_<tabla>` | `PK_facturas` |
  | Foreign Key | `FK_<tabla>_<ref>` | `FK_facturas_cliente` |
  | Check | `CK_<tabla>_<campo>` | `CK_facturas_estado` |
  | Default | `DF_<abrev>_<campo>` | `DF_fac_estado` |
  | Índice único | `UX_<tabla>_<cols>` | `UX_facturas_empresa_serie_numero` |
  | Índice | `IX_<tabla>_<cols>` | `IX_facturas_empresa_fecha` |

- **Procedimientos** (solo infraestructura, no dominio): `sp_<PascalCase>` (ej. `sp_GetActiveEmailConfiguration`).
  La lógica de negocio **no** va en SP: va en `Services/`.
- **Triggers:** `TR_<Tabla>_<Evento>` (ej. `TR_EmailConfiguration_UpdatedAt`).
- **Vistas y funciones SQL:** hoy **no existen**. La palabra "Vistas" en la documentación se refiere a **vistas Razor**
  (`.cshtml`), no a `VIEW` de SQL. Si en el futuro se crean, proponer prefijos `VW_`/`FN_` y aprobarlos aquí primero.

### 5.1 Prefijos de módulo

El proyecto **legacy** (facturación, flota, seguridad) **no usa prefijos** de módulo. A partir del módulo contable se
adopta un **prefijo de módulo en snake_case** para las tablas **nuevas**, para agruparlas visualmente:

| Prefijo | Módulo | Estado |
|---|---|---|
| `ct_` | Contabilidad | **Adoptado** (`ct_cuentas`, `ct_asientos`, `ct_asiento_movimientos`, `ct_periodos`, …) |
| `inv_` | Inventario | Propuesto |
| `ban_` | Bancos | Propuesto |
| `cxc_` | Cuentas por Cobrar | Propuesto |
| `cxp_` | Cuentas por Pagar | Propuesto |
| `rh_` | Recursos Humanos | Propuesto |

> ⚠️ Esto **no** implica renombrar tablas existentes (`facturas`, `vehiculos`, `usuarios`, `empresas` se quedan como están).
> Convivirán tablas sin prefijo (legacy) y con prefijo (módulos nuevos). Es una decisión consciente.

### 5.2 Traducción del "Prompt Maestro" original → convención real

| Prompt original (idealizado) | Convención real de este ERP |
|---|---|
| `CT_Asientos`, `SEG_Usuarios` (PascalCase + prefijo mayúscula) | `ct_asientos`, `usuarios` (snake_case; prefijo solo módulos nuevos) |
| `IdEmpresa`, `IdSucursal` (columnas) | `id_empresa` (columna); sin `id_sucursal` por ahora |
| `SP_CT_RegistrarAsiento` | Lógica en `Services/Contabilidad/AsientoService.cs` (no SP) |
| `VW_CT_BalanceGeneral` | Consulta LINQ/proyección en Service o Razor Page (no VIEW SQL por defecto) |
| `FN_CT_SaldoCuenta` | Método en Service (no función SQL por defecto) |
| `UsuarioCreacion/FechaCreacion/EstadoRegistro` | `creado_por/fecha_creacion/activo/eliminado` (ver §6) |

---

## 6. Auditoría estándar

Toda tabla de dominio nueva debe incluir el mismo bloque de auditoría que usa el proyecto (ver `2 - Script SQL/F1_Facturacion.sql`):

```sql
activo               BIT            NOT NULL DEFAULT (1),   -- habilitado / deshabilitado (catálogos)
eliminado            BIT            NOT NULL DEFAULT (0),   -- soft-delete
fecha_eliminado      DATETIME2      NULL,
creado_por           NVARCHAR(100)  NOT NULL,
fecha_creacion       DATETIME2      NOT NULL DEFAULT (SYSUTCDATETIME()),
modificado_por       NVARCHAR(100)  NULL,
fecha_modificacion   DATETIME2      NULL,
token_concurrencia   ROWVERSION     NOT NULL                -- solo en tablas transaccionales
```

Para **documentos** (facturas, pagos, asientos) se añade:

```sql
estado               NVARCHAR(30)   NOT NULL,               -- + CHECK IN (...)
motivo_anulacion     NVARCHAR(500)  NULL,
fecha_anulacion      DATETIME2      NULL
```

**Reglas de oro:**

- **Nunca borrado físico** de información contable/financiera. Usar **anulación lógica** (`estado='anulado'` +
  `motivo_anulacion` + `fecha_anulacion`) o soft-delete (`eliminado=1`).
- Concurrencia optimista con `token_concurrencia ROWVERSION` (EF: `[Timestamp]`).
- `creado_por`/`modificado_por` = usuario de sesión (correo/usuario), no un id numérico.

---

## 7. Convención de código C#

- **Entidades EF** en PascalCase, mapeadas a tablas snake_case con `[Table("ct_asientos")]` / `[Column("id_asiento")]`
  (o Fluent en `OnModelCreating`). Ver ejemplos en `Data/ApplicationDbContext.cs`.
- **Services**: interfaz `IXxxService` + implementación `XxxService`, registradas con `AddScoped` en `Program.cs`.
  La lógica transaccional (validaciones, cuadre contable, generación de asientos) vive aquí, dentro de una transacción EF.
- **Razor Pages**: un `.cshtml` + un `.cshtml.cs` (`OnGetAsync`/`OnPostAsync`). Validación con DataAnnotations +
  `jquery-validation-unobtrusive`.
- **Seguridad/permisos**: usar `Services/AuthHelper.cs` (`PuedeVer/Crear/Editar/Eliminar` por módulo). Roles:
  `admin` (super-admin del sistema), `empresa_admin`, `empresa_user`. Permisos granulares por módulo en `EmpresaRolPermiso`.
- **Rendimiento**: evitar N+1 (usar `Include`/proyecciones `Select`), paginar listados, e índices que empiecen por
  `id_empresa`. Preferir consultas asíncronas (`ToListAsync`, `FirstOrDefaultAsync`).

---

## 8. Control de cambios de esquema (proceso oficial)

El proyecto gestiona el esquema de dominio con **scripts SQL hand-written idempotentes** (estilo `F0/F1`), además de las
migraciones EF. Todo cambio de BD sigue el proceso de la skill `/skill-db`:

1. **Un archivo por cambio**, en `2 - Script SQL/`, nombre `NNN_descripcion_corta.sql`:
   - `NNN` = 3 dígitos incrementales (leer el siguiente libre en el índice). **Nunca** reutilizar ni renumerar.
   - `descripcion_corta` = snake_case, máx. 5 palabras.
2. **Encabezado canónico** (variante `/skill-db`, la que exige el índice):

   ```sql
   -- ============================================================
   -- Script   : NNN_descripcion_corta.sql
   -- Proposito: descripcion breve
   -- Autor    : eGestion360-Web
   -- Fecha    : YYYY-MM-DD
   -- BD       : eBD_SPD
   -- Requiere : precondiciones o dependencias
   -- Rollback : descripcion breve de reversa
   -- ============================================================
   ```

3. **Cuatro secciones obligatorias:** `PRECHECK` (validaciones previas) · `CAMBIO` (DDL/DML idempotente,
   `IF OBJECT_ID(...) IS NULL`) · `POSTCHECK` (verificación) · `ROLLBACK` (reversa).
4. **Registrar de inmediato** en `1 - Documetacion/INDICE_SCRIPTS_SQL.md` (nueva fila al final, estado `Pendiente`).
   No modificar el estado de scripts anteriores ni sobrescribir scripts históricos ya ejecutados.
5. **Idempotencia y `SET`-options** al inicio del script (copiar de `F1_Facturacion.sql`).
6. Trazabilidad exigida: **hallazgo → script → índice**.
7. Antes de ejecutar cualquier script contra **producción**, confirmar con el responsable (ver skill `/alerta-bd`).

> **Nota de canonicidad.** Existen dos variantes de encabezado entre `/doc-db` y `/skill-db`. La **canónica es la de
> `/skill-db`** (autor `eGestion360-Web`, secciones PRECHECK/CAMBIO/POSTCHECK/ROLLBACK), porque es la que refuerza el
> índice maestro.

---

## 9. Documentación obligatoria

- **Cada vista Razor** (`.cshtml` + `.cshtml.cs`) se documenta en `1 - Documetacion/Vistas/<ruta_sin_extension>.md`
  con la plantilla canónica `1 - Documetacion/Vistas/_PlantillaVista.md` (secciones: Proposito · Ruta · Funcionalidad ·
  Flujo tecnico GET/POST · Dependencias · Reglas de negocio · Manejo de errores · Notas — títulos **sin tildes**).
- Todo documento nuevo se **enlaza desde** `1 - Documetacion/README.md`.
- El módulo contable, además, adjunta su prompt/diseño en `PROMPT_MAESTRO_CONTABILIDAD.md`.

---

## 10. Calidad

SOLID · DRY · KISS · Clean Code. Reutilizar catálogos centralizados (monedas, impuestos, formas/condiciones de pago,
países ya existen en `F0_Catalogos_Transversales.sql`); **no duplicarlos** por módulo. Parametrizar reglas (impuestos,
tasas, secuencias) en tablas, no en código, para soportar cambios legales futuros.

---

## Archivos de referencia

- Estilo DDL/auditoría/estados: `2 - Script SQL/F1_Facturacion.sql`, `F1_Pagos_y_Notas.sql`, `F0_Catalogos_Transversales.sql`, `F0_Outbox.sql`
- Control de cambios: `.claude/commands/skill-db.md`, `1 - Documetacion/INDICE_SCRIPTS_SQL.md`
- Plantilla de vistas: `1 - Documetacion/Vistas/_PlantillaVista.md`
- Multitenant/seguridad: `Data/ApplicationDbContext.cs`, `Models/Empresa.cs`, `Services/AuthHelper.cs`
- Outbox/eventos: `Services/Eventos/IDomainEventHandler.cs`, `Services/Eventos/DomainEvent.cs`
