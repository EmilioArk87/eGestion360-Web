using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Psicologia.Pacientes
{
    [RequierePermiso("psicologia.pacientes.ver")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public string? Buscar { get; set; }
        [BindProperty(SupportsGet = true)] public int? IdJornada { get; set; }

        public string? NombreJornada { get; private set; }

        /// <summary>Un paciente en la lista. El DNI va enmascarado (solo los 4 últimos dígitos): completo, solo en el expediente.</summary>
        public sealed record FilaPaciente(int IdPaciente, string Expediente, string Nombre, string DniEnmascarado, string? Sexo, byte? Edad,
            int Atenciones, int EnBorrador, bool Activo);

        public List<FilaPaciente> Pacientes { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            var q = _db.Pacientes.AsNoTracking().Where(p => p.IdEmpresa == idEmpresa && !p.Eliminado);

            if (IdJornada is { } idJornada)
            {
                NombreJornada = await _db.JornadasPsicologia.AsNoTracking()
                    .Where(j => j.IdJornada == idJornada && j.IdEmpresa == idEmpresa).Select(j => j.Nombre).FirstOrDefaultAsync();
                q = q.Where(p => p.Atenciones.Any(a => a.IdJornada == idJornada && a.Estado != EstadosAtencion.Anulada));
            }

            if (!string.IsNullOrWhiteSpace(Buscar))
            {
                var texto = NombresPersona.Normalizar(Buscar);
                var digitos = DocumentosIdentidad.NormalizarNumero(Buscar);
                var expediente = Buscar.Trim().ToUpperInvariant();
                q = q.Where(p => p.NumeroExpediente.Contains(expediente)
                                 || (p.Vinculo.Persona.NombreNormalizado != null && p.Vinculo.Persona.NombreNormalizado.Contains(texto))
                                 || (digitos.Length >= 4 && p.Vinculo.Persona.Documentos.Any(d => !d.Eliminado && d.NumeroNormalizado != null && d.NumeroNormalizado.Contains(digitos))));
            }

            var filas = await q
                .OrderBy(p => p.NumeroExpediente)
                .Select(p => new
                {
                    p.IdPaciente, p.NumeroExpediente, p.Activo,
                    p.Vinculo.Persona.Nombres, p.Vinculo.Persona.Apellidos, p.Vinculo.Persona.Sexo,
                    Dni = p.Vinculo.Persona.Documentos.Where(d => !d.Eliminado).Select(d => d.Numero).FirstOrDefault(),
                    Edad = p.Atenciones.Where(a => a.EdadDeclarada != null).OrderByDescending(a => a.FechaAtencion).Select(a => a.EdadDeclarada).FirstOrDefault(),
                    Atenciones = p.Atenciones.Count(a => a.Estado != EstadosAtencion.Anulada),
                    EnBorrador = p.Atenciones.Count(a => a.Estado == EstadosAtencion.Borrador)
                })
                .Take(1000)
                .ToListAsync();

            Pacientes = filas.Select(f => new FilaPaciente(f.IdPaciente, f.NumeroExpediente, $"{f.Nombres} {f.Apellidos}".Trim(),
                DocumentosIdentidad.Enmascarar(f.Dni), f.Sexo, f.Edad, f.Atenciones, f.EnBorrador, f.Activo)).ToList();
            return Page();
        }
    }
}
