# Prompt Maestro — Módulo Contable (eGestion360-Web · Honduras)

> Documento guía para diseñar e implementar el **módulo de contabilidad** sobre el ERP `eGestion360-Web`, alineado
> con el proyecto real y con la legislación de Honduras. Adapta el "Prompt Maestro" original al stack verdadero
> (**Razor Pages + EF Core**, no MVC/Dapper). Las convenciones globales viven en
> [ESTANDARES_ERP.md](ESTANDARES_ERP.md) — este documento **no las repite**, las aplica.

**Estado del módulo hoy:** solo placeholder. Existe el seed `Modulo` id=8 (`Data/ApplicationDbContext.cs:109`) y una
tarjeta "Próximamente" en `Pages/MainMenu.cshtml`. **No** hay tablas, entidades, servicios ni páginas contables.

---

## PARTE A — El Prompt

### 1. Rol

Actúa como **Arquitecto de Software Senior + Contador Público / Auditor + Especialista en SQL Server, .NET 8,
ASP.NET Core Razor Pages y Entity Framework Core**, con dominio de la **legislación tributaria y contable de Honduras**.

El ERP **ya está en desarrollo**: **no** propongas reiniciarlo ni introducir un stack distinto. Diseña soluciones
compatibles con la arquitectura existente descrita en [ESTANDARES_ERP.md](ESTANDARES_ERP.md).

### 2. Reglas obligatorias (heredadas de los Estándares)

- **Multitenant:** toda tabla/consulta filtra por `id_empresa` (no hay sucursales). Índices que empiezan por `id_empresa`.
- **Auditoría estándar:** `activo`, `eliminado`/`fecha_eliminado`, `creado_por`/`fecha_creacion`,
  `modificado_por`/`fecha_modificacion`, `token_concurrencia`. En documentos: `estado`+`CHECK`, `motivo_anulacion`, `fecha_anulacion`.
- **Nunca borrado físico** contable → anulación lógica y estados.
- **Nombres:** tablas `snake_case` con prefijo **`ct_`**; objetos `PK_/FK_/CK_/DF_/UX_/IX_`.
- **Lógica en `Services/`** (no en SP/VIEW). Cambios de esquema como script `NNN_...sql` con
  PRECHECK/CAMBIO/POSTCHECK/ROLLBACK, registrado en el índice.
- **Catálogos centralizados:** reutilizar monedas/impuestos/formas de pago ya existentes; no duplicarlos.

### 3. Fuentes oficiales (Honduras)

Usar **únicamente** fuentes oficiales, en este orden de prioridad:

1. Diario Oficial **La Gaceta** — https://www.lagaceta.hn/
2. **SAR** (Servicio de Administración de Rentas) — https://www.sar.gob.hn/ · https://www.sar.gob.hn/leyes/
3. **SEFIN** (Secretaría de Finanzas) — https://www.sefin.gob.hn/ · https://www.sefin.gob.hn/normas-y-manuales-contables/
4. **TSC** (Tribunal Superior de Cuentas) — https://www.tsc.gob.hn/biblioteca/index.php/leyes
5. **Aduanas** — https://www.aduanas.gob.hn/
6. **Banco Central de Honduras** — https://www.bch.hn/

Fuente secundaria (no fundamento legal): COHPUC — https://www.cohpucphn.org/

**Reglas legales:** nunca inventar leyes, artículos, porcentajes, impuestos ni procedimientos. Ante contradicción, usar
la norma oficial **más reciente**. Siempre citar **Ley / Decreto / Artículo / Reglamento / Fuente / Fecha**. Si no hay
fundamento, responder textualmente:
> "No existe fundamento legal oficial para implementar esta funcionalidad."

### 4. Reglas contables

