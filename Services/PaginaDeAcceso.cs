using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Mvc;

namespace eGestion360Web.Services
{
    /// <summary>Un párrafo de la página de acceso: texto, con una parte opcional resaltada en el medio («Tu rol no incluye: <b>…</b>.»).</summary>
    public sealed record LineaDePagina(string Texto, string? Resaltado = null, string Cierre = "");

    /// <summary>Lo que cambia de una página de acceso a otra. Todo es texto plano: <see cref="PaginaDeAcceso"/> lo codifica.</summary>
    /// <param name="Titulo">El título grande («No tienes permiso para esto»).</param>
    /// <param name="Lineas">Los párrafos bajo el título.</param>
    /// <param name="Codigo">El permiso que falta, para quien lo pida a su administrador; nulo si no aplica.</param>
    /// <param name="Referencia">Referencia de la petición, para que soporte la encuentre en el log; nula si no hay.</param>
    /// <param name="TextoDelBotonPrincipal">Texto del botón de color.</param>
    /// <param name="EnlaceDelBotonPrincipal">A dónde lleva (una ruta del propio sitio).</param>
    /// <param name="OfrecerVolverAtras">Si se ofrece también «Volver atrás» (no tiene sentido cuando la cuenta está bloqueada).</param>
    public sealed record DatosDePaginaDeAcceso(
        string Titulo,
        IReadOnlyList<LineaDePagina> Lineas,
        string? Codigo = null,
        string? Referencia = null,
        string TextoDelBotonPrincipal = "Volver al menú principal",
        string EnlaceDelBotonPrincipal = "/MainMenu",
        bool OfrecerVolverAtras = true);

    /// <summary>
    /// La página que ve quien no puede entrar: sin permiso, módulo que no tiene, cuenta suspendida o bloqueada. Es la misma familia visual que el
    /// <c>app_offline.htm</c> de los despliegues (fondo oscuro, logo de eGestión360, un mensaje y dos botones): un solo archivo con el CSS y el
    /// logo adentro, sin imágenes, fuentes ni scripts de afuera, así que se ve igual aunque falle todo lo demás. Se arma como texto y no como
    /// vista de Razor porque la usan los filtros de página y el middleware, que responden antes de que exista una vista.
    /// </summary>
    public static class PaginaDeAcceso
    {
        private static readonly HtmlEncoder Codificador = HtmlEncoder.Create(UnicodeRanges.All);

        private static string H(string texto) => Codificador.Encode(texto);

        /// <summary>La página completa como HTML.</summary>
        public static string Html(DatosDePaginaDeAcceso datos)
        {
            var lineas = new StringBuilder();
            foreach (var l in datos.Lineas)
            {
                lineas.Append("    <p>").Append(H(l.Texto));
                if (!string.IsNullOrEmpty(l.Resaltado)) lineas.Append("<strong>").Append(H(l.Resaltado)).Append("</strong>");
                lineas.Append(H(l.Cierre)).Append("</p>\n");
            }

            var acciones = new StringBuilder();
            acciones.Append("      <a class=\"btn p\" href=\"").Append(H(datos.EnlaceDelBotonPrincipal)).Append("\">").Append(H(datos.TextoDelBotonPrincipal)).Append("</a>\n");
            if (datos.OfrecerVolverAtras) acciones.Append("      <button class=\"btn\" type=\"button\" onclick=\"history.back()\">Volver atrás</button>\n");

            var pie = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(datos.Codigo)) pie.Append("Permiso <code>").Append(H(datos.Codigo)).Append("</code>");
            if (!string.IsNullOrWhiteSpace(datos.Referencia))
            {
                if (pie.Length > 0) pie.Append(" · ");
                pie.Append("Referencia <code>").Append(H(datos.Referencia)).Append("</code>");
            }

            return Plantilla
                .Replace("@@TITULO@@", H(datos.Titulo))
                .Replace("@@LINEAS@@", lineas.ToString().TrimEnd('\n'))
                .Replace("@@ACCIONES@@", acciones.ToString().TrimEnd('\n'))
                .Replace("@@PIE@@", pie.Length > 0 ? "    <p class=\"pie\">" + pie + "</p>" : string.Empty);
        }

