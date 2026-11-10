using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Pages.Admin.Usuarios;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Admin.Invitaciones
{
    /// <summary>Las invitaciones enviadas: cuáles esperan, cuáles se aceptaron y cuáles vencieron; reenviar y anular (ADR-028).</summary>
    [RequierePermiso("administracion.invitaciones.ver")]
    public class IndexModel : PageModel
    {
        private readonly IServicioDeInvitaciones _invitaciones;

        public IndexModel(IServicioDeInvitaciones invitaciones) => _invitaciones = invitaciones;

        public IReadOnlyList<InvitacionListada> Filas { get; private set; } = Array.Empty<InvitacionListada>();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext)) return RedirectToPage("/MainMenu");

            Filas = await _invitaciones.ListarAsync(Quien(), HttpContext.RequestAborted);
            return Page();
        }

        [RequierePermiso("administracion.invitaciones.crear")]
        public async Task<IActionResult> OnPostReenviarAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext)) return RedirectToPage("/MainMenu");

            var r = await _invitaciones.ReenviarAsync(Quien(), id, EnlaceDe, HttpContext.RequestAborted);
            if (!r.Ok) TempData["ErrorMessage"] = string.Join(" ", r.Errores);
            else if (r.CorreoEnviado) TempData["SuccessMessage"] = "Invitación reenviada. El enlace anterior ya no sirve.";
            else TempData["WarningMessage"] = "Se generó un enlace nuevo, pero el correo no se pudo enviar. Revisa la configuración de correo e intenta de nuevo.";
            return RedirectToPage();
        }

        [RequierePermiso("administracion.invitaciones.anular")]
        public async Task<IActionResult> OnPostAnularAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext)) return RedirectToPage("/MainMenu");

            var anulada = await _invitaciones.AnularAsync(Quien(), id, HttpContext.RequestAborted);
            if (anulada) TempData["SuccessMessage"] = "Invitación anulada: el enlace ya no sirve.";
            else TempData["ErrorMessage"] = "No se pudo anular: no existe o ya se aceptó.";
            return RedirectToPage();
        }

        private QuienOperaUsuarios Quien() => PersonaDeUsuarioPagina.Quien(HttpContext);

        /// <summary>El enlace absoluto que va en el correo, con el sitio desde el que invita quien opera.</summary>
        private string EnlaceDe(string token) => Url.Page("/AceptarInvitacion", null, new { token }, Request.Scheme, Request.Host.Value)!;
    }
}
