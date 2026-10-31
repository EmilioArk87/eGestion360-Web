using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Pages
{
    /// <summary>
    /// Activar la verificación en dos pasos: escanear el QR con una aplicación autenticadora, confirmar con un código y guardar los códigos
    /// de recuperación (se muestran una sola vez). La usa quien está entrando y se la exigen (operador, o administrador de empresa pasada
    /// su ventana de gracia) y también cualquier persona con sesión que quiera activarla antes (D47).
    /// </summary>
    public class MfaAltaModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IServicioMfa _mfa;
        private readonly TimeProvider _tiempo;

        public MfaAltaModel(ApplicationDbContext db, IServicioMfa mfa, TimeProvider? tiempo = null)
        {
            _db = db;
            _mfa = mfa;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        [BindProperty]
        [Display(Name = "Código")]
        public string Codigo { get; set; } = string.Empty;

        public EnrolamientoMfa? Enrolamiento { get; private set; }

        /// <summary>Los códigos de recuperación, solo en la respuesta que confirma el alta.</summary>
        public IReadOnlyList<string>? CodigosDeRecuperacion { get; private set; }

        /// <summary>True si la persona ya tiene sesión (activación voluntaria); false si está entrando y se la exigen.</summary>
        public bool EsVoluntaria { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var (idCuenta, desvio) = Resolver();
            if (desvio != null) return desvio;

            if (await _mfa.EstaActivaAsync(idCuenta, HttpContext.RequestAborted))
                return RedirectToPage(EsVoluntaria ? "/MainMenu" : EntradaEnCurso.Leer(HttpContext.Session, Ahora)?.SiguientePagina ?? "/Login");

            await CargarEnrolamientoAsync(idCuenta);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var (idCuenta, desvio) = Resolver();
            if (desvio != null) return desvio;

            var codigos = string.IsNullOrWhiteSpace(Codigo) ? null : await _mfa.ConfirmarEnrolamientoAsync(idCuenta, Codigo, HttpContext.RequestAborted);
            if (codigos == null)
            {
                ModelState.AddModelError(nameof(Codigo), "El código no es válido. Escribe el que muestra tu aplicación ahora mismo.");
                await CargarEnrolamientoAsync(idCuenta);
                return Page();
            }

            // Quien está entrando acaba de probar su segundo factor: no se le pide otra vez.
            if (!EsVoluntaria && EntradaEnCurso.Leer(HttpContext.Session, Ahora) is { } entrada)
                (entrada with { Exigencia = ExigenciaDeMfa.Verificar, MfaVerificada = true }).Guardar(HttpContext.Session, Ahora);

            CodigosDeRecuperacion = codigos;
            return Page();
        }

        /// <summary>Después de guardar los códigos: sigue el inicio de sesión, o se vuelve al menú.</summary>
        public IActionResult OnPostContinuar()
        {
            if (AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/MainMenu");
            var entrada = EntradaEnCurso.Leer(HttpContext.Session, Ahora);
            return RedirectToPage(entrada?.SiguientePagina ?? "/Login");
        }

        private DateTime Ahora => _tiempo.GetUtcNow().UtcDateTime;

        private (int IdCuenta, IActionResult? Desvio) Resolver()
        {
            if (AuthHelper.IsAuthenticated(HttpContext) && AuthHelper.GetCuentaId(HttpContext) is { } idAutenticada)
            {
                EsVoluntaria = true;
                return (idAutenticada, null);
            }

            var entrada = EntradaEnCurso.Leer(HttpContext.Session, Ahora);
            if (entrada == null) return (0, RedirectToPage("/Login"));
            // Solo se llega por aquí cuando se la exigen y todavía no la tiene.
            if (entrada.SiguientePagina != "/MfaAlta") return (0, RedirectToPage(entrada.SiguientePagina));
            return (entrada.IdCuenta, null);
        }

        private async Task CargarEnrolamientoAsync(int idCuenta)
        {
            var correo = await _db.Cuentas.AsNoTracking().Where(c => c.IdCuenta == idCuenta).Select(c => c.Correo).FirstAsync(HttpContext.RequestAborted);
            Enrolamiento = await _mfa.IniciarEnrolamientoAsync(idCuenta, correo, HttpContext.RequestAborted);
        }
    }
}
