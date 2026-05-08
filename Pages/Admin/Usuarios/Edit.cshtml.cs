using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.Usuarios
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public EditModel(ApplicationDbContext context) => _context = context;

        [BindProperty] public int    Id                    { get; set; }
        [BindProperty] [Required][StringLength(50)][Display(Name="Usuario")]  public string Username { get; set; } = string.Empty;
        [BindProperty] [Required][EmailAddress][StringLength(100)][Display(Name="Email")] public string Email { get; set; } = string.Empty;
        [BindProperty] [Display(Name="Activo")]  public bool IsActive { get; set; }
        [BindProperty] [Display(Name="Solicitar cambio de contraseña al ingresar")] public bool RequirePasswordChange { get; set; }
        [BindProperty] [Required][Display(Name="Rol del sistema")] public string Role { get; set; } = "empresa_user";
        [BindProperty] [Display(Name="Empresa")]      public int? EmpresaId    { get; set; }
        [BindProperty] [Display(Name="Rol de empresa")] public int? EmpresaRolId { get; set; }

        public List<SelectListItem> Empresas     { get; set; } = new();
        public List<SelectListItem> EmpresaRoles { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            // empresa_admin solo puede editar usuarios de su empresa
            if (AuthHelper.IsEmpresaAdmin(HttpContext) && user.EmpresaId != AuthHelper.GetEmpresaId(HttpContext))
                return RedirectToPage("/MainMenu");

            Id                    = user.Id;
            Username              = user.Username;
            Email                 = user.Email;
            IsActive              = user.IsActive;
            RequirePasswordChange = user.RequirePasswordChange;
            Role                  = user.Role;
            EmpresaId             = user.EmpresaId;
            EmpresaRolId          = user.EmpresaRolId;

            await CargarListasAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            if (AuthHelper.IsEmpresaAdmin(HttpContext))
                EmpresaId = AuthHelper.GetEmpresaId(HttpContext);

            if (!ModelState.IsValid)
            {
                await CargarListasAsync();
                return Page();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == Id);
            if (user == null) return NotFound();

            if (await _context.Users.AnyAsync(u => u.Id != Id && u.Username == Username))
            {
                ModelState.AddModelError(nameof(Username), "Ese nombre de usuario ya está en uso.");
                await CargarListasAsync();
                return Page();
            }
            if (await _context.Users.AnyAsync(u => u.Id != Id && u.Email == Email))
            {
                ModelState.AddModelError(nameof(Email), "Ese email ya está registrado.");
                await CargarListasAsync();
                return Page();
            }

            user.Username              = Username.Trim();
            user.Email                 = Email.Trim().ToLowerInvariant();
            user.IsActive              = IsActive;
            user.RequirePasswordChange = RequirePasswordChange;
            user.Role                  = Role.ToLowerInvariant();
            user.EmpresaId             = EmpresaId;
            user.EmpresaRolId          = string.IsNullOrEmpty(EmpresaRolId?.ToString()) ? null : EmpresaRolId;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Usuario '{user.Username}' actualizado correctamente.";
            return RedirectToPage("/UserManagement");
        }

        private async Task CargarListasAsync()
        {
            if (AuthHelper.IsAdmin(HttpContext))
            {
                Empresas = await _context.Empresas
                    .Where(e => !e.Eliminado && e.Activa)
                    .OrderBy(e => e.RazonSocial)
                    .Select(e => new SelectListItem(e.RazonSocial, e.IdEmpresa.ToString()))
                    .ToListAsync();
                Empresas.Insert(0, new SelectListItem("— Sin empresa (superadmin) —", ""));
            }

            var empId = EmpresaId ?? AuthHelper.GetEmpresaId(HttpContext);
            if (empId.HasValue)
            {
                EmpresaRoles = await _context.EmpresaRoles
                    .Where(r => r.IdEmpresa == empId && r.Activo)
                    .OrderBy(r => r.Nombre)
                    .Select(r => new SelectListItem(r.Nombre, r.IdRol.ToString()))
                    .ToListAsync();
                EmpresaRoles.Insert(0, new SelectListItem("— Sin rol específico —", ""));
            }
        }
    }
}
