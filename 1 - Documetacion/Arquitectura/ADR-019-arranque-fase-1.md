# ADR-019 · Decisiones de arranque de la Fase 1 (D18 a D25)

- **Estado:** Aceptada
- **Fecha:** 2026-10-11
- **Decidió:** Emilio Garay (decisiones D18 a D25 del plan de la Fase 1, todas según la recomendación)
- **Fase:** F1
- **Completa:** [ADR-001](ADR-001-aislamiento-multitenant.md), [ADR-003](ADR-003-identidad-y-membresias.md),
  [ADR-004](ADR-004-persona-maestra-por-tenant.md), [ADR-008](ADR-008-autenticacion-cookie-claims.md) y
  [ADR-017](ADR-017-dotnet-10.md).

## Contexto

El plan detallado de la Fase 1 (artifact «Kernel multi-tenant de eGestión360»,
https://claude.ai/artifact/CAgg2PPxuevVLsgdaeV9q7, privado) divide la fase en 10 etapas y deja ocho decisiones
abiertas. Datos medidos el 2026-10-11 con consultas de solo lectura a eBD_SPD:

- 4 empresas: SIP Tecnología (1 usuario), Demo S. de R.L. (1 usuario), una tercera inactiva sin datos de operación y
  Transgar (2 usuarios, 218 vehículos, 6 568 registros de odómetro).
- 97 personas, cada una vinculada a una sola empresa; ninguna eliminada ni fusionada.
- 7 cuentas en `Users`: 1 administrador general sin empresa, 4 usuarios de empresa (2 sin persona) y 2 sin empresa ni
  persona.
- 44 tablas de tenant sin `id_tenant` (unas 19 mil filas), 0 filtros globales de EF, 36 comprobaciones `IsAdmin(`.

## Decisión

| ID | Tema | Se decide |
|---|---|---|
| D18 | Tenants iniciales | Tres tenants activos: **SIP** (tenant interno de la plataforma, ADR-006) con SIP Tecnología; **Demo** con Demo S. de R.L.; **Transgar** con Transgar. La empresa 3, inactiva, va a **un tenant propio en estado suspendido**. |
| D19 | Dónde viven cuentas y membresías | Cuentas y membresías en el esquema `saas`, sin RLS (se leen antes de conocer el tenant). Accesos por empresa en `dbo` como tabla de tenant, con RLS. Llaves de Data Protection en `saas`. |
| D20 | `domain_events` | Sin RLS y con `id_tenant` obligatorio. Solo el despachador la lee, y fija el contexto del tenant de cada evento antes de llamar a los manejadores. |
| D21 | Ensayos de los scripts | Con una copia real de eBD_SPD exportada a LocalDB con SqlPackage (solo lectura en producción). La copia vive solo en la PC de desarrollo, fuera del repositorio, y se borra al cerrar F1. |
| D22 | Alcance de MFA | TOTP (app autenticadora) obligatorio para operadores de la plataforma y administradores de tenant; opcional para el resto; sin SMS. |
| D23 | Administrador general | Pasa a ser operador de la plataforma. «Personas del sistema» queda como herramienta de soporte con acceso auditado por tenant (ADR-004). |
| D24 | Cuentas sin persona o sin empresa | Se migran como cuentas inactivas (hoy tampoco pueden entrar) y se revisan desde la consola del operador. |
| D25 | Orden respecto de .NET 10 | F1 se desarrolla sobre .NET 10 desde ya; su primera publicación espera a que F0.10 (.NET 10 en Somee) esté publicado. |

## Alternativas consideradas

| Decisión | Alternativa | Por qué no |
|---|---|---|
| D18 | Empresa 3 dentro del tenant SIP | Es otra entidad legal sin relación operativa con SIP; mezclarla obliga a separarla después. |
| D18 | Retirar la empresa 3 | Tiene un vínculo de persona; suspendida conserva el dato sin costo ni acceso. |
| D19 | Todo en `dbo` con RLS | Las cuentas se buscan al iniciar sesión, antes de que exista un contexto de tenant. |
| D20 | `domain_events` con RLS | El despachador recorre eventos de todos los tenants; con RLS necesitaría una conexión por tenant solo para leer la cola. |
| D21 | Esquema sin datos y datos inventados | Más seguro con los datos personales, pero el ensayo no reproduce las claves ni los volúmenes reales. |
| D22 | MFA para todos | Agrega fricción a usuarios operativos sin un riesgo equivalente; queda opcional. |
| D24 | No migrar esas cuentas | Se perdería el rastro de quién existía; inactivas no dan acceso. |
| D25 | Publicar F1 sobre .NET 8 | .NET 8 deja de recibir parches el 10 nov 2026 y F1 usa los filtros con nombre de EF 10. |

## Consecuencias

- El script 025 da de alta 4 tenants (3 activos y 1 suspendido) y asigna cada empresa a su tenant.
- El `id_tenant` de las personas sale de su única empresa: no hay que duplicar personas.
- Hace falta instalar SqlPackage en la PC de desarrollo para exportar la copia (D21). La copia contiene datos
  personales: no se sube al repositorio ni a ningún servicio, y se borra al cerrar F1.
- El rol `admin` deja de existir como rol de tenant: el administrador general entra como operador, con MFA.
- Las 4 cuentas sin persona quedan inactivas; la consola del operador permite reactivarlas o darlas de baja.
- La primera publicación de F1 queda atada a resolver el espacio del sitio en Somee (F0.10).

## Cómo se verifica

- Después del script 025: cada empresa tiene tenant; el tenant de la empresa 3 está suspendido.
- Después de F1.5: ninguna cuenta ni membresía está en `dbo`; los accesos por empresa tienen RLS.
- Pruebas de los jobs: el despachador procesa eventos de dos tenants y cada manejador ve solo su tenant.
- La copia de D21 no aparece en `git status` ni en ninguna carpeta del repositorio, y no existe al cerrar F1.
