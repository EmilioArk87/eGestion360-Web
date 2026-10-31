using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services.Identidad;
using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Pages
{
    /// <summary>
    /// Segundo paso del inicio de sesión: el código de la aplicación autenticadora o uno de recuperación (D22, D47). Un código
    /// equivocado cuenta como un fallo de clave: tras varios seguidos la cuenta se bloquea, así que no se puede probar uno por uno.
    /// </summary>
    public class MfaModel : PageModel
    {
        private readonly IServicioMfa _mfa;
        private readonly IBloqueoDeCuentas _bloqueo;
        private readonly TimeProvider _tiempo;

        public MfaModel(IServicioMfa mfa, IBloqueoDeCuentas bloqueo, TimeProvider? tiempo = null)
        {
            _mfa = mfa;
            _bloqueo = bloqueo;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        [BindProperty]
        [Required(ErrorMessage = "Escribe el código")]
        [Display(Name = "Código")]
        public string Codigo { get; set; } = string.Empty;

        public IActionResult OnGet() => Pendiente(out _) ?? Page();

        public async Task<IActionResult> OnPostAsync()
        {
            var desvio = Pendiente(out var entrada);
            if (desvio != null || entrada == null) return desvio!;
            if (!ModelState.IsValid) return Page();

            if (await _mfa.VerificarAsync(entrada.IdCuenta, Codigo, HttpContext.RequestAborted))
            {
                var siguiente = entrada with { MfaVerificada = true };
                siguiente.Guardar(HttpContext.Session, _tiempo.GetUtcNow().UtcDateTime);
                return RedirectToPage(siguiente.SiguientePagina);
            }

            if (await _bloqueo.RegistrarFalloAsync(entrada.IdCuenta, HttpContext.RequestAborted) != null)
            {
                EntradaEnCurso.Limpiar(HttpContext.Session);
                TempData["ErrorMessage"] = "Demasiados intentos fallidos. La cuenta quedó bloqueada unos minutos.";
                return RedirectToPage("/Login");
            }

            ModelState.AddModelError(nameof(Codigo), "El código no es válido o ya se usó. Espera al siguiente código de tu aplicación e inténtalo de nuevo.");
            return Page();
        }

        /// <summary>Solo se llega aquí con la clave ya aceptada y el segundo factor pendiente; si no, se devuelve a donde toca.</summary>
        private IActionResult? Pendiente(out EntradaEnCurso? entrada)
        {
            entrada = EntradaEnCurso.Leer(HttpContext.Session, _tiempo.GetUtcNow().UtcDateTime);
            if (entrada == null) return RedirectToPage("/Login");
            return entrada.SiguientePagina == "/Mfa" ? null : RedirectToPage(entrada.SiguientePagina);
        }
    }
}