- **Partida doble:** en cada asiento, `Σ débitos = Σ créditos`. No se guarda un asiento descuadrado.
- **Estados del asiento:** `borrador → mayorizado → anulado` (ver máquina de estados en la Parte B).
- **Inmutabilidad:** no modificar asientos mayorizados; no eliminar asientos ni cierres. Corregir con
  **asiento de reversión/ajuste**.
- **Trazabilidad total:** todo asiento automático conserva el `id_evento_origen` que lo generó.

### 5. Alcance del módulo

Plan Único de Cuentas / catálogo de cuentas · tipos y naturaleza de cuenta · centros de costo · ejercicios fiscales y
períodos · asientos y partida doble · **Libro Diario** · **Libro Mayor** · **Balance de Comprobación** · **Balance
General** · **Estado de Resultados** · **Flujo de Efectivo** · cierres y reaperturas · (futuro) depreciaciones y
amortizaciones · conciliaciones.

### 6. Integración con lo existente (pieza clave)

El módulo de **Facturación ya publica eventos de dominio** al outbox `domain_events`. Contabilidad debe **reaccionar**
a esos eventos generando asientos automáticos, en vez de acoplarse directamente a las tablas de facturación:

| Evento publicado | Origen | Asiento típico (ejemplo, sujeto a fundamento legal y plan de cuentas) |
|---|---|---|
| `factura.emitida.contado` | `FacturacionService` | Caja/Banco → Ventas + ISV por pagar |
| `factura.emitida.credito` | `FacturacionService` | Cuentas por Cobrar → Ventas + ISV por pagar |
| `pago.recibido` / `pago.aplicado` | `PagoService` | Caja/Banco → Cuentas por Cobrar |
| `pago.anulado` | `PagoService` | Reversión del asiento del pago |
| `nota_credito.emitida` | `NotaService` | Devoluciones/Descuentos + ISV → CxC |
| `nota_debito.emitida` | `NotaService` | CxC → Ingresos/Recargos + ISV |

**Contrato real** (`Services/Eventos/IDomainEventHandler.cs`): implementar `IDomainEventHandler` con `Name`,
`CanHandle(eventType)` y `HandleAsync(DomainEventDispatch evt, CancellationToken ct)`. El dispatcher **puede reentregar**
el evento → `HandleAsync` **debe ser idempotente**: verificar si ya existe un asiento con
`id_evento_origen == evt.IdEvento` para esa empresa y, si existe, no hacer nada. Registrarlo con `AddScoped<IDomainEventHandler, ...>` en `Program.cs`.

### 7. Entregables esperados (por cada funcionalidad)

Adaptación de la lista de 29 puntos del prompt original a este stack. Al diseñar una funcionalidad, generar:

1. Resumen · 2. **Fundamento legal** (con cita) · 3. Reglas de negocio · 4. Flujo del proceso · 5. Casos especiales ·
6. Modelo de datos · 7. Diagrama entidad-relación · 8. **DDL SQL** (script `NNN_...sql` estilo casa) · 9. Tablas ·
10. FK · 11. Índices · 12. *SP (opcional — solo si aporta)* · 13. *Vistas SQL (opcional)* · 14. *Funciones SQL
(opcional)* · 15. *Triggers (opcional)* · 16. **Entidades EF + configuración** (sustituye a "DTO") · 17. **PageModel /
ViewModel** · 18. **Service** (interfaz+impl+DI; sustituye a "Repository") · 19. Servicios · 20. *(API REST opcional)* ·
21. **Razor Pages** (sustituye a "MVC") · 22. JavaScript · 23. Bootstrap · 24. Seguridad (`AuthHelper`/permisos) ·
25. Auditoría · 26. Casos de prueba · 27. Rendimiento · 28. Riesgos · 29. Recomendaciones.

> Los puntos 12–15 y 20 están **marcados como opcionales**: por defecto la lógica vive en `Services/` y las consultas en
> LINQ. Solo usar SP/VIEW/función/trigger/API si hay una razón concreta (rendimiento, reporte pesado, integración externa).

