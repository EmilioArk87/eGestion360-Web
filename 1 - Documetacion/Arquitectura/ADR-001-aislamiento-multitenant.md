# ADR-001 · Aislamiento multi-tenant: base compartida con id_tenant y Row-Level Security

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D1 del plan)
- **Fase:** prueba de concepto en F0; implementación en F1

## Contexto

- Hoy el sistema es multiempresa, no multi-tenant: la empresa hace de cliente, de frontera de aislamiento y de entidad
  legal a la vez (ver [ADR-002](ADR-002-jerarquia-organizativa.md)).
- El aislamiento depende de que cada página filtre por `id_empresa`: hay 96 filtros manuales `IdEmpresa ==` en
  `Pages/`, ningún `HasQueryFilter` y ninguna política de Row-Level Security (consulta del 2026-10-04: 59 tablas, 39 con
  `id_empresa`, 0 políticas). Ese modelo ya produjo dos hallazgos: la edición de Personas sin filtro por empresa y las
  pantallas de Flota que caían en la empresa 1.
- La base pesa 30 MB y tiene 4 empresas: es el momento más barato para cambiar el modelo.
- Hosting (verificado el 2026-10-10 con consultas de solo lectura): el usuario de la base es `db_owner` y puede crear
  políticas de seguridad, funciones y esquemas; no puede crear bases nuevas.

## Decisión

Base y esquema compartidos. Toda tabla que guarda datos de un tenant lleva la columna `id_tenant`, incluidas las de
detalle (hoy `factura_detalle`, `pago_aplicaciones` y `condiciones_pago_cuotas` no llevan ni `id_empresa`). El
aislamiento se aplica en capas independientes:

1. **Tenant resuelto en el servidor.** Sale del claim de la cookie de autenticación firmada
   ([ADR-008](ADR-008-autenticacion-cookie-claims.md)); nunca de la URL, de un campo oculto ni de un parámetro. Cambiar
   de tenant es una acción explícita que vuelve a validar la membresía.
2. **Autorización.** Derecho de uso del plan, permiso del rol, empresas permitidas al usuario y estado del tenant.
3. **EF Core.** Interfaz `ITenantEntity`; filtro global por tenant y borrado lógico en la misma expresión (EF Core 9
   admite un solo filtro por entidad); interceptor de `SaveChanges` que asigna `id_tenant` al insertar y rechaza
   modificarlo. `IgnoreQueryFilters()` solo en código de plataforma revisado.
4. **SQL Server Row-Level Security.** Función de predicado `seg.fn_tenant_actual` que compara la columna con
   `SESSION_CONTEXT(N'id_tenant')`; política con predicado de filtro y predicados de bloqueo `AFTER INSERT` y
   `AFTER UPDATE`. Un interceptor de conexión de EF fija el contexto al abrir cada conexión con
   `sp_set_session_context @key = N'id_tenant', @value = ..., @read_only = 1`. Las tablas mixtas (fila global con
   `id_tenant` nulo más filas propias del tenant, como `tasas_cambio`) usan un predicado de lectura que acepta el nulo
   y uno de escritura que no.
5. **Claves foráneas compuestas.** Toda FK entre tablas de tenant incluye `id_tenant`, por ejemplo
   `facturas(id_tenant, id_cliente) → clientes(id_tenant, id_cliente)`. Es el mismo patrón que ya usan
   `clientes → persona_empresa` (014) y el script 010 revisado.
6. **Pruebas automáticas de aislamiento** que recorren todas las entidades.

Además:

- Todo índice de consulta empieza por `id_tenant`.
- La cadena de conexión se resuelve por tenant desde el inicio (catálogo de tenants), aunque hoy todas apunten a la misma
  base. Eso permite mover un tenant grande a su propia base (modelo híbrido) sin cambiar código.
- Los jobs (outbox, cobros, tasas) procesan tenant por tenant fijando el contexto. Los reportes entre tenants usan un
  usuario SQL de plataforma distinto, de solo lectura y auditado.
- Un operador de la plataforma solo entra a los datos de un tenant con un acceso de soporte temporal, con motivo,
  aviso al tenant y bitácora.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Base por tenant | Aislamiento físico, pero el usuario de Somee no puede crear bases, cada script de esquema se ejecutaría N veces (cada una con `/alerta-bd`) y el costo por tenant es alto. Queda como destino para tenants grandes (modelo híbrido). |
| Esquema por tenant | EF Core cachea el modelo por esquema, las migraciones se repiten N veces y no aporta más seguridad que RLS. |
| Base compartida solo con filtro en la aplicación | Es lo que hay hoy: depende de no olvidar un filtro, y ya falló dos veces. |

## Consecuencias

- La base rechaza filas de otro tenant aunque la aplicación tenga un error; cada cambio de esquema se aplica una vez.
- Hay que agregar `id_tenant` a las tablas de negocio y reemplazar los filtros manuales. Se hace por grupos de tablas con
  el patrón expandir → migrar → contraer, como se hizo con personas (scripts 014 a 019).
- Restaurar un solo tenant exige un procedimiento escrito y ensayado (restaurar la base en staging y copiar sus filas).
- Las pruebas actuales usan SQLite, que no tiene RLS: las pruebas de aislamiento necesitan SQL Server (LocalDB en F0).
- Riesgo: un predicado mal escrito puede bloquear jobs o dejar pasar filas. Lo cubre la prueba de concepto de F0.5.

## Cómo se verifica

- **F0.5:** prueba de concepto en LocalDB con dos tenants: una consulta sin filtro de EF no devuelve filas del otro
  tenant, un INSERT o UPDATE con otro `id_tenant` falla, un job recorre tenants fijando el contexto, una tabla mixta se
  comporta como se describe y el plan de ejecución usa el índice.
- **F1:** prueba que recorre todas las entidades del modelo de EF y verifica que el tenant B no lee, no edita, no borra
  ni referencia datos de A, por EF y por SQL directo; falla si una entidad de tenant no tiene `id_tenant`. Revisión
  manual de IDOR (cambiar ids en URL y formularios).
