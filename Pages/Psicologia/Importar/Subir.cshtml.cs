using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Pages.Psicologia.Importar
{
    /// <summary>Paso 1 de la importación: elegir la jornada y subir el Excel. El archivo se lee en memoria y no se guarda.</summary>
    [RequierePermiso("psicologia.pacientes.importar")]
    [RequestSizeLimit(LectorExcelBrigada.TamanoMaximoBytes + 512 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = LectorExcelBrigada.TamanoMaximoBytes + 512 * 1024)]
    public class SubirModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IImportacionPsicologiaService _importacion;

        public SubirModel(ApplicationDbContext db, IImportacionPsicologiaService importacion)
        {
            _db = db;
            _importacion = importacion;
        }

        [BindProperty(SupportsGet = true)] public int? IdJornada { get; set; }
        [BindProperty] public IFormFile? Archivo { get; set; }

        public List<SelectListItem> Jornadas { get; private set; } = new();

        public sealed record LoteReciente(int IdImportacion, string Archivo, string Jornada, string Estado, int Total, DateTime Fecha);
        public List<LoteReciente> Recientes { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");
            await CargarAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            if (IdJornada == null) ModelState.AddModelError(nameof(IdJornada), "Elige la jornada a la que pertenece el listado.");
            if (Archivo == null || Archivo.Length == 0) ModelState.AddModelError(nameof(Archivo), "Elige el archivo de Excel del listado.");
            else if (Archivo.Length > LectorExcelBrigada.TamanoMaximoBytes) ModelState.AddModelError(nameof(Archivo), "El archivo pasa de 5 MB.");

            if (!ModelState.IsValid)
            {
                await CargarAsync();
                return Page();
            }

            ResultadoLecturaBrigada lectura;
            await using (var contenido = Archivo!.OpenReadStream())
                lectura = LectorExcelBrigada.Leer(contenido, Archivo.FileName);

            if (!lectura.Ok)
            {
                ModelState.AddModelError(nameof(Archivo), lectura.Error!);
                await CargarAsync();
                return Page();
            }

            var usuario = AuthHelper.GetNombreUsuario(HttpContext) ?? "system";
            var r = await _importacion.CrearBorradorAsync(idEmpresa, IdJornada!.Value, lectura.Archivo!, usuario, ct);
            if (!r.Ok)
            {
                ModelState.AddModelError(string.Empty, r.Mensaje);
                await CargarAsync();
                return Page();
            }

            TempData["ImportarMensaje"] = r.Mensaje;
            return RedirectToPage("Revisar", new { id = r.Importacion!.IdImportacion });
        }

        private async Task CargarAsync()
        {
            var idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;
            Jornadas = await _db.JornadasPsicologia.AsNoTracking()
                .Where(j => j.IdEmpresa == idEmpresa && j.Estado != EstadosJornada.Anulada)
                .OrderByDescending(j => j.FechaInicio)
                .Select(j => new SelectListItem($"{j.Nombre} ({j.FechaInicio:dd/MM/yyyy})", j.IdJornada.ToString()))
                .ToListAsync();

            Recientes = await _db.ImportacionesPsicologia.AsNoTracking()
                .Where(i => i.IdEmpresa == idEmpresa)
                .OrderByDescending(i => i.IdImportacion).Take(10)
                .Select(i => new LoteReciente(i.IdImportacion, i.NombreArchivo, i.Jornada != null ? i.Jornada.Nombre : "—", i.Estado, i.TotalFilas, i.FechaCreacion))
                .ToListAsync();
        }
    }
}
