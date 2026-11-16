using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Importar
{
    /// <summary>Pasos 2 y 3 de la importación: revisar las filas (corregir el reparto de nombres u omitir) y confirmar.</summary>
    [RequierePermiso("psicologia.pacientes.importar")]
    public class RevisarModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IImportacionPsicologiaService _importacion;

        public RevisarModel(ApplicationDbContext db, IImportacionPsicologiaService importacion)
        {
            _db = db;
            _importacion = importacion;
        }

        /// <summary>"pendientes" muestra solo lo que hay que revisar o tiene error; cualquier otro valor, todas las filas.</summary>
        [BindProperty(SupportsGet = true)] public string? Ver { get; set; }

        public Importacion Importacion { get; private set; } = null!;
        public string NombreJornada { get; private set; } = string.Empty;
        public IReadOnlyList<ImportacionFila> Filas { get; private set; } = Array.Empty<ImportacionFila>();
        public int Listas { get; private set; }
        public int PorRevisar { get; private set; }
        public int ConError { get; private set; }
        public int Omitidas { get; private set; }
        public bool SoloPendientes => string.Equals(Ver, "pendientes", StringComparison.OrdinalIgnoreCase);
        public int Paso => Importacion.Estado == EstadosImportacion.Borrador ? (PorRevisar + ConError == 0 ? 3 : 2) : 4;

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");
            return await CargarAsync(id, ct) ? Page() : NotFound();
        }

        public async Task<IActionResult> OnPostCorregirAsync(int id, int idFila, string? primerNombre, string? segundoNombre,
            string? primerApellido, string? segundoApellido, CancellationToken ct)
        {
            var r = await _importacion.CorregirFilaAsync(Empresa(), id, new CorreccionFila
            {
                IdImportacionFila = idFila, PrimerNombre = primerNombre, SegundoNombre = segundoNombre,
                PrimerApellido = primerApellido, SegundoApellido = segundoApellido
            }, Usuario(), ct);
            return Volver(id, r);
        }

        public async Task<IActionResult> OnPostOmitirAsync(int id, int idFila, bool omitir, CancellationToken ct)
        {
            ResultadoImportacion r;
            if (omitir)
            {
                r = await _importacion.CorregirFilaAsync(Empresa(), id, new CorreccionFila { IdImportacionFila = idFila, Omitir = true }, Usuario(), ct);
            }
            else
            {
                // Volver a incluirla con el reparto que ya tiene.
                var fila = await _db.ImportacionFilasPsicologia.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.IdImportacionFila == idFila && f.IdImportacion == id && f.IdEmpresa == Empresa(), ct);
                if (fila == null) return NotFound();
                r = await _importacion.CorregirFilaAsync(Empresa(), id, new CorreccionFila
                {
                    IdImportacionFila = idFila, PrimerNombre = fila.PrimerNombre, SegundoNombre = fila.SegundoNombre,
                    PrimerApellido = fila.PrimerApellido, SegundoApellido = fila.SegundoApellido
                }, Usuario(), ct);
            }
            return Volver(id, r);
        }

        public async Task<IActionResult> OnPostConfirmarAsync(int id, CancellationToken ct) =>
            Volver(id, await _importacion.ConfirmarAsync(Empresa(), id, Usuario(), ct), soloPendientes: false);

        public async Task<IActionResult> OnPostDescartarAsync(int id, CancellationToken ct)
        {
            var r = await _importacion.DescartarAsync(Empresa(), id, Usuario(), ct);
            if (!r.Encontrado) return NotFound();
            TempData[r.Ok ? "ImportarMensaje" : "ImportarError"] = r.Mensaje;
            return r.Ok ? RedirectToPage("Subir") : RedirectToPage(new { id });
        }

        private IActionResult Volver(int id, ResultadoImportacion r, bool soloPendientes = true)
        {
            if (!r.Encontrado) return NotFound();
            TempData[r.Ok ? "ImportarMensaje" : "ImportarError"] = r.Mensaje;
            return RedirectToPage(new { id, ver = soloPendientes && SoloPendientes ? "pendientes" : null });
        }

        private int Empresa() => AuthHelper.GetEmpresaId(HttpContext) ?? 0;
        private string Usuario() => AuthHelper.GetNombreUsuario(HttpContext) ?? "system";

        private async Task<bool> CargarAsync(int id, CancellationToken ct)
        {
            var importacion = await _importacion.ObtenerAsync(Empresa(), id, ct);
            if (importacion == null) return false;

            Importacion = importacion;
            NombreJornada = importacion.IdJornada == null ? "—" : await _db.JornadasPsicologia.AsNoTracking()
                .Where(j => j.IdJornada == importacion.IdJornada).Select(j => j.Nombre + " (" + j.FechaInicio.ToString() + ")").FirstOrDefaultAsync(ct) ?? "—";

            var filas = importacion.Filas.OrderBy(f => f.NumeroFila).ToList();
            Listas = filas.Count(f => f.Resultado is ResultadosFilaImportacion.Nueva or ResultadosFilaImportacion.Reutilizada);
            PorRevisar = filas.Count(f => f.Resultado is ResultadosFilaImportacion.Revisar or ResultadosFilaImportacion.Pendiente);
            ConError = filas.Count(f => f.Resultado == ResultadosFilaImportacion.Error);
            Omitidas = filas.Count(f => f.Resultado == ResultadosFilaImportacion.Omitida);

            // Lo que necesita atención va primero; dentro, el orden del archivo.
            Filas = (SoloPendientes
                    ? filas.Where(f => f.Resultado is ResultadosFilaImportacion.Revisar or ResultadosFilaImportacion.Error or ResultadosFilaImportacion.Pendiente)
                    : filas)
                .OrderBy(f => f.Resultado switch
                {
                    ResultadosFilaImportacion.Error => 0,
                    ResultadosFilaImportacion.Revisar or ResultadosFilaImportacion.Pendiente => 1,
                    _ => 2
                })
                .ThenBy(f => f.NumeroFila)
                .ToList();
            return true;
        }
    }
}
