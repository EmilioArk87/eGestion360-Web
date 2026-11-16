using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Pacientes
{
    /// <summary>
    /// Expediente del paciente: sus datos, sus atenciones con las pruebas psicológicas aplicadas y las constancias que se le han extendido.
    /// Cada acción pide su propio permiso; la sección de constancias solo la ve quien tiene <c>psicologia.constancias.ver</c>.
    /// </summary>
    [RequierePermiso("psicologia.pacientes.ver")]
    public class DetalleModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IAtencionService _atenciones;
        private readonly IConstanciaService _constancias;

        public DetalleModel(ApplicationDbContext db, IAtencionService atenciones, IConstanciaService constancias)
        {
            _db = db;
            _atenciones = atenciones;
            _constancias = constancias;
        }

        public Paciente Paciente { get; private set; } = null!;
        public string Nombre { get; private set; } = string.Empty;
        public string? Dni { get; private set; }
        public string? Sexo { get; private set; }
        public List<Atencion> Atenciones { get; private set; } = new();

        public sealed record PruebaCatalogo(int Id, string Codigo, string Nombre);

        /// <summary>Pruebas activas del catálogo, para marcar en una atención.</summary>
        public List<PruebaCatalogo> Catalogo { get; private set; } = new();

        /// <summary>Pruebas aplicadas por atención (incluye las que se desactivaron después en el catálogo).</summary>
        public Dictionary<int, List<PruebaCatalogo>> PruebasPorAtencion { get; private set; } = new();

        /// <param name="Imprimible">Su tipo tiene formato de impresión.</param>
        public sealed record FilaConstancia(int Id, string Numero, DateOnly Fecha, string Tipo, string? Profesional, string Estado,
            string? Motivo, IReadOnlyList<string> Pruebas, DateOnly? FechaAtencion, string? Resultado, bool Imprimible);

        public List<FilaConstancia> Constancias { get; private set; } = new();

        public bool PuedeRegistrarPruebas => this.Puede("psicologia.atenciones.registrar");
        public bool PuedeVerConstancias => this.Puede("psicologia.constancias.ver");
        public bool PuedeEmitir => this.Puede("psicologia.constancias.emitir");
        public bool PuedeAnular => this.Puede("psicologia.constancias.anular");
        public bool PuedeVerCatalogos => this.Puede("psicologia.catalogos.ver");
        public bool PuedeImprimir => this.Puede("psicologia.documentos.imprimir");

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            return await CargarAsync(id) ? Page() : NotFound();
        }

        [RequierePermiso("psicologia.atenciones.registrar")]
        public async Task<IActionResult> OnPostPruebasAsync(int id, int idAtencion, int[]? pruebas)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            // La atención tiene que ser de este paciente: el servicio ya exige la empresa.
            if (!await _db.AtencionesPsicologia.AnyAsync(a => a.IdAtencion == idAtencion && a.IdPaciente == id && a.IdEmpresa == idEmpresa))
                return NotFound();

            var r = await _atenciones.GuardarPruebasAsync(idEmpresa, idAtencion, pruebas ?? Array.Empty<int>(), Usuario());
            if (r.Ok) TempData["PacienteMensaje"] = r.Valor == 0 ? "La atención quedó sin pruebas aplicadas." : $"Pruebas aplicadas guardadas ({r.Valor}).";
            else TempData["PacienteError"] = r.Errores[0].Mensaje;
            return RedirectToPage(new { id });
        }

        [RequierePermiso("psicologia.constancias.anular")]
        public async Task<IActionResult> OnPostAnularConstanciaAsync(int id, int idConstancia, string? motivo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            if (!await _db.Constancias.AnyAsync(c => c.IdConstancia == idConstancia && c.IdPaciente == id && c.IdEmpresa == idEmpresa))
                return NotFound();

            var r = await _constancias.AnularAsync(idEmpresa, idConstancia, motivo, Usuario());
            if (r.Ok) TempData["PacienteMensaje"] = $"Constancia {r.Valor!.NumeroConstancia} anulada. Su número no se vuelve a usar.";
            else TempData["PacienteError"] = r.Errores[0].Mensaje;
            return RedirectToPage(new { id });
        }

        private string Usuario() => AuthHelper.GetNombreUsuario(HttpContext) ?? "system";

        private async Task<bool> CargarAsync(int id)
        {
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var paciente = await _db.Pacientes.AsNoTracking()
                .Include(p => p.Vinculo).ThenInclude(v => v.Persona).ThenInclude(p => p.Documentos)
                .FirstOrDefaultAsync(p => p.IdPaciente == id && p.IdEmpresa == idEmpresa);
            if (paciente == null) return false;

            Paciente = paciente;
            var persona = paciente.Vinculo.Persona;
            Nombre = $"{persona.Nombres} {persona.Apellidos}".Trim();
            var documento = persona.Documentos.FirstOrDefault(d => !d.Eliminado);
            Dni = documento == null ? null : DocumentosIdentidad.FormatearDni(DocumentosIdentidad.NormalizarNumero(documento.Numero));
            Sexo = persona.Sexo switch { "M" => "Masculino", "F" => "Femenino", _ => null };

            Atenciones = await _db.AtencionesPsicologia.AsNoTracking()
                .Include(a => a.Jornada)
                .Where(a => a.IdPaciente == id && a.IdEmpresa == idEmpresa)
                .OrderByDescending(a => a.FechaAtencion).ThenByDescending(a => a.IdAtencion)
                .ToListAsync();

            var idsAtenciones = Atenciones.Select(a => a.IdAtencion).ToList();
            PruebasPorAtencion = (await _db.AtencionPruebas.AsNoTracking()
                    .Where(ap => idsAtenciones.Contains(ap.IdAtencion))
                    .Select(ap => new { ap.IdAtencion, ap.Prueba.IdPrueba, ap.Prueba.Codigo, ap.Prueba.Nombre })
                    .ToListAsync())
                .GroupBy(x => x.IdAtencion)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Codigo).Select(x => new PruebaCatalogo(x.IdPrueba, x.Codigo, x.Nombre)).ToList());

            Catalogo = await _db.PruebasPsicologicas.AsNoTracking()
                .Where(p => p.IdEmpresa == idEmpresa && p.Activo)
                .OrderBy(p => p.Codigo)
                .Select(p => new PruebaCatalogo(p.IdPrueba, p.Codigo, p.Nombre))
                .ToListAsync();

            if (PuedeVerConstancias)
            {
                var filas = await _db.Constancias.AsNoTracking()
                    .Where(c => c.IdPaciente == id && c.IdEmpresa == idEmpresa)
                    .OrderByDescending(c => c.FechaEmision).ThenByDescending(c => c.IdConstancia)
                    .Select(c => new
                    {
                        c.IdConstancia, c.NumeroConstancia, c.FechaEmision, Tipo = c.Tipo.Nombre, c.NombreProfesional, c.Estado, c.MotivoAnulacion,
                        FechaAtencion = c.Atencion != null ? c.Atencion.FechaAtencion : (DateOnly?)null,
                        Pruebas = c.Pruebas.OrderBy(p => p.Prueba.Codigo).Select(p => p.Prueba.Codigo).ToList(),
                        c.Resultado, c.Tipo.Plantilla
                    })
                    .ToListAsync();
                Constancias = filas.Select(c => new FilaConstancia(c.IdConstancia, c.NumeroConstancia, c.FechaEmision, c.Tipo, c.NombreProfesional,
                    c.Estado, c.MotivoAnulacion, c.Pruebas, c.FechaAtencion, c.Resultado, PlantillasConstancia.Buscar(c.Plantilla) != null)).ToList();
            }
            return true;
        }
    }
}
