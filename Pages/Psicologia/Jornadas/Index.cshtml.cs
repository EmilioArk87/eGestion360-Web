using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Jornadas
{
    [RequierePermiso("psicologia.jornadas.ver")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _tiempo;
        private readonly IJornadaService _jornadas;

        public IndexModel(ApplicationDbContext db, TimeProvider tiempo, IJornadaService jornadas)
        {
            _db = db;
            _tiempo = tiempo;
            _jornadas = jornadas;
        }

        /// <summary>Profesionales activos que pueden ser responsables: psicólogos y administradores (decisión D2).</summary>
        public sealed record Responsable(int IdProfesional, string Nombre, string Tipo);

        public List<Responsable> Responsables { get; private set; } = new();
        public bool PuedeEditar => this.Puede("psicologia.jornadas.editar");

        public sealed record FilaJornada(int IdJornada, string Nombre, string? Lugar, string? Institucion, DateOnly FechaInicio, DateOnly? FechaFin,
            string? Responsable, string Estado, int Atenciones, int EnBorrador);

        public List<FilaJornada> Jornadas { get; private set; } = new();

        public sealed class NuevaJornadaInput
        {
            [Required(ErrorMessage = "Escribe el nombre de la jornada.")]
            [StringLength(150, ErrorMessage = "El nombre no puede pasar de 150 caracteres.")]
            public string? Nombre { get; set; }

            [StringLength(150)] public string? Lugar { get; set; }
            [StringLength(150)] public string? Institucion { get; set; }

            [Required(ErrorMessage = "Elige la fecha de la jornada.")]
            public DateOnly? FechaInicio { get; set; }

            public DateOnly? FechaFin { get; set; }

            /// <summary>Responsable elegido de Profesionales; tiene prioridad sobre el nombre escrito.</summary>
            public int? IdProfesionalResponsable { get; set; }

            [StringLength(200)] public string? NombreResponsable { get; set; }
        }

        [BindProperty]
        public NuevaJornadaInput Nueva { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            Nueva.FechaInicio ??= DateOnly.FromDateTime(_tiempo.GetUtcNow().UtcDateTime.AddHours(-6));
            await CargarAsync();
            return Page();
        }

        [RequierePermiso("psicologia.jornadas.crear")]
        public async Task<IActionResult> OnPostCrearAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            if (Nueva.FechaFin != null && Nueva.FechaInicio != null && Nueva.FechaFin < Nueva.FechaInicio)
                ModelState.AddModelError("Nueva.FechaFin", "La fecha de fin no puede ser anterior a la de inicio.");
            if (idEmpresa <= 0)
                ModelState.AddModelError(string.Empty, "Entra a una empresa para crear jornadas.");

            Profesional? responsable = null;
            if (Nueva.IdProfesionalResponsable is { } idProfesional)
            {
                responsable = await _db.ProfesionalesPsicologia.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IdProfesional == idProfesional && p.IdEmpresa == idEmpresa && p.Activo);
                if (responsable == null) ModelState.AddModelError("Nueva.IdProfesionalResponsable", "Elige un profesional activo de la empresa.");
            }

            if (!ModelState.IsValid)
            {
                await CargarAsync();
                return Page();
            }

            var usuario = AuthHelper.GetNombreUsuario(HttpContext) ?? "system";
            _db.JornadasPsicologia.Add(new Jornada
            {
                IdEmpresa = idEmpresa,
                Nombre = NombresPersona.Limpiar(Nueva.Nombre),
                Lugar = Opcional(Nueva.Lugar),
                Institucion = Opcional(Nueva.Institucion),
                FechaInicio = Nueva.FechaInicio!.Value,
                FechaFin = Nueva.FechaFin,
                IdPersonaResponsable = responsable?.IdPersona,
                NombreResponsable = responsable?.NombreImpreso ?? Opcional(Nueva.NombreResponsable),
                Estado = EstadosJornada.Abierta,
                CreadoPor = usuario,
                FechaCreacion = _tiempo.GetUtcNow().UtcDateTime
            });
            await _db.SaveChangesAsync();

            TempData["JornadasMensaje"] = "Jornada creada. Ya puedes importar su listado de pacientes.";
            return RedirectToPage();
        }

        [RequierePermiso("psicologia.jornadas.editar")]
        public async Task<IActionResult> OnPostResponsableAsync(int idJornada, int? idProfesional)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var r = await _jornadas.AsignarResponsableAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0, idJornada, idProfesional,
                AuthHelper.GetNombreUsuario(HttpContext) ?? "system");
            TempData["JornadasMensaje"] = !r.Ok
                ? r.Errores[0].Mensaje
                : (idProfesional == null ? "Se quitó el responsable de la jornada." : "Responsable asignado.")
                  + (r.Valor > 0 ? $" {r.Valor} atención(es) en borrador de la jornada quedaron con el mismo profesional." : string.Empty);
            return RedirectToPage();
        }

        private async Task CargarAsync()
        {
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            Responsables = (await _db.ProfesionalesPsicologia.AsNoTracking()
                    .Where(p => p.IdEmpresa == idEmpresa && p.Activo)
                    .OrderBy(p => p.Tipo).ThenBy(p => p.NombreFirma)
                    .Select(p => new { p.IdProfesional, p.Titulo, p.NombreFirma, p.Tipo })
                    .ToListAsync())
                .Select(p => new Responsable(p.IdProfesional, string.IsNullOrWhiteSpace(p.Titulo) ? p.NombreFirma : $"{p.Titulo} {p.NombreFirma}", p.Tipo))
                .ToList();
            Jornadas = await _db.JornadasPsicologia.AsNoTracking()
                .Where(j => j.IdEmpresa == idEmpresa)
                .OrderByDescending(j => j.FechaInicio).ThenByDescending(j => j.IdJornada)
                .Select(j => new FilaJornada(j.IdJornada, j.Nombre, j.Lugar, j.Institucion, j.FechaInicio, j.FechaFin, j.NombreResponsable, j.Estado,
                    _db.AtencionesPsicologia.Count(a => a.IdJornada == j.IdJornada && a.Estado != EstadosAtencion.Anulada),
                    _db.AtencionesPsicologia.Count(a => a.IdJornada == j.IdJornada && a.Estado == EstadosAtencion.Borrador)))
                .ToListAsync();
        }

        private static string? Opcional(string? valor)
        {
            var limpio = NombresPersona.Limpiar(valor);
            return limpio.Length == 0 ? null : limpio;
        }
    }
}
