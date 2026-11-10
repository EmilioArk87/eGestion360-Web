using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    /// <summary>
    /// Elegir dónde trabajar: la plataforma (operador) o una empresa de un tenant, con el rol que se tiene allí. Se llega al terminar el
    /// inicio de sesión (si hay un solo sitio, entra directo) y también con la sesión iniciada, para cambiar de sitio. Siempre se vuelven a
    /// leer los sitios permitidos de la base y se elige de entre ellos: el tenant nunca sale de lo que mande el navegador (ADR-001).
    /// </summary>
    [PaginaSinPermiso("Elegir con qué empresa se trabaja")]
    public class SeleccionarContextoModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDirectorioDeCuentas _directorio;
        private readonly IAutenticador _autenticador;
        private readonly TimeProvider _tiempo;

        public SeleccionarContextoModel(ApplicationDbContext db, IDirectorioDeCuentas directorio, IAutenticador autenticador, TimeProvider? tiempo = null)
        {
            _db = db;
            _directorio = directorio;
            _autenticador = autenticador;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        [BindProperty]
        public string? Clave { get; set; }

        public IReadOnlyList<ContextoDeAcceso> Contextos { get; private set; } = Array.Empty<ContextoDeAcceso>();

        /// <summary>True si la persona ya tiene sesión y está cambiando de sitio.</summary>
        public bool EsCambio { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var origen = await ResolverAsync();
            if (origen.Desvio != null) return origen.Desvio;

            Contextos = origen.Contextos!;
            if (Contextos.Count == 0) return RedirectToPage("/Login");

            // Con un solo sitio, al entrar no hay nada que elegir.
            if (!EsCambio && Contextos.Count == 1) return await EntrarAsync(origen, Contextos[0]);

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var origen = await ResolverAsync();
            if (origen.Desvio != null) return origen.Desvio;

            Contextos = origen.Contextos!;
            var elegido = Contextos.FirstOrDefault(c => string.Equals(c.Clave, Clave, StringComparison.Ordinal));
            if (elegido == null)
            {
                ModelState.AddModelError(nameof(Clave), "Elige uno de los sitios de la lista.");
                return Page();
            }

            return await EntrarAsync(origen, elegido);
        }

        private sealed record Origen(IActionResult? Desvio, Cuenta? Cuenta, ContextosDeLaCuenta? Lista, string Nombre, bool MfaVerificada, ExigenciaDeMfa Exigencia)
        {
            public IReadOnlyList<ContextoDeAcceso>? Contextos => Lista?.Contextos;
        }

        private async Task<Origen> ResolverAsync()
        {
            var ct = HttpContext.RequestAborted;
            int idCuenta;
            string nombre;
            bool mfa;
            var exigencia = ExigenciaDeMfa.NoRequerida;

            if (AuthHelper.IsAuthenticated(HttpContext) && AuthHelper.GetCuentaId(HttpContext) is { } autenticada)
            {
                EsCambio = true;
                idCuenta = autenticada;
                nombre = AuthHelper.GetNombreUsuario(HttpContext) ?? string.Empty;
                mfa = User.FindFirst(ReclamosDeIdentidad.MfaVerificada)?.Value == "1";
            }
            else
            {
                var entrada = EntradaEnCurso.Leer(HttpContext.Session, _tiempo.GetUtcNow().UtcDateTime);
                if (entrada == null) return new Origen(RedirectToPage("/Login"), null, null, "", false, exigencia);
                if (entrada.SiguientePagina != "/SeleccionarContexto")
                    return new Origen(RedirectToPage(entrada.SiguientePagina), null, null, "", false, exigencia);

                idCuenta = entrada.IdCuenta;
                nombre = entrada.Nombre;
                mfa = entrada.MfaVerificada;
                exigencia = entrada.Exigencia;
            }

            var cuenta = await _db.Cuentas.FirstOrDefaultAsync(c => c.IdCuenta == idCuenta && c.Estado == EstadoCuenta.Activa, ct);
            if (cuenta == null)
            {
                EntradaEnCurso.Limpiar(HttpContext.Session);
                return new Origen(RedirectToPage("/Login"), null, null, "", false, exigencia);
            }

            return new Origen(null, cuenta, await _directorio.ContextosAsync(cuenta, ct), nombre, mfa, exigencia);
        }

        private async Task<IActionResult> EntrarAsync(Origen origen, ContextoDeAcceso contexto)
        {
            var cuenta = origen.Cuenta!;
            var datos = _autenticador.ConstruirIdentidad(cuenta, contexto, origen.Nombre, origen.MfaVerificada);
            await _autenticador.EntrarAsync(HttpContext, cuenta, datos, HttpContext.RequestAborted);
            EntradaEnCurso.Limpiar(HttpContext.Session);

            if (cuenta.DebeCambiarClave) return RedirectToPage("/ChangePassword");

            if (origen.Exigencia == ExigenciaDeMfa.Aviso)
                TempData["WarningMessage"] = "Pronto será obligatorio verificar tu identidad en dos pasos. Actívalo ahora desde «Activar verificación en dos pasos».";

            return RedirectToPage("/MainMenu");
        }
    }
}
