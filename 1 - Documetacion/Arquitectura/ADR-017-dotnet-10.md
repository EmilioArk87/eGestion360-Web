# ADR-017 · Actualizar a .NET 10 LTS y EF Core 10 antes del 10 de noviembre de 2026

- **Estado:** Aceptada
- **Fecha:** 2026-10-10
- **Decidió:** Emilio Garay (decisión D17 del plan)
- **Fase:** paso F0.10, antes del 10 de noviembre de 2026; no depende de la migración de hosting

## Contexto

- El sitio y las pruebas apuntan a `net8.0` y usan EF Core 9.0.9.
- Microsoft confirmó que .NET 8 y .NET 9 dejan de recibir parches de seguridad el **10 de noviembre de 2026**. La app
  seguirá corriendo, pero las vulnerabilidades nuevas quedarán abiertas.
- .NET 10 es la versión LTS vigente, con soporte hasta noviembre de 2028. EF Core 10 requiere .NET 10.
- La PC de desarrollo solo tiene el SDK 8.0.423, y el CI instala `8.0.x`.

Compatibilidad con Somee, verificada el 2026-10-10:

- La cuenta usa el plan pagado **Advanced** (ver ADR-016). La página de planes de Somee lista ASP.NET Core 3, 5, 6, 7,
  8, 9 y 10 para todos los planes pagados.
- Las preguntas frecuentes de Somee admiten publicar la app «Framework-Dependent» o «Self-Contained» y recomiendan
  autocontenida: el runtime viaja con la app y no depende de lo que tenga instalado el servidor.
- Sin desplegar no se puede confirmar que el runtime de .NET 10 esté instalado en nuestro servidor. El sitio corre hoy
  con el módulo `AspNetCoreModuleV2` de IIS en modo in-process.

## Decisión

- El sitio y las pruebas pasan a `net10.0`; los paquetes de EF Core (`SqlServer`, `Sqlite`, `Tools`) pasan a 10.x, y
  los demás paquetes se actualizan solo si .NET 10 lo exige.
- El CI instala `10.0.x`.
- Mientras se publique en Somee, el paquete se genera **autocontenido** para Windows x64 (`--self-contained -r win-x64`).
  En Azure App Service Linux se usa el runtime de la plataforma.
- Si Somee responde 500.3x con el modo in-process, se cambia el `web.config` del servidor a `hostingModel="outofprocess"`.
- La actualización se publica **sola**, sin otros cambios en el mismo paquete, para poder volver al paquete anterior si
  algo falla.

## Alternativas consideradas

| Alternativa | Por qué no |
|---|---|
| Quedarse en .NET 8 hasta la migración | Sin parches de seguridad desde el 10 de noviembre de 2026 y hasta el corte, que puede ser meses después. |
| Pasar a .NET 9 | También pierde soporte el 10 de noviembre de 2026. |
| Publicar en Somee dependiente del framework | Paquete más chico, pero si el servidor no tiene el runtime 10 el sitio cae con 500.31 hasta revertir. |

## Consecuencias

- Hace falta instalar el SDK de .NET 10 en la PC de desarrollo.
- Los cambios de comportamiento de EF Core 10 pueden cambiar el SQL que genera; la huella del modelo (descripción
  completa y script SQL generado) se compara antes y después.
- El paquete autocontenido es más grande porque incluye el runtime; la subida por FTP tarda más.
- Hay que actualizar lo que nombra `net8.0` o .NET 8: `.vscode/launch.json`, `ESTANDARES_ERP.md`,
  `PRUEBAS_AUTOMATIZADAS.md`, el `README.md` de la documentación y el `CLAUDE.md` del repositorio. El armado del paquete
  para Somee pasa a publicar autocontenido.

## Cómo se verifica

- `dotnet build` en Debug y Release sin advertencias nuevas y la suite completa en verde, incluidas las pruebas contra
  LocalDB.
- La huella del modelo de EF es idéntica antes y después, o cada diferencia queda explicada.
- El CI corre en verde con `10.0.x`.
- Tras publicar en Somee: `/Login` responde 200, las páginas protegidas redirigen a `/Login` y el inicio de sesión real
  funciona (lo prueba Emilio).
