# Verificación y operación de F1.9 (consola del operador)

Complemento de [`LEEME.md`](LEEME.md). Decisiones en [ADR-032](../../Arquitectura/ADR-032-consola-del-operador.md) (D77 a D84, aceptadas el
2026-11-25).

## F1.9a: suscriptores, estado y plan

| Pantalla | Ruta | Quién la abre |
|---|---|---|
| Suscriptores | `/Admin/Suscriptores` | El operador de la plataforma, en el sitio de plataforma |
| Un suscriptor | `/Admin/Suscriptores/Detalle?id=N` | El mismo; cambiar el estado y el plan piden su propio permiso |

Cada cambio pide un **motivo** y escribir el **código** del suscriptor. Queda en `dbo.auditoria_seguridad` del suscriptor y en
`saas.auditoria_plataforma` (`tenant.estado`, `tenant.plan`).

### Script 049 (con `/alerta-bd`)

`2 - Script SQL/049_permisos_consola_operador.sql` agrega a `saas.permisos` 6 permisos de plataforma (orden 94 a 99). No toca roles ni
plantillas. Se verifica con `verificar_despues_041.sql`, que acepta las cifras con o sin el 049:

| Comprobación | Sin el 049 | Con el 049 |
|---|---|---|
| 210 Permisos en el catálogo | 93 | 99 |
| 230 De plataforma (por módulo) | 18 | 24 |
| 240 De plataforma (por ámbito) | 18 | 24 |

### Orden de publicación

1. Ensayar el 049 en la copia local: aplicar, repetir («Nada que hacer»), revertir, PRECHECK que falla y reaplicar. **Hecho el 2026-11-25**
   en `egestion_f18c_union`, con `verificar_despues_041.sql`, `f17` y `045` sin fallas.
2. `/alerta-bd` y aprobación expresa; aplicar el 049.
3. `verificar_despues_041.sql` con las cifras de «Con el 049» y la comprobación diaria.
4. Publicar el código desde `dev`.

**El 049 va ANTES del código**: sin las filas nadie abre las pantallas nuevas. Al revés no pasa nada: el código anterior ignora los permisos
nuevos y la pantalla de roles no ofrece permisos de plataforma.

### Qué verificar después de publicar

1. Como operador, en el sitio de plataforma, abrir «Suscriptores»: salen todos, con su estado y su plan.
2. En el de **demostración**: suspender en solo lectura, comprobar el aviso y reactivar.
3. Ver las dos filas en «Auditoría de la plataforma».
4. Con un usuario de una empresa, «Suscriptores» no aparece en el menú.

### Si algo sale mal

- Un suscriptor suspendido por error se reactiva desde la misma pantalla; se nota en menos de un minuto.
- Si la consola no abre y hay que reactivar a un cliente, es un `UPDATE` de `saas.tenants` (con `/alerta-bd`).
- `2 - Script SQL/emergencia_derechos_abrir.sql` sigue sirviendo si un plan dejara a alguien sin acceso.

### Recorrido local (2026-11-25)

SQL Server local con la política de aislamiento encendida: 33 de 33 comprobaciones en navegador (lista, suscriptor interno sin cambios,
confirmación equivocada, suspensión en lectura y en bloqueo vista por su usuario, reactivación, dos pestañas a la vez, cambio de plan con su
revisión, cancelar y recuperar, las filas de auditoría en las dos tablas, quién no puede entrar y vista de teléfono).
