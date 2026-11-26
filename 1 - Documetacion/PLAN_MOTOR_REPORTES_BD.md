# Plan — Motor de Reportes Versionados en Base de Datos por Tenant (`eGestión360`)

> **Tipo:** Plan de arquitectura e implementación (complementario al roadmap principal F0–F9 en [Arquitectura/README.md](Arquitectura/README.md)).  
> **Estado:** Fase 1 (infraestructura y piloto de la constancia de licencia) en producción desde el 2026-11-25: script `047_motor_reportes_versionados.sql` aplicado y código publicado desde `dev` `f222fc1`. Las fases siguientes están sin empezar. Detalle en [ADR-031](Arquitectura/ADR-031-reportes-versionados-en-bd.md).  
> **Fecha:** 2026-11-21  
> **ADR propuesto:** `ADR-031-reportes-versionados-en-bd.md`

---

## 1. Objetivo

Desacoplar todos los reportes actuales y futuros de **eGestión360** del código fuente, almacenando su definición, diseño visual, parámetros, medidas y textos a nivel de base de datos, con:

1. **Versionamiento completo por tenant** (herencia Global → Tenant).
2. **Histórico inmutable** de todas las versiones publicadas (permitiendo reconstruir cualquier documento emitido en el pasado y hacer *rollback*).
3. **Bitácora inmutable de cambios** (quién, cuándo, qué cambió, motivo y correlación).
4. **Historial de ejecuciones/impresiones** atado a la versión exacta utilizada.

---

## 2. Diagnóstico del estado actual

| Tipo de salida | Ubicación actual en código | Situación actual |
|---|---|---|
| **Constancia psicológica imprimible** | `Services/Psicologia/PlantillasConstancia.cs` y `Pages/Psicologia/Constancias/Imprimir.cshtml` | Medidas en mm, CSS, redacción y versión (`"1"`) hardcodeados en C#/Razor. Ya existe `psi.impresiones` (`Models/Psicologia/Impresion.cs`) registrando `plantilla` y `version_plantilla`. |
| **KPI de Flota (tabular/analítico)** | `Pages/Flota/KPI/Dashboard.cshtml.cs` y `Services/KpiService.cs` | Cálculos agregados en C#. El proyecto ya cuenta con `ClosedXML` (`eGestion360Web.csproj`) para salida Excel. |
| **Reportes futuros del ERP** | `Pages/Facturacion/`, `PROMPT_MAESTRO_CONTABILIDAD.md`, `Psicologia` | Facturas con CAI, notas de crédito/débito, recibos, estados de cuenta CxC, constancias adicionales y reportes contables. |

---

## 3. Decisiones arquitectónicas propuestas (ADR-031)