        /// <summary>La respuesta HTTP con la página y el código de estado dados (403 para una negación).</summary>
        public static ContentResult Respuesta(int estado, DatosDePaginaDeAcceso datos)
            => new() { StatusCode = estado, ContentType = "text/html; charset=utf-8", Content = Html(datos) };

        private const string Plantilla = """
<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>@@TITULO@@ — eGestión360</title>
<style>
  *{box-sizing:border-box}
  body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px;
       background:#0f172a;color:#e2e8f0;--hueco:#0f172a;
       font-family:system-ui,-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif}
  .caja{max-width:560px;text-align:center}
  .logo{display:block;width:100%;max-width:280px;height:auto;margin:0 auto 30px}
  .candado{width:84px;height:84px;margin:0 auto 22px;border-radius:50%;display:grid;place-items:center;
           border:2px solid #F5A524;background:rgba(245,165,36,.10);color:#F5A524}
  .candado svg{width:40px;height:40px}
  h1{font-size:clamp(1.4rem,4vw,1.9rem);font-weight:600;margin:0 0 14px;color:#f8fafc}
  p{font-size:1.02rem;line-height:1.6;margin:0 0 10px;color:#94a3b8}
  p strong{color:#e2e8f0;font-weight:600}
  .acciones{display:flex;gap:12px;justify-content:center;flex-wrap:wrap;margin-top:28px}
  .btn{display:inline-block;padding:11px 20px;border-radius:10px;font:600 .95rem system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;
       text-decoration:none;border:1px solid #334155;color:#e2e8f0;background:transparent;cursor:pointer}
  .btn.p{border-color:transparent;color:#06202a;background:linear-gradient(90deg,#3D86E6,#2FD0DE)}
  .btn:hover{border-color:#475569}
  .btn:focus-visible{outline:2px solid #2FD0DE;outline-offset:2px}
  .pie{margin-top:32px;font-size:.82rem;color:#64748b}
  .pie code{font-family:ui-monospace,SFMono-Regular,Consolas,'Liberation Mono',monospace;color:#94a3b8}
</style>
</head>
<body>
  <div class="caja">
    <svg class="logo" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 520 140" role="img" aria-label="eGestión360">
      <defs><linearGradient id="egLogoA" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#3D86E6"/><stop offset="1" stop-color="#2FD0DE"/></linearGradient></defs>
      <g transform="translate(10,10)">
        <circle cx="60" cy="60" r="44" fill="none" stroke="url(#egLogoA)" stroke-width="13" stroke-dasharray="39.08 7"/>
        <circle cx="60" cy="60" r="44" fill="none" stroke="#F5A524" stroke-width="13" stroke-dasharray="39.08 237.38" stroke-dashoffset="-184.32"/>
      </g>
      <text x="150" y="88" font-family="system-ui,-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif" font-size="54" font-weight="500" letter-spacing="-1"><tspan fill="#4A90E2">e</tspan><tspan fill="#FFFFFF">Gestión</tspan><tspan fill="#2FD0DE">360</tspan></text>
    </svg>
    <div class="candado"><svg viewBox="0 0 64 64" aria-hidden="true" focusable="false"><rect x="13" y="28" width="38" height="28" rx="7" fill="currentColor"/><path d="M21 28v-7a11 11 0 0 1 22 0v7" fill="none" stroke="currentColor" stroke-width="5.5" stroke-linecap="round"/><circle cx="32" cy="41" r="4.2" fill="var(--hueco)"/><rect x="30" y="42" width="4" height="9" rx="2" fill="var(--hueco)"/></svg></div>
    <h1>@@TITULO@@</h1>
@@LINEAS@@
    <div class="acciones">
@@ACCIONES@@
    </div>
@@PIE@@
  </div>
</body>
</html>
""";
    }
}
