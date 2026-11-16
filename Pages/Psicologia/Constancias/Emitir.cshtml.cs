using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Constancias
{
    /// <summary>
    /// Emitir una constancia de examen psicológico desde el expediente: tipo, evaluación de la que sale (opcional), fecha, pruebas aplicadas
    /// y quién firma. El correlativo lo asigna el servidor al guardar.
    /// </summary>
    [RequierePermiso("psicologia.constancias.emitir")]
    public class EmitirModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IConstanciaService _constancias;
        private readonly TimeProvider _tiempo;

        public EmitirModel(ApplicationDbContext db, IConstanciaService constancias, TimeProvider tiempo)
        {
            _db = db;
            _constancias = constancias;
            _tiempo = tiempo;
        }

        public sealed class FormularioConstancia
        {
            [Required(ErrorMessage = "Elige el tipo de constancia.")]
            public int? IdTipoConstancia { get; set; }

            public int? IdAtencion { get; set; }

            [Required(ErrorMessage = "Elige la fecha de emisión.")]
            public DateOnly? FechaEmision { get; set; }

            public List<int> Pruebas { get; set; } = new();

            /// <summary>El psicólogo que firma, de Profesionales.</summary>
            public int? IdProfesional { get; set; }

            /// <summary>Folio de la hoja del talonario (solo tipos con talonario); vacío = el siguiente libre.</summary>
            [Range(1, int.MaxValue, ErrorMessage = "El folio debe ser un número positivo.")]
            public int? Folio { get; set; }

            /// <summary>Apto o no apto, si el formato del tipo lo imprime.</summary>
            public string? Resultado { get; set; }

            [StringLength(200, ErrorMessage = "El nombre del profesional no puede pasar de 200 caracteres.")]
            public string? NombreProfesional { get; set; }
        }

        /// <summary>Psicólogos activos que pueden firmar (los administradores no firman constancias).</summary>
        public List<Opcion> Firmantes { get; private set; } = new();

        [BindProperty] public FormularioConstancia Input { get; set; } = new();

        public int IdPaciente { get; private set; }
        public string Nombre { get; private set; } = string.Empty;
        public string Expediente { get; private set; } = string.Empty;
        /// <summary>El próximo número de cada tipo (cada tipo lleva su propio correlativo, D13).</summary>
        public IReadOnlyDictionary<int, string> SiguientesNumeros { get; private set; } = new Dictionary<int, string>();

        public string SiguientesNumerosJson => JsonSerializer.Serialize(SiguientesNumeros.ToDictionary(n => n.Key.ToString(), n => n.Value));

        public string? NumeroDelTipoElegido => Input.IdTipoConstancia is { } id && SiguientesNumeros.TryGetValue(id, out var n) ? n : null;

        public sealed record Opcion(int Id, string Texto);
        /// <param name="IdProfesional">El psicólogo de Profesionales que atendió, si lo hay: se propone como firmante.</param>
        public sealed record OpcionAtencion(int Id, string Texto, string? Profesional, IReadOnlyList<int> Pruebas, int? IdProfesional);

        /// <param name="Talonario">Usa los folios preimpresos del talonario del Colegio.</param>
        /// <param name="PideResultado">Su formato imprime apto o no apto.</param>
        public sealed record OpcionTipo(int Id, string Texto, bool Talonario, bool PideResultado);

        public List<OpcionTipo> Tipos { get; private set; } = new();

        /// <summary>Por tipo: si usa talonario, si pide resultado y su próximo número, para que el formulario se adapte al elegirlo.</summary>
        public string TiposJson => JsonSerializer.Serialize(Tipos.ToDictionary(t => t.Id.ToString(), t => new
        {
            t.Talonario, t.PideResultado, Siguiente = SiguientesNumeros.TryGetValue(t.Id, out var n) ? n : null
        }));

        public OpcionTipo? TipoElegido => Tipos.FirstOrDefault(t => t.Id == Input.IdTipoConstancia);
        public List<OpcionAtencion> Atenciones { get; private set; } = new();
        public List<Opcion> Catalogo { get; private set; } = new();

        /// <summary>Pruebas y profesional de cada atención, para que el formulario los proponga al elegirla.</summary>
        public string AtencionesJson => JsonSerializer.Serialize(Atenciones.ToDictionary(a => a.Id.ToString(), a => new { a.Profesional, a.Pruebas, a.IdProfesional }));

        private DateOnly Hoy() => DateOnly.FromDateTime(_tiempo.GetUtcNow().UtcDateTime.AddHours(-6));

        public async Task<IActionResult> OnGetAsync(int idPaciente, int? idAtencion)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!await CargarAsync(idPaciente)) return NotFound();

            var atencion = Atenciones.FirstOrDefault(a => a.Id == idAtencion) ?? Atenciones.FirstOrDefault();
            Input = new FormularioConstancia
            {
                IdAtencion = atencion?.Id,
                FechaEmision = Hoy(),
                Pruebas = atencion?.Pruebas.Where(p => Catalogo.Any(c => c.Id == p)).ToList() ?? new(),
                IdProfesional = atencion?.IdProfesional ?? (Firmantes.Count == 1 ? Firmantes[0].Id : null),
                NombreProfesional = Firmantes.Count == 0 ? atencion?.Profesional : null,
                IdTipoConstancia = Tipos.Count == 1 ? Tipos[0].Id : null
            };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int idPaciente)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!await CargarAsync(idPaciente)) return NotFound();
            if (!ModelState.IsValid) return Page();

            var r = await _constancias.EmitirAsync(new EmitirConstanciaInput
            {
                IdEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0,
                IdPaciente = idPaciente,
                IdTipoConstancia = Input.IdTipoConstancia,
                IdAtencion = Input.IdAtencion,
                FechaEmision = Input.FechaEmision,
                IdsPruebas = Input.Pruebas,
                IdProfesional = Input.IdProfesional,
                Folio = TipoElegido?.Talonario == true ? Input.Folio : null,
                Resultado = Input.Resultado,
                NombreProfesional = Input.NombreProfesional,
                Usuario = AuthHelper.GetNombreUsuario(HttpContext) ?? "system"
            });
            if (!r.Ok)
            {
                foreach (var e in r.Errores)
                    ModelState.AddModelError(string.IsNullOrEmpty(e.Campo) ? string.Empty : "Input." + e.Campo, e.Mensaje);
                return Page();
            }

            TempData["PacienteMensaje"] = $"Constancia {r.Valor!.NumeroConstancia} emitida.";
            return RedirectToPage("/Psicologia/Pacientes/Detalle", new { id = idPaciente });
        }

        private async Task<bool> CargarAsync(int idPaciente)
        {
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var paciente = await _db.Pacientes.AsNoTracking()
                .Where(p => p.IdPaciente == idPaciente && p.IdEmpresa == idEmpresa && !p.Eliminado)
                .Select(p => new { p.IdPaciente, p.NumeroExpediente, p.Vinculo.Persona.Nombres, p.Vinculo.Persona.Apellidos })
                .FirstOrDefaultAsync();
            if (paciente == null) return false;

            IdPaciente = paciente.IdPaciente;
            Expediente = paciente.NumeroExpediente;
            Nombre = $"{paciente.Nombres} {paciente.Apellidos}".Trim();
            SiguientesNumeros = await _constancias.SiguientesNumerosAsync(idEmpresa, (Input.FechaEmision ?? Hoy()).Year);

            Tipos = (await _db.TiposConstancia.AsNoTracking()
                    .Where(t => t.IdEmpresa == idEmpresa && t.Activo)
                    .OrderBy(t => t.Nombre)
                    .Select(t => new { t.IdTipoConstancia, t.Nombre, t.FolioDesde, t.FolioHasta, t.Plantilla })
                    .ToListAsync())
                .Select(t => new OpcionTipo(t.IdTipoConstancia, t.Nombre, t.FolioDesde != null && t.FolioHasta != null,
                    PlantillasConstancia.Buscar(t.Plantilla)?.PideResultado == true))
                .ToList();
            Catalogo = await _db.PruebasPsicologicas.AsNoTracking()
                .Where(p => p.IdEmpresa == idEmpresa && p.Activo)
                .OrderBy(p => p.Codigo)
                .Select(p => new Opcion(p.IdPrueba, p.Codigo + " · " + p.Nombre))
                .ToListAsync();

            var psicologos = await _db.ProfesionalesPsicologia.AsNoTracking()
                .Where(p => p.IdEmpresa == idEmpresa && p.Activo && p.Tipo == TiposProfesional.Psicologo)
                .OrderBy(p => p.NombreFirma)
                .Select(p => new { p.IdProfesional, p.IdPersona, p.Titulo, p.NombreFirma, p.NumeroColegiacion })
                .ToListAsync();
            Firmantes = psicologos.Select(p => new Opcion(p.IdProfesional,
                (string.IsNullOrWhiteSpace(p.Titulo) ? p.NombreFirma : $"{p.Titulo} {p.NombreFirma}")
                + (p.NumeroColegiacion != null ? $" · {p.NumeroColegiacion}" : string.Empty))).ToList();

            var atenciones = await _db.AtencionesPsicologia.AsNoTracking()
                .Where(a => a.IdPaciente == idPaciente && a.IdEmpresa == idEmpresa && a.Estado != EstadosAtencion.Anulada)
                .OrderByDescending(a => a.FechaAtencion).ThenByDescending(a => a.IdAtencion)
                .Select(a => new
                {
                    a.IdAtencion, a.FechaAtencion, Jornada = a.Jornada != null ? a.Jornada.Nombre : null, a.NombreProfesional, a.IdPersonaProfesional,
                    Pruebas = a.Pruebas.Select(p => p.IdPrueba).ToList()
                })
                .ToListAsync();
            Atenciones = atenciones.Select(a => new OpcionAtencion(a.IdAtencion, $"{a.FechaAtencion:dd/MM/yyyy} · {a.Jornada ?? "Consulta"}",
                a.NombreProfesional, a.Pruebas, psicologos.FirstOrDefault(p => p.IdPersona == a.IdPersonaProfesional)?.IdProfesional)).ToList();
            return true;
        }
    }
}
