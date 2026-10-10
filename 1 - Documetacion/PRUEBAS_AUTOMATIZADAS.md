# Pruebas automatizadas

Proyecto `eGestion360Web.Tests` (xUnit, .NET 8), en la carpeta del mismo nombre. Se excluye de la compilación
y de la publicación del sitio (`eGestion360Web.csproj`).

## Cómo correrlas

```bash
dotnet test eGestion360Web.Tests/eGestion360Web.Tests.csproj
```

No necesitan SQL Server, ni internet, ni secretos: usan SQLite en memoria. **Nunca tocan la base real.**

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

## Qué cubren

| Carpeta | Qué prueban |
|---|---|
| `Services/Personas/NombresPersonaTests`, `DocumentosIdentidadTests`, `ContactoPersonaTests` | Reglas puras (sin base de datos): nombres, DNI y otros documentos, teléfonos y correos |
| `Services/Personas/PersonaValidacionServiceTests` | `IPersonaValidacionService` contra los catálogos: documento, código de empleado, cargo, edad, licencia, contacto, fechas laborales, tarifa y moneda |
| `Services/Personas/PersonaServiceTests` | `IPersonaService`: alta con verificación entre empresas (D8) y detección de parecidas, edición protegida por empresa, y fusión de fichas |
| `Services/Personas/VinculoServiceTests` | `IVinculoService`: alta de un cliente natural que reutiliza a la persona (de la empresa o de otra con verificación), reactivación, baja, consulta de vínculos, razón social al día, privacidad entre empresas y su bitácora e historial; también el cliente que luego es empleado |
| `Services/Auditoria/AuditoriaCambiosInterceptorTests` | La bitácora por campo: altas, modificaciones, bajas, enmascarado del documento y atomicidad |
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