Además, todo cambio debe: **documentar sus vistas** (plantilla `_PlantillaVista.md`) y **registrar sus scripts** en
`INDICE_SCRIPTS_SQL.md`. Si la respuesta es extensa, dividirla en varias entregas manteniendo continuidad.

---

## PARTE B — Diseño concreto del núcleo contable

Modelo de datos propuesto, en el estilo idéntico a `2 - Script SQL/F1_Facturacion.sql`. Se entregaría como un script
`2 - Script SQL/NNN_ct_nucleo_contable.sql` (número real a asignar leyendo el índice).

### B.1 Diagrama entidad-relación (resumen)

```
empresas ─┐
          ├─< ct_cuentas (jerárquica: id_cuenta_padre → ct_cuentas)
          ├─< ct_ejercicios ─< ct_periodos
          ├─< ct_centros_costo
          └─< ct_asientos ─< ct_asiento_movimientos >─ ct_cuentas
                   │                     └─ ct_centros_costo (opcional)
                   └─ id_evento_origen → domain_events.id_evento (idempotencia)
```

### B.2 Tablas

```sql
/* ── ct_cuentas — Plan de cuentas jerárquico ──────────────────────────────── */
IF OBJECT_ID('dbo.ct_cuentas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ct_cuentas (
        id_cuenta           INT IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        codigo              NVARCHAR(30)   NOT NULL,
        nombre              NVARCHAR(200)  NOT NULL,
        id_cuenta_padre     INT            NULL,
        nivel               INT            NOT NULL CONSTRAINT DF_ctcta_nivel DEFAULT (1),
        naturaleza          NVARCHAR(10)   NOT NULL,   -- deudora / acreedora
        tipo                NVARCHAR(20)   NOT NULL,   -- activo/pasivo/patrimonio/ingreso/gasto/orden
        es_movimiento       BIT            NOT NULL CONSTRAINT DF_ctcta_mov DEFAULT (1),  -- 1 = acepta asientos
        moneda              NVARCHAR(3)    NOT NULL CONSTRAINT DF_ctcta_mon DEFAULT ('HNL'),
        activo              BIT            NOT NULL CONSTRAINT DF_ctcta_act  DEFAULT (1),
        eliminado           BIT            NOT NULL CONSTRAINT DF_ctcta_elim DEFAULT (0),
        fecha_eliminado     DATETIME2      NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctcta_fc DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2      NULL,
        CONSTRAINT PK_ct_cuentas         PRIMARY KEY CLUSTERED (id_cuenta),
        CONSTRAINT FK_ct_cuentas_empresa FOREIGN KEY (id_empresa)      REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_ct_cuentas_padre   FOREIGN KEY (id_cuenta_padre) REFERENCES dbo.ct_cuentas(id_cuenta),
        CONSTRAINT CK_ct_cuentas_nat     CHECK (naturaleza IN ('deudora','acreedora')),
        CONSTRAINT CK_ct_cuentas_tipo    CHECK (tipo IN ('activo','pasivo','patrimonio','ingreso','gasto','orden'))
    );
    CREATE UNIQUE INDEX UX_ct_cuentas_empresa_codigo ON dbo.ct_cuentas(id_empresa, codigo);
    CREATE INDEX        IX_ct_cuentas_empresa_padre  ON dbo.ct_cuentas(id_empresa, id_cuenta_padre);
END
GO

/* ── ct_ejercicios — Ejercicios fiscales ──────────────────────────────────── */
IF OBJECT_ID('dbo.ct_ejercicios', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ct_ejercicios (
        id_ejercicio        INT IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        anio                INT            NOT NULL,
        fecha_inicio        DATE           NOT NULL,
        fecha_fin           DATE           NOT NULL,
        estado              NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctejer_est DEFAULT ('abierto'),
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctejer_fc DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2      NULL,
        CONSTRAINT PK_ct_ejercicios         PRIMARY KEY CLUSTERED (id_ejercicio),
        CONSTRAINT FK_ct_ejercicios_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT CK_ct_ejercicios_estado  CHECK (estado IN ('abierto','cerrado'))
    );
    CREATE UNIQUE INDEX UX_ct_ejercicios_empresa_anio ON dbo.ct_ejercicios(id_empresa, anio);
END
GO

/* ── ct_periodos — Períodos contables (por ejercicio) ─────────────────────── */
IF OBJECT_ID('dbo.ct_periodos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ct_periodos (
        id_periodo          INT IDENTITY(1,1) NOT NULL,
        id_ejercicio        INT            NOT NULL,
        id_empresa          INT            NOT NULL,
        numero              INT            NOT NULL,   -- 1..12 (13 = ajustes/cierre)
        fecha_inicio        DATE           NOT NULL,
        fecha_fin           DATE           NOT NULL,
        estado              NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctper_est DEFAULT ('abierto'),
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctper_fc DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2      NULL,
        CONSTRAINT PK_ct_periodos           PRIMARY KEY CLUSTERED (id_periodo),
        CONSTRAINT FK_ct_periodos_ejercicio FOREIGN KEY (id_ejercicio) REFERENCES dbo.ct_ejercicios(id_ejercicio),
        CONSTRAINT FK_ct_periodos_empresa   FOREIGN KEY (id_empresa)   REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT CK_ct_periodos_estado    CHECK (estado IN ('abierto','cerrado')),
        CONSTRAINT CK_ct_periodos_numero    CHECK (numero BETWEEN 1 AND 13)
    );
    CREATE UNIQUE INDEX UX_ct_periodos_ejercicio_numero ON dbo.ct_periodos(id_ejercicio, numero);
END
GO

/* ── ct_centros_costo — Centros de costo (opcional por línea) ─────────────── */
IF OBJECT_ID('dbo.ct_centros_costo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ct_centros_costo (
        id_centro_costo     INT IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        codigo              NVARCHAR(30)   NOT NULL,
        nombre              NVARCHAR(200)  NOT NULL,
        activo              BIT            NOT NULL CONSTRAINT DF_ctcc_act  DEFAULT (1),
        eliminado           BIT            NOT NULL CONSTRAINT DF_ctcc_elim DEFAULT (0),
        fecha_eliminado     DATETIME2      NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctcc_fc DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2      NULL,
        CONSTRAINT PK_ct_centros_costo         PRIMARY KEY CLUSTERED (id_centro_costo),
        CONSTRAINT FK_ct_centros_costo_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa)
    );
    CREATE UNIQUE INDEX UX_ct_centros_costo_empresa_codigo ON dbo.ct_centros_costo(id_empresa, codigo);
END
GO

/* ── ct_asientos — Cabecera del asiento ───────────────────────────────────── */
IF OBJECT_ID('dbo.ct_asientos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ct_asientos (
        id_asiento          INT IDENTITY(1,1) NOT NULL,
        id_empresa          INT            NOT NULL,
        id_periodo          INT            NOT NULL,
        numero              INT            NULL,        -- correlativo asignado al mayorizar
        fecha               DATE           NOT NULL,
        tipo_asiento        NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctas_tipo DEFAULT ('diario'),
        concepto            NVARCHAR(500)  NOT NULL,
        origen              NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctas_orig DEFAULT ('manual'),
        id_evento_origen    BIGINT         NULL,        -- domain_events.id_evento (idempotencia outbox)
        total_debito        DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ctas_td DEFAULT (0),
        total_credito       DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ctas_tc DEFAULT (0),
        estado              NVARCHAR(20)   NOT NULL CONSTRAINT DF_ctas_est DEFAULT ('borrador'),
        motivo_anulacion    NVARCHAR(500)  NULL,
        fecha_anulacion     DATETIME2      NULL,
        eliminado           BIT            NOT NULL CONSTRAINT DF_ctas_elim DEFAULT (0),
        fecha_eliminado     DATETIME2      NULL,
        creado_por          NVARCHAR(100)  NOT NULL,
        fecha_creacion      DATETIME2      NOT NULL CONSTRAINT DF_ctas_fc DEFAULT (SYSUTCDATETIME()),
        modificado_por      NVARCHAR(100)  NULL,
        fecha_modificacion  DATETIME2      NULL,
        token_concurrencia  ROWVERSION     NOT NULL,
        CONSTRAINT PK_ct_asientos         PRIMARY KEY CLUSTERED (id_asiento),
        CONSTRAINT FK_ct_asientos_empresa FOREIGN KEY (id_empresa) REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_ct_asientos_periodo FOREIGN KEY (id_periodo) REFERENCES dbo.ct_periodos(id_periodo),
        CONSTRAINT CK_ct_asientos_tipo    CHECK (tipo_asiento IN ('apertura','diario','ajuste','cierre')),
        CONSTRAINT CK_ct_asientos_origen  CHECK (origen IN ('manual','automatico')),
        CONSTRAINT CK_ct_asientos_estado  CHECK (estado IN ('borrador','mayorizado','anulado')),
        CONSTRAINT CK_ct_asientos_cuadre  CHECK (total_debito = total_credito)   -- partida doble
    );
    -- Idempotencia: un evento del outbox genera a lo sumo un asiento por empresa
    CREATE UNIQUE INDEX UX_ct_asientos_empresa_evento
        ON dbo.ct_asientos(id_empresa, id_evento_origen) WHERE id_evento_origen IS NOT NULL;
    -- Correlativo único de asientos ya mayorizados
    CREATE UNIQUE INDEX UX_ct_asientos_empresa_numero
        ON dbo.ct_asientos(id_empresa, numero) WHERE numero IS NOT NULL;
    CREATE INDEX IX_ct_asientos_empresa_fecha           ON dbo.ct_asientos(id_empresa, fecha);
    CREATE INDEX IX_ct_asientos_empresa_periodo_estado  ON dbo.ct_asientos(id_empresa, id_periodo, estado);
END
GO

/* ── ct_asiento_movimientos — Detalle (partida doble) ─────────────────────── */
IF OBJECT_ID('dbo.ct_asiento_movimientos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ct_asiento_movimientos (
        id_movimiento       INT IDENTITY(1,1) NOT NULL,
        id_asiento          INT            NOT NULL,
        id_empresa          INT            NOT NULL,
        numero_linea        INT            NOT NULL,
        id_cuenta           INT            NOT NULL,
        id_centro_costo     INT            NULL,
        descripcion         NVARCHAR(300)  NULL,
        debito              DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ctmov_deb DEFAULT (0),
        credito             DECIMAL(18,2)  NOT NULL CONSTRAINT DF_ctmov_cre DEFAULT (0),
        CONSTRAINT PK_ct_asiento_movimientos    PRIMARY KEY CLUSTERED (id_movimiento),
        CONSTRAINT FK_ct_mov_asiento            FOREIGN KEY (id_asiento)      REFERENCES dbo.ct_asientos(id_asiento),
        CONSTRAINT FK_ct_mov_empresa            FOREIGN KEY (id_empresa)      REFERENCES dbo.empresas(id_empresa),
        CONSTRAINT FK_ct_mov_cuenta             FOREIGN KEY (id_cuenta)       REFERENCES dbo.ct_cuentas(id_cuenta),
        CONSTRAINT FK_ct_mov_centro             FOREIGN KEY (id_centro_costo) REFERENCES dbo.ct_centros_costo(id_centro_costo),
        CONSTRAINT CK_ct_mov_no_negativos       CHECK (debito >= 0 AND credito >= 0),
        -- exactamente uno de los dos debe ser > 0
        CONSTRAINT CK_ct_mov_debito_xor_credito CHECK ((debito > 0 AND credito = 0) OR (credito > 0 AND debito = 0))
    );
    CREATE UNIQUE INDEX UX_ct_mov_asiento_linea   ON dbo.ct_asiento_movimientos(id_asiento, numero_linea);
    CREATE INDEX        IX_ct_mov_asiento         ON dbo.ct_asiento_movimientos(id_asiento);
    CREATE INDEX        IX_ct_mov_empresa_cuenta  ON dbo.ct_asiento_movimientos(id_empresa, id_cuenta);
END
GO
```

