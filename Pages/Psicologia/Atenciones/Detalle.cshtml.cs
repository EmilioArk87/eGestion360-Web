using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Atenciones
{
    /// <summary>
    /// La evaluación de un paciente (decisión D17, contenido mínimo para el examen de licencia de conducir): las pruebas aplicadas con
    /// su resultado, la conclusión apto o no apto y las observaciones. Se completa en borrador, se cierra (ya no se cambia) o se anula
    /// con motivo. Cada acción pide su propio permiso.
    /// </summary>
    [RequierePermiso("psicologia.atenciones.ver")]
    public class DetalleModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IAtencionService _atenciones;

        public DetalleModel(ApplicationDbContext db, IAtencionService atenciones)
        {
            _db = db;
            _atenciones = atenciones;
        }

        public sealed class FilaPrueba
        {
            public int IdPrueba { get; set; }
            public bool Aplicada { get; set; }

            [StringLength(AtencionService.LargoResultado, ErrorMessage = "El resultado de una prueba no puede pasar de 200 caracteres.")]
            public string? Resultado { get; set; }
        }

        public sealed class FormularioEvaluacion
        {
            public List<FilaPrueba> Pruebas { get; set; } = new();

            public string? Conclusion { get; set; }

            [StringLength(AtencionService.LargoObservaciones, ErrorMessage = "Las observaciones no pueden pasar de 4000 caracteres.")]
            public string? Observaciones { get; set; }
        }

        [BindProperty]
        public FormularioEvaluacion Input { get; set; } = new();

        /// <param name="Activa">Sigue en el catálogo activo (una desactivada solo aparece si ya estaba aplicada).</param>
        public sealed record PruebaVista(int Id, string Codigo, string Nombre, bool Activa);

        public sealed record ConstanciaVista(int Id, string Numero, string Tipo, string Estado);

        public Atencion Atencion { get; private set; } = null!;
        public int IdPaciente { get; private set; }
        public string Paciente { get; private set; } = string.Empty;
        public string Expediente { get; private set; } = string.Empty;
        public string? Jornada { get; private set; }

        /// <summary>Pruebas que se pueden marcar: el catálogo activo más las ya aplicadas que se desactivaron después.</summary>
        public List<PruebaVista> Pruebas { get; private set; } = new();

        /// <summary>Pruebas aplicadas con su resultado guardado (para la vista de solo lectura).</summary>
        public List<(PruebaVista Prueba, string? Resultado)> Aplicadas { get; private set; } = new();

        public List<ConstanciaVista> Constancias { get; private set; } = new();

        public bool PuedeRegistrar => this.Puede("psicologia.atenciones.registrar");
        public bool PuedeCerrar => this.Puede("psicologia.atenciones.cerrar");
        public bool PuedeAnular => this.Puede("psicologia.atenciones.anular");
        public bool PuedeVerConstancias => this.Puede("psicologia.constancias.ver");
        public bool PuedeEmitir => this.Puede("psicologia.constancias.emitir");

        public bool EnBorrador => Atencion.Estado == EstadosAtencion.Borrador;
        public bool Editable => EnBorrador && PuedeRegistrar;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!await CargarAsync(id)) return NotFound();

            var resultados = Aplicadas.ToDictionary(a => a.Prueba.Id, a => a.Resultado);
            Input = new FormularioEvaluacion
            {
                Pruebas = Pruebas.Select(p => new FilaPrueba
                {
                    IdPrueba = p.Id, Aplicada = resultados.ContainsKey(p.Id), Resultado = resultados.GetValueOrDefault(p.Id)
                }).ToList(),
                Conclusion = Atencion.Conclusion,
                Observaciones = Atencion.Observaciones
            };
            return Page();
        }

        [RequierePermiso("psicologia.atenciones.registrar")]
        public async Task<IActionResult> OnPostGuardarAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!await CargarAsync(id)) return NotFound();
            if (!ModelState.IsValid) return Page();

            var r = await GuardarAsync(id);
            if (!r.Ok) return ConErrores(r.Errores);
            TempData["AtencionMensaje"] = "Evaluación guardada.";
            return RedirectToPage(new { id });
        }

        /// <summary>
        /// Cierra la evaluación. Si quien cierra también puede registrar, primero guarda lo que trae el formulario ("Guardar y cerrar").
        /// </summary>
        [RequierePermiso("psicologia.atenciones.cerrar")]
        public async Task<IActionResult> OnPostCerrarAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!await CargarAsync(id)) return NotFound();

            if (PuedeRegistrar && Request.Form.Keys.Any(k => k.StartsWith("Input.", StringComparison.Ordinal)))
            {
                if (!ModelState.IsValid) return Page();
                var guardado = await GuardarAsync(id);
                if (!guardado.Ok) return ConErrores(guardado.Errores);
            }

            var r = await _atenciones.CerrarAsync(IdEmpresa(), id, Usuario());
            if (!r.Ok)
            {
                // Lo que se guardó queda guardado: se vuelve a mostrar con lo que falta para cerrar.
                TempData["AtencionError"] = string.Join(" ", r.Errores.Select(e => e.Mensaje));
                return RedirectToPage(new { id });
            }
            TempData["AtencionMensaje"] = "Evaluación cerrada: ya no se puede cambiar.";
            return RedirectToPage(new { id });
        }

        [RequierePermiso("psicologia.atenciones.anular")]
        public async Task<IActionResult> OnPostAnularAsync(int id, string? motivo)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!await CargarAsync(id)) return NotFound();

            var r = await _atenciones.AnularAsync(IdEmpresa(), id, motivo, Usuario());
            if (r.Ok) TempData["AtencionMensaje"] = "Evaluación anulada. Queda en el expediente con su motivo.";
            else TempData["AtencionError"] = r.Errores[0].Mensaje;
            return RedirectToPage(new { id });
        }

        private Task<ResultadoPsicologia<Atencion>> GuardarAsync(int id) =>
            _atenciones.GuardarEvaluacionAsync(IdEmpresa(), id, new EvaluacionInput
            {
                Pruebas = Input.Pruebas.Where(p => p.Aplicada).Select(p => new PruebaAplicadaInput(p.IdPrueba, p.Resultado)).ToList(),
                Conclusion = Input.Conclusion,
                Observaciones = Input.Observaciones
            }, Usuario());

        /// <summary>
        /// Con el formulario a la vista, los errores van junto a él; si ya no se muestra (la atención se cerró o se anuló mientras tanto),
        /// se avisan arriba de la página.
        /// </summary>
        private IActionResult ConErrores(IReadOnlyList<Services.Personas.ErrorValidacion> errores)
        {
            if (!Editable)
            {
                TempData["AtencionError"] = string.Join(" ", errores.Select(e => e.Mensaje));
                return RedirectToPage(new { id = Atencion.IdAtencion });
            }
            foreach (var e in errores)
                ModelState.AddModelError(string.IsNullOrEmpty(e.Campo) ? string.Empty : "Input." + e.Campo, e.Mensaje);
            return Page();
        }

        private int IdEmpresa() => AuthHelper.GetEmpresaId(HttpContext) ?? 0;

        private string Usuario() => AuthHelper.GetNombreUsuario(HttpContext) ?? "system";

        private async Task<bool> CargarAsync(int id)
        {
            var idEmpresa = IdEmpresa();
            var atencion = await _db.AtencionesPsicologia.AsNoTracking()
                .Include(a => a.Jornada)
                .Include(a => a.Paciente).ThenInclude(p => p.Vinculo).ThenInclude(v => v.Persona)
                .FirstOrDefaultAsync(a => a.IdAtencion == id && a.IdEmpresa == idEmpresa);
            if (atencion == null) return false;

            Atencion = atencion;
            IdPaciente = atencion.IdPaciente;
            Paciente = $"{atencion.Paciente.Vinculo.Persona.Nombres} {atencion.Paciente.Vinculo.Persona.Apellidos}".Trim();
            Expediente = atencion.Paciente.NumeroExpediente;
            Jornada = atencion.Jornada?.Nombre;

            var aplicadas = await _db.AtencionPruebas.AsNoTracking()
                .Where(ap => ap.IdAtencion == id && ap.IdEmpresa == idEmpresa)
                .Select(ap => new { ap.Prueba.IdPrueba, ap.Prueba.Codigo, ap.Prueba.Nombre, ap.Prueba.Activo, ap.Resultado })
                .ToListAsync();
            Aplicadas = aplicadas.OrderBy(a => a.Codigo)
                .Select(a => (new PruebaVista(a.IdPrueba, a.Codigo, a.Nombre, a.Activo), a.Resultado)).ToList();

            var catalogo = await _db.PruebasPsicologicas.AsNoTracking()
                .Where(p => p.IdEmpresa == idEmpresa && p.Activo)
                .Select(p => new PruebaVista(p.IdPrueba, p.Codigo, p.Nombre, true))
                .ToListAsync();
            Pruebas = catalogo.Concat(Aplicadas.Select(a => a.Prueba).Where(a => catalogo.All(c => c.Id != a.Id)))
                .OrderBy(p => p.Codigo).ToList();

            if (PuedeVerConstancias)
                Constancias = await _db.Constancias.AsNoTracking()
                    .Where(c => c.IdAtencion == id && c.IdEmpresa == idEmpresa)
                    .OrderBy(c => c.FechaEmision).ThenBy(c => c.IdConstancia)
                    .Select(c => new ConstanciaVista(c.IdConstancia, c.NumeroConstancia, c.Tipo.Nombre, c.Estado))
                    .ToListAsync();
            return true;
        }
    }
}
