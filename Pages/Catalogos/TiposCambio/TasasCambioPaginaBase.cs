using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using eGestion360Web.Services;
using eGestion360Web.Services.TasasCambio;

namespace eGestion360Web.Pages.Catalogos.TiposCambio
{
    /// <summary>Datos de la barra superior compartida (_TasasEncabezado): en qué pantalla está y si ve Configuración.</summary>
    public sealed record TasasEncabezado(string Actual, bool EsAdminSistema);

    /// <summary>
    /// Lo que comparten las tres pantallas de tasas de cambio (Tipos de cambio, Histórico y Bitácora del job): el
    /// acceso (sesión y módulo "catalogos"), el contexto de la sesión, las horas en hora de Honduras y el aviso cuando
    /// la base no responde o faltan las tablas del script 020.
    /// </summary>
    public abstract class TasasCambioPaginaBase : PageModel
    {
        protected readonly TasasCambioOptions Opciones;
        protected readonly CalendarioTasasCambio Calendario;

        protected TasasCambioPaginaBase(IOptions<TasasCambioOptions> opciones, TimeProvider reloj)
        {
            Opciones = opciones.Value;
            Calendario = new CalendarioTasasCambio(reloj, Opciones);
        }

        /// <summary>Aviso en lugar de los datos cuando la base no respondió.</summary>
        public string? AvisoBaseDatos { get; protected set; }

        /// <summary>Verdadero si el aviso es porque faltan las tablas del script 020.</summary>
        public bool ModuloNoInstalado { get; protected set; }

        public DateOnly Hoy => Calendario.Hoy;
        public IReadOnlyList<string> Monedas => Opciones.MonedasEfectivas;
        public string MonedaLocal => Opciones.MonedaLocalEfectiva;

        public int? IdEmpresaSesion => AuthHelper.GetEmpresaId(HttpContext) is int id && id > 0 ? id : null;
        public bool EsAdminSistema => AuthHelper.IsAdmin(HttpContext);

        protected string Usuario => HttpContext.Session.GetString("Username") ?? "system";
        protected CancellationToken Ct => HttpContext.RequestAborted;

        /// <summary>Igual que Clientes: sin sesión al login; sin el módulo, al menú.</summary>
        protected IActionResult? Acceso()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.HasModulo(HttpContext, IndexModel.Modulo)) return RedirectToPage("/MainMenu");
            return null;
        }

        /// <summary>Deja el aviso de la pantalla según el error de base de datos y lo escribe en el log.</summary>
        protected void AvisarErrorBaseDatos(Exception ex, ILogger log, string que)
        {
            ModuloNoInstalado = IndexModel.FaltanTablas(ex);
            AvisoBaseDatos = ModuloNoInstalado ? IndexModel.AvisoNoInstalado : IndexModel.AvisoErrorBaseDatos;
            if (ModuloNoInstalado)
                log.LogError(ex, "Tasas de cambio: faltan las tablas del script 020; la pantalla muestra el aviso.");
            else
                log.LogError(ex, "Tasas de cambio: no se pudo consultar la base de datos para {Pantalla}.", que);
        }

        /// <summary>Un valor de filtro que no existe se ignora (no se manda tal cual a la consulta).</summary>
        protected static string? Elegir(string? valor, IEnumerable<string> permitidos)
        {
            var codigo = valor?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(codigo) || !permitidos.Contains(codigo) ? null : codigo;
        }

        // ── Para la vista ─────────────────────────────────────────────────────

        /// <summary>Instante UTC de la base en hora de Honduras.</summary>
        public string HoraHonduras(DateTime? utc) =>
            utc is { } valor ? Calendario.AHonduras(valor).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "—";

        /// <summary>Fin de una ejecución en hora de Honduras: solo la hora si terminó el mismo día en que empezó.</summary>
        public string FinHonduras(DateTime inicioUtc, DateTime? finUtc)
        {
            if (finUtc is not { } fin) return "—";
            var inicio = Calendario.AHonduras(inicioUtc);
            var final = Calendario.AHonduras(fin);
            return final.ToString(final.Date == inicio.Date ? "HH:mm" : "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