### B.3 Máquina de estados del asiento

```
                (crear)                (mayorizar: valida cuadre y período abierto)
   [ · ] ───────────────► borrador ──────────────────────────────────► mayorizado
                              │                                              │
                              │ (editar / borrar borrador)                   │ (anular → genera reversión)
                              ▼                                              ▼
                         eliminado=1                                      anulado
```

- **borrador:** editable; puede eliminarse (soft-delete). No impacta libros.
- **mayorizado:** inmutable; recibe `numero` correlativo; impacta Diario/Mayor/Balances. Solo si el período está `abierto`.
- **anulado:** no se borra; se registra `motivo_anulacion`/`fecha_anulacion` y se genera un **asiento de reversión**.

### B.4 Flujo de asiento automático (outbox → handler idempotente)

```
FacturacionService.EmitirAsync()  ──(misma transacción)──►  INSERT domain_events (event_type='factura.emitida.credito')
                                                                     │
OutboxDispatcherBackgroundService reclama el lote  ◄──────────────────┘
                                                   │
                                                   ▼
ContabilidadEventHandler.HandleAsync(evt):
   1. ¿existe ct_asientos con id_empresa=evt.IdEmpresa y id_evento_origen=evt.IdEvento?  → sí: return (idempotente)
   2. leer payload (evt.PayloadJson), resolver cuentas por reglas parametrizadas
   3. construir asiento (cabecera + movimientos), validar Σdébito = Σcrédito
   4. INSERT ct_asientos (origen='automatico', id_evento_origen=evt.IdEvento) + movimientos, en transacción
   5. si algo falla → lanzar excepción (el dispatcher reintenta con backoff)
```

