# ADR-032 · Consola del operador (D77 a D84)

- **Estado:** Aceptada (Emilio, 2026-11-25: las ocho recomendaciones del plan de F1.9)
- **Fecha:** 2026-11-25
- **Decidió:** Emilio (D77 a D84); Claude (detalles de implementación, marcados como tales)
- **Fase:** F1.9
- **Completa:** [ADR-025](ADR-025-tenant-de-soporte.md) (soporte), [ADR-026](ADR-026-derechos-de-uso-y-estado-del-tenant.md) (derechos y estado) y
  [ADR-030](ADR-030-auditoria-de-seguridad.md) (auditoría)

## Contexto

La aplicación ya hace cumplir el estado de un suscriptor (en el código, tenant) y su plan, pero ninguna pantalla los cambiaba: dar de alta un
cliente, suspenderlo o cambiarle el plan exigía un script contra la base. F1.9 lo lleva a pantallas que solo usa el operador de la plataforma.

## Decisión

| # | Decisión | Detalle |
|---|----------|---------|
| D77 | Cuatro entregas | F1.9a estado y plan; F1.9b alta de un suscriptor; F1.9c soporte con motivo, vencimiento y aviso; F1.9d accesos de las personas. |
| D78 | El alta crea lo necesario para trabajar | Suscriptor, plan, primera empresa, módulos del plan, roles de las plantillas de esos módulos y catálogos de partida según el módulo (F1.9b). |
| D79 | El primer administrador entra por invitación | La invitación por correo registra a su persona en el mismo paso; nadie más conoce su contraseña (F1.9b). |
| D80 | Suspender y cancelar piden motivo y confirmación | Motivo obligatorio y escribir el código del suscriptor. El interno no se suspende ni se cancela. Cancelar solo desde suspendido, sin borrar datos, y se puede volver a suspendido. |
| D81 | Los planes siguen creándose por script | La consola asigna versiones de plan que ya existen. Crear planes o cambiar sus topes espera a que haya cobro (F4). |
| D82 | El soporte vence | 1 hora por defecto, a elegir 30 minutos, 1 hora o 4 horas; al vencer se sale solo (F1.9c). |
| D83 | El suscriptor se entera del soporte | La entrada queda en su auditoría con el motivo y sus administradores reciben un correo de mejor esfuerzo; sin correo para el interno y el de demostración (F1.9c). |
| D84 | En pantalla se llaman «Suscriptores» | Así no se confunden con los clientes de una empresa ni con las cuentas de usuario. |

## F1.9a: estado y plan

- **Pantallas:** `/Admin/Suscriptores` (lista con estado, plan, empresas y usuarios activos frente a su tope) y `/Admin/Suscriptores/Detalle`.
  Corren en ámbito de plataforma y piden permisos de plataforma, que solo tiene el operador.
- **Permisos** (script 049, orden 94 a 99): `plataforma.suscriptores.ver`, `.crear`, `.estado`, `.plan` y `plataforma.accesos.ver`, `.editar`. Van
  los seis juntos para no repetir la alerta de base de datos; `crear` y los de accesos esperan sus pantallas.
- **Estado:** de activo a suspendido (solo lectura o bloqueo); de suspendido a activo, al otro modo o a cancelado; de cancelado a suspendido.
- **Plan:** se revisa antes (qué se gana, qué se pierde, qué topes quedarían superados) y se cambia cerrando la fila vigente de
  `saas.tenant_planes` y abriendo la nueva en una transacción. Ningún dato se borra.
- **Auditoría:** `tenant.estado` y `tenant.plan`, en las dos tablas y en la misma transacción que el cambio (ADR-030).
- **Efecto inmediato:** al guardar se olvida lo recordado del suscriptor (D55); en otras instancias, 30 segundos como máximo.

### Detalles de implementación decididos por Claude (a confirmar al publicar)

- El plan del suscriptor **interno** tampoco se cambia desde la consola: es el que sostiene al operador.
- Un suscriptor **cancelado** no cambia de plan hasta recuperarlo.
- Si otra persona cambió el suscriptor mientras tanto, el cambio se rechaza y se pide volver a cargar la página.

## Alternativas consideradas

| # | Alternativa | Por qué no |
|---|-------------|-----------|
| D77 | Dejar los accesos para F1.10 | El acceso de una persona ya pidió un script una vez; la consola quedaría incompleta. |
| D78 | Alta solo de suscriptor y empresa | El resto volvería a hacerse a mano, que es lo que se quiere evitar. |
| D79 | Contraseña temporal escrita por el operador | La conocería otra persona. |
| D80 | Sin botón de cancelar | Seguiría exigiendo SQL. |
| D81 | Editor de planes | Suma pantallas, un script y control de concurrencia antes de que haya cobro. |
| D82 | Soporte sin vencimiento | Una sesión olvidada seguiría viendo datos del suscriptor. |
| D83 | Solo auditoría | El suscriptor tendría que ir a buscarlo. |
| D84 | «Cuentas de cliente» | Se confunde con las cuentas de usuario. |

## Cómo se verifica

- `Services/Plataforma/ServicioDeSuscriptoresTests`: transiciones, motivo y confirmación, suscriptor interno, concurrencia, planes disponibles,
  las dos filas de auditoría y que sin auditoría no hay cambio.
- `Paginas/SuscriptoresPaginasTests` y `Services/Permisos/PermisosPageFilterTests`: las pantallas y quién las abre.
- Recorrido en navegador sobre SQL Server con la política de aislamiento encendida (ver `1 - Documetacion/Operacion/F1/LEEME_F19.md`).
