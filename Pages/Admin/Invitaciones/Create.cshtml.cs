using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Pages.Admin.Usuarios;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Admin.Invitaciones
{
    /// <summary>
    /// Invitar a una persona por correo (ADR-028): se elige quién es (una persona ya registrada en la empresa), su correo, la empresa y
    /// el rol. La persona recibe un enlace y elige su contraseña; ningún administrador la conoce.
    /// </summary>
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IServicioDeInvitaciones _invitaciones;
        private readonly IUsuarioPersonaService _personas;

        public CreateModel(ApplicationDbContext context, IServicioDeInvitaciones invitaciones, IUsuarioPersonaService personas)
        {
            _context = context;
            _invitaciones = invitaciones;
            _personas = personas;
        }

        [BindProperty]
        [Required(ErrorMessage = "El correo es requerido")]
        [EmailAddress(ErrorMessage = "El formato del correo no es válido")]
        [StringLength(100)]
        [Display(Name = "Correo de la persona")]
        public string Correo { get; set; } = string.Empty;

        [BindProperty]
        [Display(Name = "Empresa")]
        public int? IdEmpresa { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Elige el tipo de usuario")]
        [Display(Name = "Tipo de usuario")]
        public string RolLegado { get; set; } = AuthHelper.EmpresaUserRole;

        [BindProperty]
        [Display(Name = "Rol en la empresa")]
        public int? IdRol { get; set; }

        [BindProperty]
        public int? IdPersonaElegida { get; set; }

        public string? NombrePersonaElegida { get; private set; }

        public bool EsAdministradorGeneral { get; private set; }

        public List<SelectListItem> Empresas { get; private set; } = new();
        public List<SelectListItem> Roles { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext)) return RedirectToPage("/MainMenu");

            // Un administrador de empresa solo invita a su empresa.
            if (AuthHelper.IsEmpresaAdmin(HttpContext)) IdEmpresa = AuthHelper.GetEmpresaId(HttpContext);

            await CargarListasAsync();
            return Page();
        }

        /// <summary>Búsqueda de personas para elegir a quién se invita (la usa usuarios-persona.js).</summary>
        public async Task<IActionResult> OnGetBuscarPersonasAsync(string? texto)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext) || !AuthHelper.IsAnyAdmin(HttpContext))
                return StatusCode(StatusCodes.Status403Forbidden);

            var personas = await _personas.BuscarPersonasAsync(PersonaDeUsuarioPagina.Quien(HttpContext), texto);
            return new JsonResult(personas.Select(p => new
            {
                id = p.IdPersona, nombre = p.NombreCompleto, documento = p.Documento, empresas = p.Empresas, usuarios = p.Usuarios
            }));
        }

        /// <summary>Los roles de una empresa, para llenar la lista cuando el administrador general cambia de empresa.</summary>
        public async Task<IActionResult> OnGetRolesAsync(int idEmpresa)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext) || !AuthHelper.IsAnyAdmin(HttpContext))
                return StatusCode(StatusCodes.Status403Forbidden);

            if (AuthHelper.IsEmpresaAdmin(HttpContext) && idEmpresa != AuthHelper.GetEmpresaId(HttpContext))
                return new JsonResult(Array.Empty<object>());

            var roles = await _context.EmpresaRoles.AsNoTracking()
                .Where(r => r.IdEmpresa == idEmpresa && r.Activo)
                .OrderBy(r => r.Nombre)
                .Select(r => new { id = r.IdRol, nombre = r.Nombre })
                .ToListAsync();
            return new JsonResult(roles);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext)) return RedirectToPage("/MainMenu");

            if (AuthHelper.IsEmpresaAdmin(HttpContext)) IdEmpresa = AuthHelper.GetEmpresaId(HttpContext);

            if (!ModelState.IsValid) return await VolverAsync();
            if (IdEmpresa is not > 0)
            {
                ModelState.AddModelError(nameof(IdEmpresa), "Elige la empresa a la que se invita.");
                return await VolverAsync();
            }
            if (IdPersonaElegida is not > 0)
            {
                ModelState.AddModelError(nameof(IdPersonaElegida), "Elige a la persona a la que invitas.");
                return await VolverAsync();
            }

            var solicitud = new SolicitudDeInvitacion(Correo, IdEmpresa.Value, RolLegado, RolLegado == AuthHelper.EmpresaAdminRole ? null : IdRol, IdPersonaElegida.Value);
            var resultado = await _invitaciones.InvitarAsync(PersonaDeUsuarioPagina.Quien(HttpContext), solicitud, EnlaceDe, HttpContext.RequestAborted);

            if (!resultado.Ok)
            {
                foreach (var error in resultado.Errores) ModelState.AddModelError(string.Empty, error);
                return await VolverAsync();
            }

            if (resultado.CorreoEnviado)
                TempData["SuccessMessage"] = $"Invitación enviada a {Correo.Trim().ToLowerInvariant()}. Vence en unos días: puedes reenviarla desde esta lista.";
            else
                TempData["WarningMessage"] = "La invitación se creó, pero el correo no se pudo enviar. Revisa la configuración de correo y usa «Reenviar».";
            return RedirectToPage("Index");
        }

        private async Task<IActionResult> VolverAsync()
        {
            await CargarListasAsync();
            return Page();
        }

        private string EnlaceDe(string token) => Url.Page("/AceptarInvitacion", null, new { token }, Request.Scheme, Request.Host.Value)!;

        private async Task CargarListasAsync()
        {
            var quien = PersonaDeUsuarioPagina.Quien(HttpContext);
            EsAdministradorGeneral = quien.EsAdministradorGeneral;

            // Si ya se había elegido una persona, se vuelve a mostrar su nombre (solo si quien opera puede verla).
            NombrePersonaElegida = null;
            if (IdPersonaElegida is > 0)
            {
                NombrePersonaElegida = (await _personas.PersonaVisibleAsync(quien, IdPersonaElegida.Value))?.NombreCompleto;
                if (NombrePersonaElegida == null) IdPersonaElegida = null;
            }

            if (quien.EsAdministradorGeneral)
            {
                Empresas = await _context.Empresas.AsNoTracking()
                    .Where(e => !e.Eliminado && e.Activa)
                    .OrderBy(e => e.RazonSocial)
                    .Select(e => new SelectListItem(e.RazonSocial, e.IdEmpresa.ToString()))
                    .ToListAsync();
                Empresas.Insert(0, new SelectListItem("— Elige la empresa —", ""));
            }

            if (IdEmpresa is > 0)
            {
                Roles = await _context.EmpresaRoles.AsNoTracking()
                    .Where(r => r.IdEmpresa == IdEmpresa && r.Activo)
                    .OrderBy(r => r.Nombre)
                    .Select(r => new SelectListItem(r.Nombre, r.IdRol.ToString()))
                    .ToListAsync();
            }
            Roles.Insert(0, new SelectListItem("— Elige un rol —", ""));
        }
    }
}