| Tema | Propuesta |
|---|---|
| **Separación Datos vs. Presentación** | **En BD:** plantillas HTML/CSS (Scriban), configuración en JSON (medidas en mm, papel, columnas/formatos de Excel, textos legales) y metadatos.<br>**En código / vistas SQL controladas:** fuentes de datos (`IReporteDataSource`, ej. `psi.constancia`, `flota.kpi_vehiculos`, `fac.factura`). **No** se guarda ni ejecuta SQL arbitrario editable en BD. |
| **Motor de plantillas HTML/Impresión** | **Scriban** (paquete NuGet liviano, rápido y *sandboxed* — sin ejecución arbitraria de C#, a diferencia de compilar Razor en runtime). |
| **Motor de reportes tabulares (Excel)** | **ClosedXML** (ya instalado) alimentado por un esquema declarativo en `configuracion_json` (hojas, columnas, formatos, totales). |
| **Herencia Global → Tenant** | Tablas mixtas (`ITenantOpcionalEntity`, ADR-015 / ADR-023): `id_tenant IS NULL` = plantilla base global de plataforma; `id_tenant = X` = personalización o reporte propio del tenant. Al renderizar se resuelve `(id_tenant actual, codigo)` y, si no existe, hace fallback a `(NULL, codigo)`. |
| **Inmutabilidad y trazabilidad** | Triggers `INSTEAD OF UPDATE, DELETE` en `dbo.rep_reporte_bitacora` y protección de escritura/borrado sobre filas `publicada`/`archivada` en `dbo.rep_reporte_versiones` (mismo patrón que `dbo.bitacora_cambios`, script 014/045). |
| **Esquema y prefijo** | Esquema `dbo` con prefijo `rep_` (ADR-013). |
| **Control de cambios en BD** | Antes de ejecutar cualquier script en `eBD_SPD`, disparar `/alerta-bd` y esperar aprobación explícita. |

### Decisiones pendientes de confirmar antes de codificar
- [ ] **Permisos de edición:** ¿El administrador de cada tenant puede clonar/editar sus propias plantillas (`reportes.plantillas.*`), o solo el operador de plataforma edita versiones por tenant?
- [ ] **Generación nativa de PDF:** ¿Basta con impresión HTML/CSS (`window.print()`) + Excel (`ClosedXML`) en Fase 1, o se requiere PDF nativo (ej. QuestPDF) desde el inicio?

---

## 4. Modelo de datos propuesto (siguiente script correlativo `NNN_motor_reportes_versionados.sql`)

### Resumen de tablas y políticas RLS (`seg.pol_tenant`)

| Tabla | Interfaz EF Core | RLS (`seg.pol_tenant`) | Propósito |
|---|---|---|---|
| `dbo.rep_reportes` | `ITenantOpcionalEntity` | `fn_mixta_lectura` / `fn_mixta_escritura` | Catálogo maestro del reporte (global o por tenant) y puntero `id_version_activa`. |
| `dbo.rep_reporte_versiones` | `ITenantOpcionalEntity` | `fn_mixta_lectura` / `fn_mixta_escritura` | Snapshots versionados (`numero_version`, `contenido_plantilla`, `configuracion_json`, `hash_sha256`). Inmutable una vez publicada. |
| `dbo.rep_reporte_bitacora` | `ITenantOpcionalEntity` | `fn_mixta_lectura` / `fn_mixta_escritura` | Bitácora inmutable de toda acción sobre definiciones y versiones. |
| `dbo.rep_ejecuciones` | `ITenantEntity` | `fn_tenant_actual` | Registro inmutable de cada generación/impresión, vinculado a `id_version` exacta. |

### Borrador de estructura SQL

```sql
-- 1. Cabecera del reporte (global o personalizado por tenant)
CREATE TABLE dbo.rep_reportes (
    id_reporte          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_rep_reportes PRIMARY KEY,
    id_tenant           INT NULL CONSTRAINT FK_rep_reportes_tenant REFERENCES saas.tenants(id_tenant),
    codigo              VARCHAR(60) NOT NULL,          -- ej. 'psi_constancia_licencia'
    nombre              NVARCHAR(150) NOT NULL,
    descripcion         NVARCHAR(500) NULL,
    modulo              VARCHAR(40) NOT NULL,          -- 'psicologia', 'flota', 'facturacion', 'contabilidad'
    fuente_datos        VARCHAR(80) NOT NULL,          -- ej. 'psi.constancia'
    tipo_salida         VARCHAR(20) NOT NULL,          -- 'impresion_html', 'excel', 'pdf'
    id_version_activa   INT NULL,                      -- FK diferida a rep_reporte_versiones
    activo              BIT NOT NULL CONSTRAINT DF_rep_reportes_activo DEFAULT (1),
    creado_por          NVARCHAR(100) NOT NULL,
    fecha_creacion      DATETIME2(3) NOT NULL CONSTRAINT DF_rep_reportes_fc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_rep_reportes_tipo_salida CHECK (tipo_salida IN ('impresion_html', 'excel', 'pdf'))
);

CREATE UNIQUE INDEX UX_rep_reportes_global ON dbo.rep_reportes(codigo) WHERE id_tenant IS NULL;
CREATE UNIQUE INDEX UX_rep_reportes_tenant ON dbo.rep_reportes(id_tenant, codigo) WHERE id_tenant IS NOT NULL;

-- 2. Versiones inmutables (historico completo)
CREATE TABLE dbo.rep_reporte_versiones (
    id_version          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_rep_reporte_versiones PRIMARY KEY,
    id_reporte          INT NOT NULL CONSTRAINT FK_rep_versiones_reporte REFERENCES dbo.rep_reportes(id_reporte),
    id_tenant           INT NULL CONSTRAINT FK_rep_versiones_tenant REFERENCES saas.tenants(id_tenant),
    numero_version      INT NOT NULL,
    estado              VARCHAR(15) NOT NULL,          -- 'borrador', 'publicada', 'archivada'
    contenido_plantilla NVARCHAR(MAX) NOT NULL,        -- HTML/CSS Scriban o plantilla estructurada
    configuracion_json  NVARCHAR(MAX) NULL,            -- Medidas mm, papel, columnas Excel, etc.
    hash_sha256         CHAR(64) NOT NULL,
    motivo_cambio       NVARCHAR(500) NULL,
    id_version_origen   INT NULL CONSTRAINT FK_rep_versiones_origen REFERENCES dbo.rep_reporte_versiones(id_version),
    id_cuenta           INT NULL,
    creado_por          NVARCHAR(100) NOT NULL,
    fecha_creacion      DATETIME2(3) NOT NULL CONSTRAINT DF_rep_versiones_fc DEFAULT (SYSUTCDATETIME()),
    fecha_publicacion   DATETIME2(3) NULL,
    CONSTRAINT UX_rep_versiones_num UNIQUE (id_reporte, numero_version),
    CONSTRAINT CK_rep_versiones_estado CHECK (estado IN ('borrador', 'publicada', 'archivada')),
    CONSTRAINT CK_rep_versiones_config_json CHECK (configuracion_json IS NULL OR ISJSON(configuracion_json) = 1)
);

-- 3. Bitacora inmutable de cambios de reportes
CREATE TABLE dbo.rep_reporte_bitacora (
    id_bitacora         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_rep_reporte_bitacora PRIMARY KEY,
    id_tenant           INT NULL,
    id_reporte          INT NOT NULL,
    codigo_reporte      VARCHAR(60) NOT NULL,
    accion              VARCHAR(30) NOT NULL,
    id_version_anterior INT NULL,
    id_version_nueva    INT NULL,
    observaciones       NVARCHAR(1000) NULL,
    detalle_json        NVARCHAR(MAX) NULL,
    id_cuenta           INT NULL,
    usuario             NVARCHAR(100) NOT NULL,
    id_correlacion      VARCHAR(64) NULL,
    fecha_hora          DATETIME2(3) NOT NULL CONSTRAINT DF_rep_bitacora_fh DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_rep_bitacora_accion CHECK (accion IN ('CREAR_BORRADOR','EDITAR_BORRADOR','PUBLICAR','CLONAR_GLOBAL','REVERTIR','ACTIVAR','DESACTIVAR')),
    CONSTRAINT CK_rep_bitacora_detalle_json CHECK (detalle_json IS NULL OR ISJSON(detalle_json) = 1)
);

-- 4. Historial de ejecuciones / impresiones por tenant
CREATE TABLE dbo.rep_ejecuciones (
    id_ejecucion        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_rep_ejecuciones PRIMARY KEY,
    id_tenant           INT NOT NULL CONSTRAINT FK_rep_ejecuciones_tenant REFERENCES saas.tenants(id_tenant),
    id_empresa          INT NOT NULL,
    id_reporte          INT NOT NULL,
    id_version          INT NOT NULL,
    codigo_reporte      VARCHAR(60) NOT NULL,
    numero_version      INT NOT NULL,
    entidad_origen      VARCHAR(60) NULL,              -- ej. 'psi.constancias', 'fac.facturas'
    id_registro_origen  BIGINT NULL,
    parametros_json     NVARCHAR(MAX) NULL,
    tipo_salida         VARCHAR(20) NOT NULL,
    id_cuenta           INT NULL,
    usuario             NVARCHAR(100) NOT NULL,
    fecha_hora          DATETIME2(3) NOT NULL CONSTRAINT DF_rep_ejecuciones_fh DEFAULT (SYSUTCDATETIME())
);
```

> **Compatibilidad con Psicología:** `psi.impresiones` se mantiene intacta; opcionalmente se puede añadir `id_version_reporte INT NULL` o registrar en paralelo en `dbo.rep_ejecuciones`.

---

## 5. Plantilla Inicial — Piloto: Constancia de Licencia de Conducir (`psi_constancia_licencia` v1)

Migra lo que hoy vive en `Services/Psicologia/PlantillasConstancia.cs` y `Pages/Psicologia/Constancias/Imprimir.cshtml` hacia `dbo.rep_reporte_versiones`.

### 5.1. `configuracion_json` (medidas calibrables en milímetros y textos)

```json
{
  "tamano_papel": "letter",
  "pide_resultado": true,
  "hoja": {
    "ancho_mm": 215,
    "alto_mm": 275,
    "fuente": "\"Arial\", \"Helvetica\", sans-serif"
  },
  "fecha": {
    "base_mm": 35,
    "dia_desde_mm": 14,
    "dia_hasta_mm": 25,
    "mes_desde_mm": 33,
    "mes_hasta_mm": 68,
    "anio_desde_mm": 80,
    "anio_hasta_mm": 92,
    "tamano_pt": 13
  },
  "cuerpo": {
    "arriba_mm": 74,
    "izquierda_mm": 26,
    "ancho_mm": 160,
    "renglon_mm": 14,
    "tamano_pt": 15,
    "tamano_minimo_junto_pt": 10
  },
  "pruebas": {
    "arriba_mm": 204,
    "izquierda_mm": 13,
    "ancho_mm": 190,
    "sangria_mm": 98,
    "renglon_mm": 9,
    "tamano_pt": 12
  },
  "textos": {
    "conclusion_apto": "posee personalidad estable, APTO para conducir.",
    "conclusion_no_apto": "no reúne las condiciones psicológicas requeridas, NO APTO para conducir."
  }
}
```

### 5.2. `contenido_plantilla` (HTML + CSS + Scriban)

```html
<style>
  :root {
    --hoja-ancho: {{ cfg.hoja.ancho_mm }}mm;
    --hoja-alto: {{ cfg.hoja.alto_mm }}mm;
    --letra: {{ cfg.hoja.fuente }};

    --fecha-base: {{ cfg.fecha.base_mm }}mm;
    --dia-desde: {{ cfg.fecha.dia_desde_mm }}mm;   --dia-hasta: {{ cfg.fecha.dia_hasta_mm }}mm;
    --mes-desde: {{ cfg.fecha.mes_desde_mm }}mm;   --mes-hasta: {{ cfg.fecha.mes_hasta_mm }}mm;
    --anio-desde: {{ cfg.fecha.anio_desde_mm }}mm; --anio-hasta: {{ cfg.fecha.anio_hasta_mm }}mm;

    --cuerpo-arriba: {{ cfg.cuerpo.arriba_mm }}mm;
    --cuerpo-izquierda: {{ cfg.cuerpo.izquierda_mm }}mm;
    --cuerpo-ancho: {{ cfg.cuerpo.ancho_mm }}mm;
    --cuerpo-renglon: {{ cfg.cuerpo.renglon_mm }}mm;

    --pruebas-arriba: {{ cfg.pruebas.arriba_mm }}mm;
    --pruebas-izquierda: {{ cfg.pruebas.izquierda_mm }}mm;
    --pruebas-ancho: {{ cfg.pruebas.ancho_mm }}mm;
    --pruebas-sangria: {{ cfg.pruebas.sangria_mm }}mm;
    --pruebas-renglon: {{ cfg.pruebas.renglon_mm }}mm;
  }

  @page { size: {{ cfg.tamano_papel }}; margin: 0; }
  .hoja { position: relative; width: var(--hoja-ancho); height: var(--hoja-alto); margin: 16px auto; background: #fff; box-shadow: 0 0 0 1px #bbb; overflow: hidden; font-family: var(--letra); color: #000; }
  .fecha { position: absolute; top: calc(var(--fecha-base) - 0.81em); font-size: {{ cfg.fecha.tamano_pt }}pt; line-height: 1; white-space: nowrap; text-align: center; }
  .cuerpo { position: absolute; top: var(--cuerpo-arriba); left: var(--cuerpo-izquierda); width: var(--cuerpo-ancho); font-size: {{ cfg.cuerpo.tamano_pt }}pt; line-height: var(--cuerpo-renglon); }
  .cuerpo .junto { white-space: nowrap; }
  .pruebas { position: absolute; top: var(--pruebas-arriba); left: var(--pruebas-izquierda); width: var(--pruebas-ancho); font-size: {{ cfg.pruebas.tamano_pt }}pt; line-height: var(--pruebas-renglon); text-indent: var(--pruebas-sangria); }
  .guia { position: absolute; color: #9aa0a6; font-size: 10pt; font-weight: bold; white-space: nowrap; }
  .renglon { position: absolute; left: 12mm; right: 12mm; border-bottom: 0.3mm dotted #c0c4c8; }
  .blanco { position: absolute; top: var(--fecha-base); border-top: 0.3mm solid #9aa0a6; }

  @media print {
    html, body { background: #fff; }
    .barra { display: none; }
    .hoja { margin: 0; box-shadow: none; }
  }
</style>

<div class="hoja">
  {{ if modo_guia }}
    <div class="guia" style="top:12mm; left:40mm; font-size:14pt;">COLEGIO DE PSICÓLOGOS DE HONDURAS</div>
    <div class="blanco" style="left:var(--dia-desde); width:calc(var(--dia-hasta) - var(--dia-desde));"></div>
    <div class="guia fecha" style="left:var(--dia-hasta); width:calc(var(--mes-desde) - var(--dia-hasta));">de</div>
    <div class="blanco" style="left:var(--mes-desde); width:calc(var(--mes-hasta) - var(--mes-desde));"></div>
    <div class="guia fecha" style="left:var(--mes-hasta); width:calc(var(--anio-desde) - var(--mes-hasta));">del 20</div>
    <div class="blanco" style="left:var(--anio-desde); width:calc(var(--anio-hasta) - var(--anio-desde));"></div>
    <div class="guia" style="top:46mm; left:12mm;">CONSTANCIA DE EXAMEN PSICOLÓGICO PARA SOLICITUD DE LICENCIA DE CONDUCIR</div>
    {{ for i in 1..4 }}
      <div class="renglon" style="top:calc(var(--cuerpo-arriba) + {{ i }} * var(--cuerpo-renglon) - 3mm);"></div>
    {{ end }}
    <div class="guia" style="top:203mm; left:85mm;">VALOR L. 200.00</div>
    <div class="guia" style="top:var(--pruebas-arriba); left:var(--pruebas-izquierda);">PRUEBAS PSICOLÓGICAS APLICADAS:</div>
    <div class="renglon" style="top:calc(var(--pruebas-arriba) + var(--pruebas-renglon) - 2mm);"></div>
    <div class="renglon" style="top:calc(var(--pruebas-arriba) + 2 * var(--pruebas-renglon) - 2mm);"></div>
  {{ end }}

  <div class="fecha" style="left:var(--dia-desde); width:calc(var(--dia-hasta) - var(--dia-desde));">{{ fecha.dia }}</div>
  <div class="fecha" style="left:var(--mes-desde); width:calc(var(--mes-hasta) - var(--mes-desde));">{{ fecha.mes }}</div>
  <div class="fecha" style="left:var(--anio-desde); width:calc(var(--anio-hasta) - var(--anio-desde));">{{ fecha.anio_2d }}</div>

  <div class="cuerpo">
    Por este medio se hace constar que {{ paciente.tratamiento }}
    <span class="junto">{{ paciente.nombre_completo }}</span>, con
    <span class="junto">DNI {{ paciente.dni_formateado }}</span>,
    {{ if constancia.resultado == "no_apto" }}{{ cfg.textos.conclusion_no_apto }}{{ else }}{{ cfg.textos.conclusion_apto }}{{ end }}
  </div>

  <div class="pruebas">{{ pruebas_texto }}</div>
</div>
```

---

## 6. Etapas de implementación

### Fase 1 — Infraestructura base + Piloto Constancias (Mínimo Viable)
- [ ] Redactar `1 - Documetacion/Arquitectura/ADR-031-reportes-versionados-en-bd.md`.
- [ ] Crear script de migración en `2 - Script SQL/NNN_motor_reportes_versionados.sql` (tablas `rep_*`, triggers de inmutabilidad, predicados en `seg.pol_tenant`, semilla global `psi_constancia_licencia` v1, PRECHECK, POSTCHECK y ROLLBACK) y registrarlo en `1 - Documetacion/INDICE_SCRIPTS_SQL.md`.
- [ ] Disparar `/alerta-bd` antes de ejecutar el script contra `eBD_SPD`.
- [ ] Agregar paquete NuGet `Scriban` en `eGestion360Web.csproj`.
- [ ] Crear modelos `Models/Reportes/` (`Reporte`, `ReporteVersion`, `ReporteBitacora`, `ReporteEjecucion`) y configuración EF `Data/Configuracion/ReportesConfiguracion.cs`.
- [ ] Actualizar `ScriptsRlsTests` y pruebas de aislamiento RLS en `eGestion360Web.Tests`.
- [ ] Implementar `Services/Reportes/` (`IReporteDataSource`, `ConstanciaPsicologiaDataSource`, `ScribanReporteRenderer`, `MotorReportesService` con caché por `(id_tenant, codigo, id_version)`).
- [ ] Conectar `Pages/Psicologia/Constancias/Imprimir.cshtml.cs` y `Imprimir.cshtml` al motor en BD manteniendo la hoja de prueba (`Guía`) y `psi.impresiones`.

### Fase 2 — Pantallas de gestión, versionamiento y bitácora
- [ ] Registrar permisos `reportes.plantillas.ver`, `reportes.plantillas.editar` y `reportes.plantillas.publicar` en `CatalogoDePermisos` y `saas.permisos`.
- [ ] Crear `Pages/Admin/Reportes/Index.cshtml` (listado con indicador Global vs. Personalizado y versión activa).
- [ ] Crear `Pages/Admin/Reportes/Editar.cshtml` (clonar desde global, editar borrador con formulario visual para `configuracion_json` y editor Scriban, vista previa con datos de prueba, publicar nueva versión).
- [ ] Crear `Pages/Admin/Reportes/Historial.cshtml` (lista de versiones, comparación *diff*, bitácora completa y acción **Revertir a esta versión**).

### Fase 3 — Reportes tabulares (Excel con ClosedXML) y expansión al ERP
- [ ] Implementar `ClosedXmlReporteRenderer` leyendo `configuracion_json` (hojas, columnas, formatos, totales).
- [ ] Migrar exportación de KPI de Flota (`flota.kpi_vehiculos`).
- [ ] Adoptar el mismo motor para futuros reportes de **Facturación** (facturas con CAI, notas, recibos) y **Contabilidad** (libro diario, mayor, balanza, estados financieros).

---

## 7. Plan de verificación y pruebas
- [ ] **Pruebas unitarias:** resolución Tenant → Global, clonación, renderizado Scriban de `psi_constancia_licencia` (casos borde: sexo nulo, DNI nulo, apto/no apto, modo guía).
- [ ] **Pruebas de BD y RLS:** inmutabilidad de versiones publicadas y bitácora, aislamiento entre tenants (tenant A no ve ni modifica versiones del tenant B), `ScriptsRlsTests`.
- [ ] **Prueba funcional en navegador:** imprimir constancia con `Guía`, publicar versión 2 ajustando `fecha.base_mm`, comprobar renderizado con v2, revertir a v1 y verificar trazabilidad en `rep_reporte_bitacora` y `rep_ejecuciones`.
