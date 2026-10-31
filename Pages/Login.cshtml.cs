using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Pages
{
    /// <summary>
    /// Inicio de sesión con la cuenta global (F1.5, ADR-003). Aquí solo se prueba la clave; lo que sigue —segundo factor, elegir dónde
    /// trabajar— es de <see cref="EntradaEnCurso"/>, y la cookie se emite al final (<see cref="SeleccionarContextoModel"/>).
    /// </summary>
    public class LoginModel : PageModel
    {
        private readonly IAutenticador _autenticador;
        private readonly IDirectorioDeCuentas _directorio;
        private readonly ILogger<LoginModel> _logger;
        private readonly TimeProvider _tiempo;

        public LoginModel(IAutenticador autenticador, IDirectorioDeCuentas directorio, ILogger<LoginModel> logger, TimeProvider? tiempo = null)
        {
            _autenticador = autenticador;
            _directorio = directorio;
            _logger = logger;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        public const string MensajeSinPersona =
            "Tu usuario todavía no está vinculado a una persona. Pide al administrador que lo vincule para poder entrar.";

        public const string MensajeSinAcceso =
            "Tu cuenta no tiene acceso a ninguna empresa. Pide al administrador que te dé acceso.";

        public const string MensajeIncorrectas =
            "Usuario o contraseña incorrectos. Si fallas varias veces seguidas, la cuenta se bloquea unos minutos.";

        [BindProperty]
        [Required(ErrorMessage = "El nombre de usuario es requerido")]
        [Display(Name = "Usuario")]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "La contraseña es requerida")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        public IActionResult OnGet()
        {
            // Si ya hay una cuenta con sesión iniciada, no tiene sentido pedirle la clave otra vez.
            if (AuthHelper.HayCuenta(HttpContext))
                return RedirectToPage(AuthHelper.MustChangePassword(HttpContext) ? "/ChangePassword" : "/MainMenu");

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var credenciales = await _autenticador.VerificarCredencialesAsync(Username, Password, HttpContext.RequestAborted);
            if (!credenciales.Correctas || credenciales.Cuenta is not { } cuenta)
            {
                // Una cuenta bloqueada y una clave errónea se ven igual desde fuera: no se revela cuáles cuentas existen.
                ModelState.AddModelError("", MensajeIncorrectas);
                return Page();
            }

            var contextos = await _directorio.ContextosAsync(cuenta, HttpContext.RequestAborted);
            if (contextos.Contextos.Count == 0)
            {
                // El mensaje solo aparece después de una clave correcta.
                _logger.LogWarning("Inicio de sesión rechazado: la cuenta {IdCuenta} no tiene ningún sitio donde trabajar ({SinPersona} sin persona).",
                    cuenta.IdCuenta, contextos.ExcluidosPorFaltaDePersona);
                ModelState.AddModelError("", contextos.ExcluidosPorFaltaDePersona > 0 ? MensajeSinPersona : MensajeSinAcceso);
                return Page();
            }

            var exigencia = await _autenticador.ExigenciaDeMfaAsync(cuenta, contextos, HttpContext.RequestAborted);
            var entrada = new EntradaEnCurso(cuenta.IdCuenta, credenciales.NombreEscrito, exigencia, MfaVerificada: false);
            entrada.Guardar(HttpContext.Session, _tiempo.GetUtcNow().UtcDateTime);

            return RedirectToPage(entrada.SiguientePagina);
        }
    }
}