### B.5 Esbozo de código C#

```csharp
// Models/Contabilidad/Asiento.cs
[Table("ct_asientos")]
public class Asiento
{
    [Column("id_asiento")]        public int      IdAsiento { get; set; }
    [Column("id_empresa")]        public int      IdEmpresa { get; set; }
    [Column("id_periodo")]        public int      IdPeriodo { get; set; }
    [Column("numero")]            public int?     Numero { get; set; }
    [Column("fecha")]             public DateTime Fecha { get; set; }
    [Column("tipo_asiento")]      public string   TipoAsiento { get; set; } = "diario";
    [Column("concepto")]          public string   Concepto { get; set; } = "";
    [Column("origen")]            public string   Origen { get; set; } = "manual";
    [Column("id_evento_origen")]  public long?    IdEventoOrigen { get; set; }
    [Column("total_debito")]      public decimal  TotalDebito { get; set; }
    [Column("total_credito")]     public decimal  TotalCredito { get; set; }
    [Column("estado")]            public string   Estado { get; set; } = "borrador";
    [Timestamp, Column("token_concurrencia")] public byte[]? TokenConcurrencia { get; set; }
    public List<AsientoMovimiento> Movimientos { get; set; } = new();
    // + creado_por / fecha_creacion / modificado_por / fecha_modificacion / eliminado / ...
}

// Data/ApplicationDbContext.cs — OnModelCreating (índice único filtrado para idempotencia)
modelBuilder.Entity<Asiento>()
    .HasIndex(a => new { a.IdEmpresa, a.IdEventoOrigen })
    .HasFilter("[id_evento_origen] IS NOT NULL")
    .IsUnique();

// Services/Contabilidad/ContabilidadEventHandler.cs
public sealed class ContabilidadEventHandler : IDomainEventHandler
{
    public string Name => "contabilidad";
    public bool CanHandle(string eventType) =>
        eventType.StartsWith("factura.emitida") || eventType.StartsWith("pago.")
        || eventType.StartsWith("nota_credito") || eventType.StartsWith("nota_debito");

    public async Task HandleAsync(DomainEventDispatch evt, CancellationToken ct)
    {
        // 1. idempotencia
        bool yaProcesado = await _db.Asientos.AnyAsync(
            a => a.IdEmpresa == evt.IdEmpresa && a.IdEventoOrigen == evt.IdEvento, ct);
        if (yaProcesado) return;
        // 2. mapear evt.PayloadJson → asiento según reglas contables parametrizadas
        // 3. validar cuadre y persistir en transacción
    }
}
// Program.cs → builder.Services.AddScoped<IDomainEventHandler, ContabilidadEventHandler>();
```

