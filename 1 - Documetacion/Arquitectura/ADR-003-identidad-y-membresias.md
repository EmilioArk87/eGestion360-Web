# ADR-003 · Identidad: cuenta de acceso global con membresías por tenant

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D3 del plan)
- **Fase:** F1

## Contexto

- Hoy cada usuario (`Users`) pertenece a una sola empresa (`EmpresaId`) y tiene un rol de texto (`admin`,
  `empresa_admin`, `empresa_user`). No está vinculado a su persona. Existe además la tabla legada `usuarios`.
- Los permisos son 4 casillas por módulo (ver, crear, editar, eliminar); no alcanzan para un ERP (aprobar, anular,
  mayorizar, cerrar periodo, ver salarios).
- Casos reales a cubrir: un contador externo que lleva varios clientes; un grupo con varias sociedades.

## Decisión

- **Cuenta de acceso** global: correo, contraseña (BCrypt), MFA y estado. Una por ser humano en toda la plataforma.
- **Membresía:** cuenta ↔ tenant, con estado (invitada, activa, suspendida) y, opcional, la persona del tenant que la
  representa (para que un empleado vea su propia ficha).
- **Acceso por empresa:** membresía ↔ empresa ↔ rol, con alcance opcional por sucursal.
- **Operadores de la plataforma** con sus propios roles, separados de los usuarios de los tenants.
- Al iniciar sesión, si la cuenta tiene más de un tenant o empresa, se elige. Cambiar de tenant vuelve a validar la
  membresía.
- **Permisos:** catálogo `modulo.recurso.accion` (por ejemplo `contabilidad.asiento.mayorizar`) que declaran los
  módulos; cada tenant arma sus roles con ese catálogo, a partir de roles plantilla.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Usuario atado a un tenant | Un contador con cinco clientes tendría cinco cuentas y cinco contraseñas; no hay forma limpia de darle acceso a un grupo. |

Si una firma contable contrata la plataforma para todos sus clientes, la firma es un tenant con varias empresas; el
modelo soporta los dos casos.

## Anexo · Paso intermedio hasta F1: `Users.PersonaId` (2026-10-10)

Emilio pidió que cada usuario quede vinculado a la persona que lo usa antes de construir el kernel de F1 (plan
«Usuarios y personas», decisiones U1 a U5, aprobadas el 2026-10-10).

- **Dónde:** columna `Users.PersonaId` (script 021), con clave foránea a `personas` y sin cascada. Hoy cada usuario
  pertenece a una sola empresa, así que equivale a «la persona de su única membresía».
- **La persona queda dentro de la empresa del usuario:** si no tiene relación con ella, se le registra un vínculo de tipo
  `usuario` en `persona_empresa`. Para quien no tiene empresa (el administrador general) se usa la empresa dueña de la
  plataforma (`Plataforma:IdEmpresaPropia`, SIP), que en F1 será el tenant interno de [ADR-006](ADR-006-factura-fiscal-de-la-plataforma.md).
- **Varios usuarios por persona** mientras exista el modelo actual (`admin` y `egaray` son la misma persona). Obligatoria
  al crear usuarios nuevos; los existentes se vinculan desde Editar.
- **Obligatoria para entrar** (cambio de U3 pedido por Emilio el mismo 2026-10-10): un usuario sin persona vinculada no
  inicia sesión, y la barra y las bienvenidas muestran el nombre de la persona en lugar del nombre de usuario. En F1 la
  regla pasa a la membresía: sin persona en la membresía del tenant elegido, no se entra a ese tenant.
- **Migración en F1:** `PersonaId` pasa a `membresia.id_persona` del tenant de la empresa del usuario; `admin` y `egaray`
  se unen en una sola cuenta (operador de la plataforma y miembro del tenant de SIP); la columna se retira con `Users`.
- Mientras tanto, una tabla de plataforma (`Users`) apunta a datos de un tenant (`personas`). Es temporal y aceptada
  aquí; en el arnés de F0.6 `Users` sigue clasificada como Plataforma.

## Consecuencias

- Migración de los 7 usuarios actuales: el administrador general pasa a ser operador de la plataforma; los demás reciben
  cuenta, membresía y acceso a su empresa.
- `empresa_roles` y `empresa_rol_permisos` evolucionan a roles del tenant con el catálogo de permisos.
- Los permisos dejan de copiarse a la sesión en el login ([ADR-008](ADR-008-autenticacion-cookie-claims.md)).

## Cómo se verifica

- Una cuenta con membresía en dos tenants solo ve los datos del tenant elegido.
- Un usuario sin acceso a una empresa del tenant no la ve ni puede operar en ella.
- Un cambio de rol se aplica en la petición siguiente, sin cerrar sesión.
