using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Profesionales
{
    /// <summary>
    /// Profesionales del centro (D2, D11): psicólogos y administradores, con sus datos para lo impreso y si ya tienen usuario del
    /// sistema (por su persona). Invitar a usar el sistema se hace en Administración › Invitaciones, con la persona ya elegida.
    /// </summary>
    [RequierePermiso("psicologia.profesionales.ver")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IProfesionalService _profesionales;

        public IndexModel(ApplicationDbContext db, IProfesionalService profesionales)
        {
            _db = db;
            _profesionales = profesionales;
        }

        public sealed class FormularioProfesional
        {
            public int? IdProfesional { get; set; }

            [StringLength(20)] public string? Dni { get; set; }
            [StringLength(50)] public string? PrimerNombre { get; set; }
            [StringLength(50)] public string? SegundoNombre { get; set; }
            [StringLength(50)] public string? PrimerApellido { get; set; }
            [StringLength(50)] public string? SegundoApellido { get; set; }
            public string? Sexo { get; set; }
            [StringLength(30)] public string? Telefono { get; set; }

            [EmailAddress(ErrorMessage = "El correo no es válido.")]
            [StringLength(150)] public string? Correo { get; set; }

            [Required(ErrorMessage = "Elige el tipo.")]
            public string? Tipo { get; set; } = TiposProfesional.Psicologo;

            [StringLength(30, ErrorMessage = "El título no puede pasar de 30 caracteres.")] public string? Titulo { get; set; }
            [StringLength(200, ErrorMessage = "El nombre para la firma no puede pasar de 200 caracteres.")] public string? NombreFirma { get; set; }
            [StringLength(30, ErrorMessage = "La colegiación no puede pasar de 30 caracteres.")] public string? NumeroColegiacion { get; set; }
            [StringLength(150, ErrorMessage = "La especialidad no puede pasar de 150 caracteres.")] public string? Especialidad { get; set; }
        }

        [BindProperty] public FormularioProfesional Input { get; set; } = new();

        /// <summary>Al editar: la persona del profesional (no se cambia desde aquí).</summary>
        public string? PersonaEditada { get; private set; }

        public sealed record Fila(int Id, int IdPersona, string Tipo, string NombreImpreso, string Persona, string? Dni, string? Colegiacion,
            string? Especialidad, bool Activo, string? Usuario, bool UsuarioActivo);

        public List<Fila> Profesionales { get; private set; } = new();
        public bool PuedeEditar => this.Puede("psicologia.profesionales.editar");
        public bool PuedeInvitar => this.Puede("administracion.invitaciones.crear");

        public async Task<IActionResult> OnGetAsync(int? editar)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            await CargarAsync();

            if (editar is { } id)
            {
                var p = await _db.ProfesionalesPsicologia.AsNoTracking().Include(x => x.Persona)
                    .FirstOrDefaultAsync(x => x.IdProfesional == id && x.IdEmpresa == (AuthHelper.GetEmpresaId(HttpContext) ?? 0));
                if (p != null)
                {
                    Input = new FormularioProfesional
                    {
                        IdProfesional = p.IdProfesional, Tipo = p.Tipo, Titulo = p.Titulo, NombreFirma = p.NombreFirma,
                        NumeroColegiacion = p.NumeroColegiacion, Especialidad = p.Especialidad
                    };
                    PersonaEditada = $"{p.Persona.Nombres} {p.Persona.Apellidos}".Trim();
                }
            }
            return Page();
        }

        [RequierePermiso("psicologia.profesionales.editar")]
        public async Task<IActionResult> OnPostGuardarAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var nuevo = Input.IdProfesional is not > 0;
            if (nuevo)
            {
                if (string.IsNullOrWhiteSpace(Input.PrimerNombre)) ModelState.AddModelError("Input.PrimerNombre", "Escribe el primer nombre.");
                if (string.IsNullOrWhiteSpace(Input.PrimerApellido)) ModelState.AddModelError("Input.PrimerApellido", "Escribe el primer apellido.");
            }
            if (!ModelState.IsValid) { await RecargarAsync(); return Page(); }

            var r = await _profesionales.GuardarAsync(new ProfesionalInput
            {
                IdEmpresa = idEmpresa, IdProfesional = Input.IdProfesional,
                Dni = Input.Dni, PrimerNombre = Input.PrimerNombre, SegundoNombre = Input.SegundoNombre, PrimerApellido = Input.PrimerApellido,
                SegundoApellido = Input.SegundoApellido, Sexo = Input.Sexo, Telefono = Input.Telefono, Correo = Input.Correo,
                Tipo = Input.Tipo, Titulo = Input.Titulo, NombreFirma = Input.NombreFirma, NumeroColegiacion = Input.NumeroColegiacion,
                Especialidad = Input.Especialidad, Usuario = AuthHelper.GetNombreUsuario(HttpContext) ?? "system"
            });
            if (!r.Ok)
            {
                foreach (var e in r.Errores) ModelState.AddModelError(string.IsNullOrEmpty(e.Campo) ? string.Empty : "Input." + e.Campo, e.Mensaje);
                await RecargarAsync();
                return Page();
            }

            TempData["ProfesionalesMensaje"] = nuevo
                ? $"Se registró a {r.Valor!.NombreImpreso} como {(r.Valor.Tipo == TiposProfesional.Psicologo ? "psicólogo(a)" : "administrador(a)")}."
                : $"Datos de {r.Valor!.NombreImpreso} actualizados.";
            return RedirectToPage();
        }

        [RequierePermiso("psicologia.profesionales.editar")]
        public async Task<IActionResult> OnPostEstadoAsync(int id, bool activo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            var r = await _profesionales.CambiarEstadoAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0, id, activo, AuthHelper.GetNombreUsuario(HttpContext) ?? "system");
            TempData["ProfesionalesMensaje"] = r.Ok
                ? (activo ? "Profesional activado." : "Profesional desactivado: ya no se ofrece como responsable ni para firmar.")
                : r.Errores[0].Mensaje;
            return RedirectToPage();
        }

        private async Task RecargarAsync()
        {
            await CargarAsync();
            if (Input.IdProfesional is > 0)
                PersonaEditada = await _db.ProfesionalesPsicologia.AsNoTracking()
                    .Where(x => x.IdProfesional == Input.IdProfesional && x.IdEmpresa == (AuthHelper.GetEmpresaId(HttpContext) ?? 0))
                    .Select(x => x.Persona.Nombres + " " + x.Persona.Apellidos).FirstOrDefaultAsync();
        }

        private async Task CargarAsync()
        {
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            var filas = await _db.ProfesionalesPsicologia.AsNoTracking()
                .Where(p => p.IdEmpresa == idEmpresa)
                .OrderByDescending(p => p.Activo).ThenBy(p => p.Tipo).ThenBy(p => p.NombreFirma)
                .Select(p => new
                {
                    p.IdProfesional, p.IdPersona, p.Tipo, p.Titulo, p.NombreFirma, p.NumeroColegiacion, p.Especialidad, p.Activo,
                    p.Persona.Nombres, p.Persona.Apellidos,
                    Dni = p.Persona.Documentos.Where(d => !d.Eliminado).Select(d => d.Numero).FirstOrDefault()
                })
                .ToListAsync();

            // Su usuario del sistema: una membresía de la cuenta con la persona del profesional y acceso a esta empresa.
            var personas = filas.Select(f => f.IdPersona).ToList();
            var usuarios = await (from m in _db.Membresias.AsNoTracking()
                                  join c in _db.Cuentas.AsNoTracking() on m.IdCuenta equals c.IdCuenta
                                  join a in _db.AccesosEmpresa.AsNoTracking() on m.IdMembresia equals a.IdMembresia
                                  where m.IdPersona != null && personas.Contains(m.IdPersona.Value) && a.IdEmpresa == idEmpresa
                                  select new { IdPersona = m.IdPersona!.Value, Nombre = c.NombreUsuario ?? c.Correo,
                                               Activo = a.Activo && m.Estado == EstadoMembresia.Activa && c.Estado == EstadoCuenta.Activa })
                                 .ToListAsync();

            Profesionales = filas.Select(f =>
            {
                var u = usuarios.Where(x => x.IdPersona == f.IdPersona).OrderByDescending(x => x.Activo).FirstOrDefault();
                return new Fila(f.IdProfesional, f.IdPersona, f.Tipo,
                    string.IsNullOrWhiteSpace(f.Titulo) ? f.NombreFirma : $"{f.Titulo} {f.NombreFirma}",
                    $"{f.Nombres} {f.Apellidos}".Trim(), DocumentosIdentidad.Enmascarar(f.Dni), f.NumeroColegiacion, f.Especialidad, f.Activo,
                    u?.Nombre, u?.Activo ?? false);
            }).ToList();
        }
    }
}
