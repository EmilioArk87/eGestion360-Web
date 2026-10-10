# ADR-016 · Hosting de producción: Azure App Service en Linux y Azure SQL Database

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D16 del plan)
- **Fase:** staging en Azure durante F1; corte de producción antes de F4
- **Completa:** [ADR-007](ADR-007-hosting.md), que fijó los requisitos y dejó la elección del proveedor para un ADR nuevo.

## Contexto

ADR-007 pide, antes del primer cliente externo, un hosting con proceso siempre activo, recuperación a un punto en el
tiempo, staging, TLS y despliegue automatizado. El paso F0.9 comparó cinco opciones con precios de lista del
2026-10-10 (artifact «Hosting de eGestión360», https://claude.ai/artifact/XBhZiWF8A8GEtK8nEUMQQG, privado).

Estado de Somee, verificado el 2026-10-10 con consultas de solo lectura y las páginas públicas de Somee:

- La cuenta usa el plan pagado **Advanced**: la base tiene un tope de 1 000 MB de datos y 1 000 MB de log, y corre en
  SQL Server 2022 **Web Edition** (el plan gratuito usa Express y 30 MB).
- El login de la aplicación es `db_owner`: puede apagar la política de RLS (límite anotado en ADR-015).
- La base está en modelo de recuperación SIMPLE con un respaldo completo por día.
- IIS descarga la aplicación sin visitas: el job de tasas de cambio necesita un disparador externo. El despachador del
  outbox (`OutboxDispatcherBackgroundService`, cada 5 s) tampoco corre mientras la app está dormida.
- No hay staging, la publicación es por FTP y el usuario de la base no puede crear bases nuevas.

Requisitos y resultado por opción (detalle en el artifact):

| Requisito | Azure Linux | Azure Windows | AWS (Lightsail + RDS Express) | VPS propio | Somee |
|---|---|---|---|---|---|
| RLS y `SESSION_CONTEXT` | Sí | Sí | Sí | Sí | Sí |
| Usuario de la app sin poder apagar RLS | Sí | Sí | Sí | Sí | No |
| Procesos de fondo siempre activos | Sí | Sí | Sí | Sí | No |
| Recuperación a un punto en el tiempo | 1–35 días | 1–35 días | Hasta 35 días | Manual | No |
| Staging | Sin costo extra | Sin costo extra | ≈ US$ 25 | ≈ US$ 12 | No |
| Tamaño máximo de la base | 250 GB | 250 GB | 10 GB | 10 GB | 1 GB (plan actual) |
| Costo mensual con staging | **≈ US$ 28** | ≈ US$ 69 | ≈ US$ 72 | ≈ US$ 41 + operación | Plan actual |

## Decisión

- **Producción:** Azure App Service, plan **B1 Linux**, región **South Central US**, y Azure SQL Database **S0** (modelo
  DTU) en un servidor lógico de la misma región.
- **Staging:** una segunda app en el mismo plan B1, sin costo extra, con una base de la **oferta gratuita** de Azure SQL
  (serverless con auto-pausa: 100 000 vCore-segundos, 32 GB de datos y 32 GB de respaldo por mes). Si no alcanza, base
  Basic.
- **Acceso a la base:** el administrador es una cuenta de Microsoft Entra. La aplicación entra con un usuario contenido
  que solo lee, escribe y ejecuta: sin `db_owner` ni `ALTER`, así que no puede apagar la política de RLS. Esto cierra el
  límite que ADR-015 anotó para Somee.
- **Respaldo:** recuperación a un punto en el tiempo de 35 días con almacenamiento geo-redundante.
- **Secretos:** en la configuración de App Service y Key Vault; ninguno en el repositorio ni en el paquete.
- **Despliegue:** GitHub Actions con credencial federada (OIDC), sin claves guardadas en GitHub. Cada push publica en
  staging; producción se publica a mano desde el workflow, con aprobación de Emilio.
- **Infraestructura como código:** los recursos se describen en Bicep dentro del repositorio.
- **Calendario:** staging en Azure durante F1, para probar el kernel en el hosting final; corte de producción antes de
  F4, como pide ADR-007. Somee queda 30 días en solo lectura como vuelta atrás.
- **Si la app no corre en Linux** y no se resuelve en staging, el plan cambia a B1 Windows sin tocar la base
  (US$ 41.61 más al mes). El cambio se anota en este ADR.

Precios de lista usados (USD, sin impuestos, mes de 730 horas; las bases DTU se cobran por día):

| Recurso | Precio | Mes |
|---|---|---|
| App Service B1 Linux, South Central US | 0.018 por hora | 13.14 |
| Azure SQL S0, South Central US | 0.4839 por día | 14.72 |
| App de staging en el mismo plan | — | 0.00 |
| Base de staging, oferta gratuita | — | 0.00 |
| Base Basic (alternativa para staging) | 0.161 por día | 4.90 |

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Azure en Windows (B1) | Cumple igual, pero el plan cuesta US$ 54.75 contra US$ 13.14. Queda como plan B si Linux falla. |
| Azure en Mexico Central | Más cerca, unos US$ 2 más al mes; habría que confirmar respaldos geo-redundantes y la oferta gratuita en esa región. |
| AWS: Lightsail + RDS SQL Server Express | Express limita cada base a 10 GB y 1 410 MB de búfer; staging se paga completo; crecer exige ediciones Web o Standard, mucho más caras. |
| VPS propio con SQL Server Express | Parches, respaldos de log, TLS y monitoreo a cargo nuestro; Express no trae SQL Agent; un solo servidor sin respaldo administrado. |
| Quedarse en Somee | Falla 6 de los 10 requisitos de F0.9, entre ellos PITR, procesos activos y el usuario sin `db_owner`. |

## Consecuencias

- **Código (antes del corte):** opción para apagar el despachador del outbox en staging (sin ella, la base gratuita
  nunca se pausa), endpoint de salud, corrida del CI en Linux además de Windows, y la página de mantenimiento, que hoy es
  el `app_offline.htm` de IIS.
- **Base:** se migra con un `.bacpac` exportado de eBD_SPD. Ningún script del repositorio usa SQL Agent, CLR ni
  consultas entre bases; solo uno viejo de `sql_scripts/` usa `USE`.
- **Sesión:** la sesión en memoria sirve con una instancia. Para escalar a dos o más, afinidad de sesión o caché
  distribuida.
- **Costo:** unos US$ 28 al mes al empezar; con los primeros clientes, entre US$ 55 (B2 + S1) y US$ 98 (P0v3 + S1).
  Presupuesto con alerta en Azure Cost Management desde el primer día.
- **Operación:** B1 no tiene slots de despliegue: cada publicación reinicia la app unos segundos. Con clientes que
  pagan, subir a S1 (US$ 45 más al mes) para publicar sin cortes.

## Cómo se verifica

- Staging: la app corre en Linux y pasan las pruebas E2E de Playwright contra ese entorno.
- El usuario de la aplicación no puede ejecutar `ALTER SECURITY POLICY` (prueba contra la base de staging).
- Antes de F4: restauración de prueba a un punto en el tiempo, documentada (lo pide ADR-007).
- El costo real del primer mes en Cost Management no pasa de US$ 35.