### B.6 Encabezado del script de entrega (referencia)

```sql
-- ============================================================
-- Script   : NNN_ct_nucleo_contable.sql
-- Proposito: Nucleo del modulo contable (cuentas, ejercicios, periodos, centros, asientos, movimientos)
-- Autor    : eGestion360-Web
-- Fecha    : YYYY-MM-DD
-- BD       : eBD_SPD
-- Requiere : F0_Catalogos_Transversales.sql, F0_Outbox.sql, F1_Facturacion.sql
-- Rollback : DROP de las 6 tablas ct_* en orden inverso de dependencias (ver ROLLBACK)
-- ============================================================
-- PRECHECK : verificar que existen dbo.empresas y dbo.domain_events; que no existan ya las tablas ct_*
-- CAMBIO   : los CREATE TABLE de la sección B.2 (idempotentes)
-- POSTCHECK: SELECT sobre sys.tables WHERE name LIKE 'ct_%'  → deben aparecer 6 tablas
-- ROLLBACK : DROP TABLE ct_asiento_movimientos, ct_asientos, ct_centros_costo, ct_periodos, ct_ejercicios, ct_cuentas;
```

---

## PARTE C — Pasos de implementación (fuera del alcance de esta documentación)

1. Crear `2 - Script SQL/NNN_ct_nucleo_contable.sql` con la DDL de B.2 + PRECHECK/CAMBIO/POSTCHECK/ROLLBACK y registrarlo
   en [INDICE_SCRIPTS_SQL.md](INDICE_SCRIPTS_SQL.md) (estado `Pendiente`). Confirmar antes de ejecutar en producción.
