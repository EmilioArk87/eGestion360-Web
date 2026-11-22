using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Preferencias
{
    /// <summary>
    /// Elegir el estilo de menú (plan secundario del menú configurable, etapa 4): hub, barra lateral o barra superior. La preferencia se guarda en
    /// <c>saas.cuentas.estilo_menu</c> (script 048), así que sigue a la persona en todos sus tenants y equipos, y viaja en la cookie para que el
    /// layout no vaya a la base en cada pantalla. Al cambiarla, la cookie se vuelve a emitir con el estilo nuevo sin cerrar la sesión.
    /// <para>
    /// <c>saas.cuentas</c> es del plano de control y no tiene RLS (ADR-003): la cuenta que se actualiza es SIEMPRE la de la cookie autenticada;
    /// el formulario no manda ningún id. El valor se valida contra la lista blanca de <see cref="EstilosDeMenu"/>.
    /// </para>
    /// </summary>
    [PaginaSinPermiso("Preferencias de la propia cuenta")]
    [ValidateAntiForgeryToken]
    public class EstiloMenuModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _tiempo;

        public EstiloMenuModel(ApplicationDbContext db, TimeProvider? tiempo = null)
        {
            _db = db;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        /// <summary>El estilo que tiene ahora la persona.</summary>
        public string EstiloActual { get; private set; } = EstilosDeMenu.Hub;

        [TempData]
        public string? Mensaje { get; set; }

        [TempData]
        public string? Error { get; set; }

        public IActionResult OnGet()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            EstiloActual = AuthHelper.GetEstiloMenu(HttpContext);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? estilo, string? returnUrl)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext) || AuthHelper.GetCuentaId(HttpContext) is not { } idCuenta)
                return RedirectToPage("/Login");

            // Solo los valores de la lista: nada de normalizar lo que llega (un valor raro es un formulario manipulado, no una preferencia).
            if (!EstilosDeMenu.EsValido(estilo))
            {
                Error = "Ese estilo de menú no existe.";
                return RedirectToPage();
            }

            // La cuenta es la de la cookie, nunca un id del formulario.
            var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.IdCuenta == idCuenta, HttpContext.RequestAborted);
            if (cuenta == null) return RedirectToPage("/Login");

            if (!string.Equals(cuenta.EstiloMenu, estilo, StringComparison.Ordinal))
            {
                cuenta.EstiloMenu = estilo!;
                cuenta.ModificadoPor = Recortar(AuthHelper.GetNombreUsuario(HttpContext) ?? cuenta.Correo, 100);
                cuenta.FechaModificacion = _tiempo.GetUtcNow().UtcDateTime;
                await _db.SaveChangesAsync(HttpContext.RequestAborted);
            }

            await RenovarCookieAsync(estilo!);

            if (EsRutaLocal(returnUrl)) return LocalRedirect(returnUrl!);
            Mensaje = $"Tu menú ahora usa el estilo «{EstilosDeMenu.Nombre(estilo!)}».";
            return RedirectToPage();
        }

        /// <summary>Vuelve a emitir la cookie con el estilo nuevo, con las mismas propiedades (persistencia, vencimiento) que tenía.</summary>
        private async Task RenovarCookieAsync(string estilo)
        {
            var datos = ReclamosDeIdentidad.Leer(User);
            if (datos == null) return;

            var principal = ReclamosDeIdentidad.Crear(datos with { EstiloMenu = estilo });
            var actual = await HttpContext.AuthenticateAsync(ReclamosDeIdentidad.Esquema);
            var propiedades = actual.Succeeded && actual.Properties != null
                ? actual.Properties
                : new AuthenticationProperties { IsPersistent = false, AllowRefresh = true };

            await HttpContext.SignInAsync(ReclamosDeIdentidad.Esquema, principal, propiedades);
            HttpContext.User = principal;
        }

        /// <summary>
        /// True si <paramref name="url"/> es una ruta de este mismo sitio (<c>/Flota/Index?x=1</c>), con la misma regla que <c>Url.IsLocalUrl</c>:
        /// empieza con una sola «/» (no «//» ni «/\»), o con «~/». Así el formulario no sirve para mandar a la persona a otro sitio.
        /// </summary>
        public static bool EsRutaLocal(string? url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (url[0] == '/')
                return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
            if (url.Length > 1 && url[0] == '~' && url[1] == '/')
                return url.Length == 2 || (url[2] != '/' && url[2] != '\\');
            return false;
        }

        private static string Recortar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];
    }
}
