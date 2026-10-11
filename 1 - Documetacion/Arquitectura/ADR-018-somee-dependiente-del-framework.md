# ADR-018 · En Somee, publicar .NET 10 dependiente del framework

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay
- **Fase:** F0.10
- **Modifica:** [ADR-017](ADR-017-dotnet-10.md), en el punto «Mientras se publique en Somee, el paquete se genera
  autocontenido».

## Contexto

Al armar el paquete autocontenido para Somee aparecieron tres problemas que ADR-017 no había previsto:

- El `web.config` del servidor arranca la app con `processPath="dotnet" arguments=".\eGestion360Web.dll"` y es el que
  guarda las variables de entorno con los secretos (ver `CONFIGURACION_SECRETOS.md`). Un paquete autocontenido necesita
  `processPath=".\eGestion360Web.exe"`, así que publicar y revertir obligaba a modificar ese archivo cada vez.
- El script que bajaba ese `web.config` por FTP, cambiaba solo la línea de arranque y lo volvía a subir fue bloqueado por
  Bitdefender: pedir la clave del FTP, bajar un archivo con secretos y reescribirlo se parece a un robo de credenciales.
  No se intenta esquivar el antivirus.
- El paquete autocontenido tenía 621 archivos y 160 MB.

## Decisión

- En Somee, .NET 10 se publica **dependiente del framework**, igual que con .NET 8: `dotnet publish -c Release` sin
  RID, sin `web.config` en el paquete y con el mismo `Publicar-SRV.ps1`. El `web.config` del servidor no se toca.
- Somee lista ASP.NET Core 10 para el plan Advanced (ADR-017). Si el servidor no tuviera el runtime 10, la app responde
  500.31: se vuelve al paquete anterior y se evalúa el autocontenido con la línea de arranque cambiada a mano por
  Emilio, o sacar los secretos del `web.config` junto con la rotación de F0.2.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Autocontenido, con el script que cambia el `web.config` | Bitdefender lo bloquea; además, cada reversa exige restaurar la línea de arranque. |
| Autocontenido, editando a mano el `web.config` del servidor | Posible, pero agrega un paso manual sobre el archivo con secretos en cada publicación y cada reversa. Queda como plan B. |
| Mover primero los secretos a `appsettings.Production.json` | Deja el `web.config` libre para subirlo con cada paquete, pero retrasa .NET 10; se puede hacer con F0.2. |

## Consecuencias

- El paquete tiene 321 archivos y 146 MB (zip de 54 MB), contra 179 archivos y 77 MB del anterior, por dos cambios
  del SDK y de los paquetes:
  - el SDK 10 genera copias `.gz` y `.br` de los estáticos (0,5 MB); la app usa `UseStaticFiles` y no las necesita;
  - `Microsoft.Data.SqlClient` 6.1, que llega con EF Core 10, trae librerías nativas de autenticación (MSAL) para todas
    las plataformas; unos 85 MB son de Linux y macOS y Somee no los carga.
- 33 archivos del paquete anterior ya no se publican porque ahora son parte del framework. Quedan en el servidor, pero
  la app no los carga: no están en su `deps.json`.
- Riesgo aceptado: unos minutos de error 500.31 si el servidor no tuviera el runtime 10, hasta volver al paquete anterior.

## Cómo se verifica

- Después de publicar: `/Login` responde 200, `/MainMenu` redirige a `/Login` y `/css/site.css.gz` responde 200 con
  1 109 bytes. Ese archivo solo existe en el paquete .NET 10: antes de publicar responde 404.
- El inicio de sesión real lo prueba Emilio.