2. Crear entidades EF en `Models/Contabilidad/` + configuración en `Data/ApplicationDbContext.cs` (o migración EF).
3. Crear `Services/Contabilidad/` (plan de cuentas, asientos, mayorización, reportes) con interfaz + DI.
4. Implementar `ContabilidadEventHandler : IDomainEventHandler` y registrarlo en `Program.cs`.
5. Crear páginas en `Pages/Contabilidad/` y enlazar la tarjeta del menú (`Pages/MainMenu.cshtml`, hoy "Próximamente").
6. Documentar cada vista nueva con `_PlantillaVista.md`.
7. **Validar cada regla contable/tributaria contra fuente oficial** (§3) antes de fijar cuentas, porcentajes de ISV/retención y tratamiento.

---

## Referencias

- Convenciones globales: [ESTANDARES_ERP.md](ESTANDARES_ERP.md)
- Estilo DDL/eventos: `2 - Script SQL/F1_Facturacion.sql`, `F1_Pagos_y_Notas.sql`, `F0_Outbox.sql`
- Contrato de eventos: `Services/Eventos/IDomainEventHandler.cs`, `Services/Eventos/DomainEvent.cs`
- Control de cambios: `.claude/commands/skill-db.md`, [INDICE_SCRIPTS_SQL.md](INDICE_SCRIPTS_SQL.md)
