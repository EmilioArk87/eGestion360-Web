using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Constancias
{
    /// <summary>
    /// Impresión de una constancia sobre la hoja del Colegio (D14, D15): solo el texto que antes se escribía a mano, en su lugar.
    /// Imprimir deja constancia en psi.impresiones; la hoja de prueba (para calibrar las posiciones sobre papel blanco) no.
    /// </summary>
    [RequierePermiso("psicologia.documentos.imprimir")]
    public class ImprimirModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IConstanciaService _constancias;

        public ImprimirModel(ApplicationDbContext db, IConstanciaService constancias)
        {
            _db = db;
            _constancias = constancias;
        }

        public int IdConstancia { get; private set; }
        public int IdPaciente { get; private set; }
        public string Numero { get; private set; } = string.Empty;
        public string Paciente { get; private set; } = string.Empty;
        public PlantillaConstancia? Plantilla { get; private set; }
        public TextoDeConstancia? Texto { get; private set; }

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
            var r = await _constancias.RegistrarImpresionAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0, id, AuthHelper.GetNombreUsuario(HttpContext) ?? "system");
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
            }
            return true;
        }
    }
}
