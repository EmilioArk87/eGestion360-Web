# Pruebas automatizadas

Proyecto `eGestion360Web.Tests` (xUnit, .NET 8), en la carpeta del mismo nombre. Se excluye de la compilación
y de la publicación del sitio (`eGestion360Web.csproj`).

## Cómo correrlas

```bash
dotnet test eGestion360Web.Tests/eGestion360Web.Tests.csproj
```

Casi todas usan SQLite en memoria y no necesitan SQL Server, ni internet, ni secretos. Las de `SqlServer/` usan LocalDB
si está instalado y, si no, se omiten (ver más abajo). **Ninguna toca la base real.**

## Integración continua

`.github/workflows/ci.yml` corre en GitHub Actions en cada push a cualquier rama y en cada pull request (también se
puede lanzar a mano desde la pestaña *Actions*). En una máquina Windows, igual que producción:

1. Restaura los paquetes NuGet de la solución.
2. Compila el sitio en Debug y en Release.
3. Corre todas las pruebas de este proyecto.

Si un paso falla, la corrida queda en rojo en GitHub. Los resultados (`resultados.trx`) quedan como artefacto de la
corrida durante 14 días. No usa secretos: el flujo solo tiene permiso de lectura del repositorio.

Para reproducir la corrida en local, los mismos comandos:

```bash
dotnet restore eGestion360-Web.sln
dotnet build eGestion360Web.csproj --configuration Debug --no-restore
dotnet build eGestion360Web.csproj --configuration Release --no-restore
dotnet test eGestion360Web.Tests/eGestion360Web.Tests.csproj --configuration Debug --no-restore
```

## Pruebas contra SQL Server (LocalDB)

Lo que SQLite no puede probar (Row-Level Security, claves foráneas compuestas reales, planes de ejecución) se prueba en
`SqlServer/` contra **SQL Server 2022 LocalDB**, el motor de SQL Server instalado solo en la máquina, sin servidor ni
claves (autenticación de Windows). Cada corrida crea una base propia con nombre único (`egestion_prueba_…`), la llena
con datos inventados y la borra al terminar. **Nunca se conectan a eBD_SPD.**

- Se marcan con `[FactSqlServer]` y el rasgo `Categoria=SqlServer`.
- Si la máquina no tiene LocalDB, aparecen como **omitidas** con el motivo, no como fallidas. Así la suite sigue pasando
  en una máquina sin LocalDB (por ejemplo, si el runner de GitHub no lo trae).
- Para usar otro servidor de pruebas, definir la variable de entorno `EGESTION_PRUEBAS_SQLSERVER` con su cadena de
  conexión, sin base: cada prueba crea la suya. Nunca apuntarla a un servidor con datos reales.
- Instalar LocalDB: paquete `SqlLocalDB.msi` de SQL Server 2022 desde download.microsoft.com (el mismo motor 16.x de
  eBD_SPD). Se verifica con `sqllocaldb info MSSQLLocalDB`.

```bash
dotnet test eGestion360Web.Tests/eGestion360Web.Tests.csproj --filter "Categoria=SqlServer"
```

| Carpeta | Qué prueban |
|---|---|
| `SqlServer/Rls/RlsPocTests` | Prueba de concepto de RLS (paso F0.5, ADR-001 y ADR-015) sobre un esquema de laboratorio (`poc_*`): sin contexto no se ve nada, la base entrega solo el tenant aunque EF no filtre, rechaza escribir en otro tenant, la clave foránea compuesta, el contexto de solo lectura, el pool de conexiones, el job que recorre tenants, la tabla mixta, el ámbito de plataforma, que el usuario de la aplicación no puede apagar la política y que la consulta del tenant busca por el índice |

## Qué cubren

