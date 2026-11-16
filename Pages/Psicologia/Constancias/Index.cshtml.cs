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
    /// Libro de constancias de la empresa: todas las emitidas y anuladas, para controlar los correlativos (D13: uno por tipo). Muestra los
    /// números que faltan en el año dentro de cada tipo, que no debería haber porque los anulados no se borran.
    /// </summary>
    [RequierePermiso("psicologia.constancias.ver")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _tiempo;

        public IndexModel(ApplicationDbContext db, TimeProvider tiempo)
        {
            _db = db;
            _tiempo = tiempo;
        }

        [BindProperty(SupportsGet = true)] public int? Anio { get; set; }
        [BindProperty(SupportsGet = true)] public int? IdTipo { get; set; }
        [BindProperty(SupportsGet = true)] public string? Estado { get; set; }
        [BindProperty(SupportsGet = true)] public string? Buscar { get; set; }

        public sealed record Fila(int Id, string Numero, DateOnly Fecha, int IdPaciente, string Expediente, string Paciente, string Tipo,
            IReadOnlyList<string> Pruebas, string Estado, string? Motivo);

        public sealed record Opcion(int Id, string Texto);

        public List<Fila> Constancias { get; private set; } = new();
        public List<Opcion> Tipos { get; private set; } = new();
        public List<int> Anios { get; private set; } = new();
        public int Emitidas { get; private set; }
        public int Anuladas { get; private set; }

        /// <summary>Números del año que no aparecen (con todos los filtros quitados salvo el año).</summary>
        public List<string> Faltantes { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var hoy = DateOnly.FromDateTime(_tiempo.GetUtcNow().UtcDateTime.AddHours(-6));
            Anio ??= hoy.Year;

            Tipos = await _db.TiposConstancia.AsNoTracking().Where(t => t.IdEmpresa == idEmpresa)
                .OrderBy(t => t.Nombre).Select(t => new Opcion(t.IdTipoConstancia, t.Nombre)).ToListAsync();
            Anios = await _db.Constancias.AsNoTracking().Where(c => c.IdEmpresa == idEmpresa)
                .Select(c => c.FechaEmision.Year).Distinct().ToListAsync();
            if (!Anios.Contains(Anio.Value)) Anios.Add(Anio.Value);
            Anios = Anios.OrderByDescending(a => a).ToList();

            var desde = new DateOnly(Anio.Value, 1, 1);
            var hasta = new DateOnly(Anio.Value, 12, 31);
            var delAnio = _db.Constancias.AsNoTracking().Where(c => c.IdEmpresa == idEmpresa && c.FechaEmision >= desde && c.FechaEmision <= hasta);

            // Cada tipo lleva su correlativo (D13): los saltos se buscan dentro de cada uno.
            var numeros = await delAnio.Select(c => new { c.NumeroConstancia, c.Tipo.Codigo }).ToListAsync();
            foreach (var grupo in numeros.GroupBy(n => n.Codigo).OrderBy(g => g.Key))
            {
                var correlativos = grupo.Select(n => NumeradorDeConstancias.Correlativo(n.NumeroConstancia, grupo.Key, Anio.Value)).OfType<int>().ToHashSet();
                if (correlativos.Count == 0) continue;
                Faltantes.AddRange(Enumerable.Range(1, correlativos.Max()).Where(n => !correlativos.Contains(n))
                    .Select(n => NumeradorDeConstancias.Formato(grupo.Key, Anio.Value, n)));
            }

            var q = delAnio;
            if (IdTipo is { } idTipo) q = q.Where(c => c.IdTipoConstancia == idTipo);
            if (Estado is EstadosConstancia.Emitida or EstadosConstancia.Anulada) q = q.Where(c => c.Estado == Estado);
            if (!string.IsNullOrWhiteSpace(Buscar))
            {
                var texto = NombresPersona.Normalizar(Buscar);
                var numero = Buscar.Trim().ToUpperInvariant();
                q = q.Where(c => c.NumeroConstancia.Contains(numero) || c.Paciente.NumeroExpediente.Contains(numero)
                                 || (c.Paciente.Vinculo.Persona.NombreNormalizado != null && c.Paciente.Vinculo.Persona.NombreNormalizado.Contains(texto)));
            }

            var filas = await q
                .OrderByDescending(c => c.FechaEmision).ThenByDescending(c => c.IdConstancia)
                .Select(c => new
                {
                    c.IdConstancia, c.NumeroConstancia, c.FechaEmision, c.IdPaciente, c.Paciente.NumeroExpediente,
                    c.Paciente.Vinculo.Persona.Nombres, c.Paciente.Vinculo.Persona.Apellidos, Tipo = c.Tipo.Nombre, c.Estado, c.MotivoAnulacion,
                    Pruebas = c.Pruebas.OrderBy(p => p.Prueba.Codigo).Select(p => p.Prueba.Codigo).ToList()
                })
                .Take(2000)
                .ToListAsync();

            Constancias = filas.Select(c => new Fila(c.IdConstancia, c.NumeroConstancia, c.FechaEmision, c.IdPaciente, c.NumeroExpediente,
                $"{c.Nombres} {c.Apellidos}".Trim(), c.Tipo, c.Pruebas, c.Estado, c.MotivoAnulacion)).ToList();
            Emitidas = Constancias.Count(c => c.Estado == EstadosConstancia.Emitida);
            Anuladas = Constancias.Count - Emitidas;
            return Page();
        }
    }
}
