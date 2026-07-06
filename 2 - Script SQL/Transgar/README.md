# Carga de datos — Transportes Garay (Transgar)

Scripts para dar de alta la empresa **Transportes Garay** (nombre comercial **Transgar**,
código `TRANSGAR`) en el módulo de **Flota**, con los datos del libro
`T.M. COMB Y MTO (2021-04-14).xlsx` (flota histórica de Transportes Martínez).

La empresa se configura **emulando la empresa Demo** (Compañía para demostración, `id_empresa = 2`):
copia sus módulos activos y crea roles equivalentes.

Todos los scripts son **idempotentes** (se pueden re-ejecutar sin duplicar) y ninguno
modifica datos de otras empresas.

## Orden de ejecución

| # | Script | Contenido |
|---|--------|-----------|
| 1 | `00_transgar_empresa_usuarios.sql` | Empresa + módulos (copiados de la Demo) + 3 roles + **2 usuarios** |
| 1b | `05_transgar_suscripcion.sql` | Suscripción: deja **solo el módulo `flota`** activo (idempotente) |
| 2 | `20_transgar_vehiculos.sql` | 8 tipos de vehículo + **218 vehículos** (hoja DATA) |
| 3 | `30_transgar_personas.sql` | **68 empleados** (hoja «No. EMPLEADO») |
| 4 | `40_transgar_cargas_combustible.sql` | **4.798 cargas de combustible** (hoja «Control de Combustible») |
| 5 | `50_transgar_ordenes_mantenimiento.sql` | Taller interno + **729 órdenes de mantenimiento** (hoja «Historial de Mantenimientos») |
| 6 | `60_transgar_odometro_diario.sql` | **6.568 odómetros diarios** (KM INICIAL/KM FINAL, agregados por vehículo/fecha) |
| 7 | `06_transgar_admin_operativo.sql` | Deja `transgar.admin` como usuario **operativo** (empresa_user, rol Administrador con permisos completos) para que vea/use los módulos |

> Respete el orden: 20 depende de 00; 30 depende de 00; 40 depende de 00/20/30; 50 depende de 00/20.

## Cómo aplicar (sqlcmd)

Desde esta carpeta. El flag `-I` (QUOTED_IDENTIFIER) es obligatorio en esta base
(índices filtrados), si no fallan los INSERT con Msg 1934.

```bash
SRV="eBD_SPD.mssql.somee.com"; USR="acc_datos"; PWD='a3XQ#@z^'; DB="eBD_SPD"
for f in 00_transgar_empresa_usuarios.sql 20_transgar_vehiculos.sql 30_transgar_personas.sql 40_transgar_cargas_combustible.sql 50_transgar_ordenes_mantenimiento.sql; do
  echo ">>> $f"
  sqlcmd -S "$SRV" -U "$USR" -P "$PWD" -d "$DB" -I -b -i "$f" || break
done
```

`-b` aborta la secuencia si un script falla.

## Usuarios creados

| Usuario | Contraseña inicial | Rol | Acceso |
|---------|--------------------|-----|--------|
| `transgar.admin` | `Transgar#2026` | empresa_admin | Total sobre los módulos de la empresa |
| `transgar.flota` | `Transgar#2026` | empresa_user → «Operador de Flota» | Ver/crear/editar en Flota |

Las contraseñas se guardan en texto plano y el sistema las **migra a BCrypt en el primer
inicio de sesión** (comportamiento normal de la app). **Cámbielas después de entrar.**

## Mapeo de datos y decisiones

- **Vehículos**: `numero_interno` = código de unidad (`Cab34`, `FM12`, `LB01`…).
  La `placa` usa la matrícula real cuando es válida y única; si falta o está repetida en el
  libro, se usa el código de unidad (la placa debe ser única por empresa).
  El `tipo_vehiculo` se deduce del prefijo: Cab/FL→Cabezal, FM→Cisterna, LB→Low Boy,
  GRU→Grúa, U→Pick-up, Vol→Volqueta, AS→Asfaltero.
- **Empleados** → `personas`. El prefijo del No. de empleado define el cargo
  (1x=OTRO/administración, 2x=MECANICO/taller, 3x y 4x=CONDUCTOR/motoristas), porque la
  BD sólo admite los cargos CONDUCTOR/COBRADOR/MECANICO/SUPERVISOR/OTRO. Se omiten filas sin nombre.
- **Combustible** → `cargas_combustible`. Sólo se cargan las filas con **galones > 0**
  (2.776 filas de «tráiler» con 0 galones quedan fuera, no son compras de combustible).
  El `no_factura` es sintético y único (`TMC-<fila>`) porque el libro no trae número de factura.
  La ruta va en `observaciones`. Se conservan los odómetros negativos de las primeras filas
  (dato histórico tal cual). El conductor queda nulo cuando el libro no trae No. de empleado.
- **Mantenimiento** → `ordenes_mantenimiento` desde «Historial de Mantenimientos»
  (bitácora real). `no_factura` sintético `TMM-<fila>`; montos en 0 (el libro no los trae);
  tipo PREVENTIVO/CORRECTIVO según el texto. Taller genérico «Taller Interno Transgar».
  Se omiten filas sin fecha o sin unidad.

## Fuera de alcance (el libro no tiene estos datos)

Pólizas de seguro, salarios diarios y gastos de repuestos/llantas no se cargan porque
el libro no los contiene. La hoja «Mantenimientos» (estado/próximo mantenimiento) es un
tablero calculado y no se importa como transacciones.

**Odómetro diario** (script 60): un registro por vehículo/fecha, tomando la fila de **mayor
KM FINAL** del día con su propio par (km_inicial, km_final) — así se evita mezclar filas y
se respeta el índice único y el CHECK `km_final ≥ km_inicial`. La carga es limpia (borra los
odómetros previos de la empresa antes de insertar). Ojo: ~75 días (≈1,1%) traen distancias
imposibles porque la propia fila del libro está mal (KM INICIAL en 0/1 con un KM FINAL enorme,
o un KM FINAL mal tecleado); conviene neutralizarlos antes de usar KPIs de rendimiento.

## Reversión

Para deshacer una carga (borra TODO lo de la empresa Transgar):

```sql
DECLARE @e INT = (SELECT id_empresa FROM dbo.empresas WHERE codigo='TRANSGAR');
DELETE FROM dbo.cargas_combustible      WHERE id_empresa=@e;
DELETE FROM dbo.odometro_diario         WHERE id_empresa=@e;
DELETE FROM dbo.ordenes_mantenimiento   WHERE id_empresa=@e;
DELETE FROM dbo.vehiculos               WHERE id_empresa=@e;
DELETE FROM dbo.tipos_vehiculo          WHERE id_empresa=@e;
DELETE FROM dbo.talleres                WHERE id_empresa=@e;
DELETE FROM dbo.personas                WHERE id_empresa=@e;
DELETE FROM dbo.Users                   WHERE EmpresaId=@e;
DELETE FROM dbo.empresa_rol_permisos    WHERE id_rol IN (SELECT id_rol FROM dbo.empresa_roles WHERE id_empresa=@e);
DELETE FROM dbo.empresa_roles           WHERE id_empresa=@e;
DELETE FROM dbo.empresa_modulos         WHERE id_empresa=@e;
DELETE FROM dbo.empresas                WHERE id_empresa=@e;
```