| Carpeta | Qué prueban |
|---|---|
| `Services/Personas/NombresPersonaTests`, `DocumentosIdentidadTests`, `ContactoPersonaTests` | Reglas puras (sin base de datos): nombres, DNI y otros documentos, teléfonos y correos |
| `Services/Personas/PersonaValidacionServiceTests` | `IPersonaValidacionService` contra los catálogos: documento, código de empleado, cargo, edad, licencia, contacto, fechas laborales, tarifa y moneda |
| `Services/Personas/PersonaServiceTests` | `IPersonaService`: alta con verificación entre empresas (D8) y detección de parecidas, edición protegida por empresa, y fusión de fichas |
| `Services/Personas/VinculoServiceTests` | `IVinculoService`: alta de un cliente natural que reutiliza a la persona (de la empresa o de otra con verificación), reactivación, baja, consulta de vínculos, razón social al día, privacidad entre empresas y su bitácora e historial; también el cliente que luego es empleado, y que dar de baja a quien solo es cliente apaga a la persona |
| `Services/Personas/UsuarioPersonaServiceTests` | `IUsuarioPersonaService` (script 021): crear y vincular la persona de un usuario, vínculo de tipo «usuario» con su empresa o la de la plataforma, persona con dos usuarios, quitar y reabrir, verificación D8, parecidas, separación entre empresas, bitácora solo del campo `PersonaId` (nunca la contraseña) e historial |
| `Services/Auditoria/AuditoriaCambiosInterceptorTests` | La bitácora por campo: altas, modificaciones, bajas, enmascarado del documento y atomicidad |
| `Arquitectura/ArnesAislamientoTests` | Arnés de aislamiento (paso F0.6): compara el modelo de EF con `Arquitectura/ClasificacionDeTablas` (las 60 tablas de eBD_SPD y las 6 contables del modelo, cada una con su regla: tenant, mixta, infraestructura, global, plataforma o legado). Falla si aparece una tabla sin clasificar, si una tabla de tenant nueva nace sin `id_tenant` o si la lista de pendientes de `id_tenant` no se mantiene al día; esa lista solo puede bajar hasta quedar vacía en F1. `Informe_de_aislamiento` deja el estado en la salida de la prueba |
| `Paginas/LoginTests` | Inicio de sesión (paso F0.7): entra con una clave guardada con BCrypt; una guardada en texto plano se rechaza aunque coincida y no se convierte, con el mismo mensaje que una clave equivocada (prueba el `LoginModel` con `Infra/SesionFalsa`) |
| `Services/EmpresaRequeridaTests` | `AuthHelper.GetEmpresaIdRequerida` y `EmpresaRequeridaPageFilter` (con una sesión en memoria, `Infra/SesionFalsa`): qué rutas de Flota exige empresa, adónde se desvía cada sesión y que ninguna página vuelva a caer en la empresa 1 (la prueba lee los `.cs` de `Pages/`) |

## La base de prueba

`Infra/BaseDeDatosDePrueba` crea una base SQLite con **el mismo modelo de EF** que usa la aplicación y los catálogos
de `Infra/DatosBase` (tipos de documento con sus patrones, las 9 categorías de licencia, 5 cargos base y dos
empresas). Cada `Crear()` devuelve un contexto nuevo sobre la misma base, como si fuera otra petición.
`Infra/RelojFijo` fija el día para que las pruebas con fechas no dependan de cuándo se corren.

Diferencias con SQL Server que el proyecto compensa:

- SQLite no genera `rowversion`: se desactiva `token_concurrencia`.
- `persona_documentos.numero_normalizado` es una columna calculada y persistida en SQL Server; en SQLite se
  emula con una columna generada y la misma regla.

## Lo que estas pruebas NO cubren

- Índices únicos filtrados (por ejemplo, un solo cliente por vínculo), claves foráneas compuestas, el disparador de
  inmutabilidad de la bitácora y las restricciones `CHECK`: viven en la base y los cubren los scripts SQL con su
  `PRECHECK` y `POSTCHECK`.
- La concurrencia optimista (`token_concurrencia`).
- Las pantallas Razor.

## Convenciones

- Nombres de prueba en español, con la regla que se comprueba (`Crear_exige_el_rol_de_empleado`).
- Una prueba que necesita datos los crea ella misma con códigos y documentos propios.
- Las pruebas de seguridad entre empresas (que una empresa no vea ni modifique lo de otra) son obligatorias para
  cada servicio nuevo que lea o escriba datos compartidos.
