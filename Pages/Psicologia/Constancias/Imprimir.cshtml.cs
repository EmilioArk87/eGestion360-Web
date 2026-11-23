using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;
using eGestion360Web.Services.Reportes;

namespace eGestion360Web.Pages.Psicologia.Constancias
{
    /// <summary>
    /// Impresión de una constancia sobre la hoja del Colegio (D14, D15): solo el texto que antes se escribía a mano, en su lugar.
    /// Imprimir deja constancia en psi.impresiones; la hoja de prueba (para calibrar las posiciones sobre papel blanco) no.
    /// Si el formato tiene su versión publicada en el motor de reportes (ADR-031, script 047), la hoja sale de ahí; si no (el 047 sin
    /// aplicar, el reporte inactivo o una plantilla que falla), sale la de siempre, escrita en esta página.
    /// </summary>
    [RequierePermiso("psicologia.documentos.imprimir")]
    public class ImprimirModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IConstanciaService _constancias;
        private readonly IMotorReportes _motor;
        private readonly ILogger<ImprimirModel> _log;

        public ImprimirModel(ApplicationDbContext db, IConstanciaService constancias, IMotorReportes motor, ILogger<ImprimirModel> log)
        {
            _db = db;
            _constancias = constancias;
            _motor = motor;
            _log = log;
        }

        public int IdConstancia { get; private set; }
        public int IdPaciente { get; private set; }
        public string Numero { get; private set; } = string.Empty;
        public string Paciente { get; private set; } = string.Empty;
        public PlantillaConstancia? Plantilla { get; private set; }
        public TextoDeConstancia? Texto { get; private set; }

        /// <summary>La hoja generada por el motor de reportes (estilos y contenido); nula si se usa la de esta página.</summary>
        public string? HojaDelMotor { get; private set; }

        /// <summary>Número de la versión publicada del motor que se usó; nulo si se usa la de esta página.</summary>
        public int? VersionDelMotor { get; private set; }

        /// <summary>Por qué no se puede imprimir (anulada, sin formato); nulo si se puede.</summary>
        public string? Impedimento { get; private set; }

        /// <summary>Hoja de prueba: imprime también las etiquetas de la hoja del Colegio, para calibrar sobre papel blanco.</summary>
        [BindProperty(SupportsGet = true)] public bool Guia { get; set; }

        /// <summary>Llega en verdadero tras registrar la impresión: la página abre el cuadro de impresión sola.</summary>
        [BindProperty(SupportsGet = true)] public bool Imprimir { get; set; }

        public int Impresiones { get; private set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            return await CargarAsync(id) ? Page() : NotFound();
        }

        [RequierePermiso("psicologia.documentos.imprimir")]
        public async Task<IActionResult> OnPostRegistrarAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            var r = await _constancias.RegistrarImpresionAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0, id, AuthHelper.GetNombreUsuario(HttpContext) ?? "system",
                AuthHelper.GetCuentaId(HttpContext));
            if (!r.Ok)
            {
                TempData["ImprimirError"] = r.Errores[0].Mensaje;
                return RedirectToPage(new { id });
            }
            return RedirectToPage(new { id, imprimir = true });
        }

        private async Task<bool> CargarAsync(int id)
        {
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var c = await _db.Constancias.AsNoTracking()
                .Where(x => x.IdConstancia == id && x.IdEmpresa == idEmpresa)
                .Select(x => new
                {
                    x.IdConstancia, x.IdPaciente, x.NumeroConstancia, x.FechaEmision, x.Estado, x.Resultado, x.Tipo.Plantilla,
                    x.Paciente.Vinculo.Persona.Nombres, x.Paciente.Vinculo.Persona.Apellidos, x.Paciente.Vinculo.Persona.Sexo,
                    // La hoja dice "con DNI": solo un documento de tipo DNI (el principal primero), nunca un pasaporte u otro.
                    Dni = x.Paciente.Vinculo.Persona.Documentos
                        .Where(d => !d.Eliminado && d.TipoDocumento == DocumentosIdentidad.Dni)
                        .OrderByDescending(d => d.EsPrincipal).ThenBy(d => d.IdPersonaDocumento)
                        .Select(d => d.Numero).FirstOrDefault(),
                    Pruebas = x.Pruebas.OrderBy(p => p.Prueba.Nombre).Select(p => new { p.Prueba.Nombre, p.Prueba.Codigo }).ToList(),
                    Impresiones = _db.ImpresionesPsicologia.Count(i => i.IdConstancia == x.IdConstancia)
                })
                .FirstOrDefaultAsync();
            if (c == null) return false;

            IdConstancia = c.IdConstancia;
            IdPaciente = c.IdPaciente;
            Numero = c.NumeroConstancia;
            Paciente = $"{c.Nombres} {c.Apellidos}".Trim();
            Impresiones = c.Impresiones;
            Plantilla = PlantillasConstancia.Buscar(c.Plantilla);

            if (c.Estado != EstadosConstancia.Emitida) Impedimento = $"La constancia {c.NumeroConstancia} está anulada: no se imprime.";
            else if (Plantilla == null) Impedimento = "Este tipo de constancia todavía no tiene formato de impresión.";

            if (Plantilla?.Codigo == PlantillasConstancia.LicenciaConducir)
            {
                var dni = c.Dni == null ? null : DocumentosIdentidad.FormatearDni(DocumentosIdentidad.NormalizarNumero(c.Dni));
                Texto = RedaccionDeConstancias.LicenciaConducir(c.FechaEmision, Paciente, c.Sexo, dni, c.Resultado,
                    c.Pruebas.Select(p => (p.Nombre, p.Codigo)));

                if (Impedimento == null && Plantilla.CodigoReporte != null)
                    await GenerarConElMotorAsync(Plantilla.CodigoReporte,
                        ConstanciaPsicologiaDataSource.Modelo(c.FechaEmision, Paciente, c.Sexo, dni, c.Resultado, c.Pruebas.Select(p => (p.Nombre, p.Codigo)), Guia));
            }
            return true;
        }

        /// <summary>La hoja desde el motor de reportes; si no hay versión o la plantilla falla, se queda la de esta página.</summary>
        private async Task GenerarConElMotorAsync(string codigoReporte, IReadOnlyDictionary<string, object?> modelo)
        {
            var version = await _motor.ResolverAsync(codigoReporte, HttpContext.RequestAborted);
            if (version == null) return;
            try
            {
                HojaDelMotor = _motor.Renderizar(version, modelo);
                VersionDelMotor = version.NumeroVersion;
            }
            catch (PlantillaDeReporteInvalidaException ex)
            {
                _log.LogError(ex, "La versión {NumeroVersion} del reporte {Codigo} falló al generarse; se imprime el formato de la página.",
                    version.NumeroVersion, codigoReporte);
            }
        }
    }
}
