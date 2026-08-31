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
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;

        public CreateModel(ApplicationDbContext context, IPasswordService passwordService)
        {
            _context         = context;
            _passwordService = passwordService;
        }

        [BindProperty]
        [Required(ErrorMessage = "El nombre de usuario es requerido")]
        [StringLength(50)]
        [Display(Name = "Usuario")]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "El formato del email no es válido")]
        [StringLength(100)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "La contraseña es requerida")]
        [PasswordSeguro(nameof(Username), nameof(Email))]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Debe confirmar la contraseña")]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [BindProperty]
        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        [BindProperty]
        [Display(Name = "Solicitar cambio de contraseña al ingresar")]
        public bool RequirePasswordChange { get; set; } = false;

        [BindProperty]
        [Required(ErrorMessage = "El rol es requerido")]
        [Display(Name = "Rol del sistema")]
        public string Role { get; set; } = "empresa_user";

        [BindProperty]
        [Display(Name = "Empresa")]
        public int? EmpresaId { get; set; }

        [BindProperty]
        [Display(Name = "Rol de empresa")]
        public int? EmpresaRolId { get; set; }

        public List<SelectListItem> Empresas    { get; set; } = new();
        public List<SelectListItem> EmpresaRoles { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            // empresa_admin solo puede crear usuarios para su empresa
            if (AuthHelper.IsEmpresaAdmin(HttpContext))
                EmpresaId = AuthHelper.GetEmpresaId(HttpContext);

            await CargarListasAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            // empresa_admin solo puede crear en su empresa
            if (AuthHelper.IsEmpresaAdmin(HttpContext))
                EmpresaId = AuthHelper.GetEmpresaId(HttpContext);

            if (!ModelState.IsValid)
            {
                await CargarListasAsync();
                return Page();
            }

            if (await _context.Users.AnyAsync(u => u.Username == Username.Trim()))
            {
                ModelState.AddModelError(nameof(Username), "Ese nombre de usuario ya está en uso.");
                await CargarListasAsync();
                return Page();
            }
            if (await _context.Users.AnyAsync(u => u.Email == Email.Trim().ToLowerInvariant()))
            {
                ModelState.AddModelError(nameof(Email), "Ese email ya está registrado.");
                await CargarListasAsync();
                return Page();
            }

            var user = new User
            {
                Username              = Username.Trim(),
                Email                 = Email.Trim().ToLowerInvariant(),
                Password              = _passwordService.HashPassword(Password),
                IsActive              = IsActive,
                RequirePasswordChange = RequirePasswordChange,
                Role                  = Role.ToLowerInvariant(),
                EmpresaId             = EmpresaId,
                EmpresaRolId          = EmpresaRolId,
                CreatedAt             = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Usuario '{user.Username}' creado correctamente.";
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

            if (EmpresaId.HasValue)
            {
                EmpresaRoles = await _context.EmpresaRoles
                    .Where(r => r.IdEmpresa == EmpresaId && r.Activo)
                    .OrderBy(r => r.Nombre)
                    .Select(r => new SelectListItem(r.Nombre, r.IdRol.ToString()))
                    .ToListAsync();
                EmpresaRoles.Insert(0, new SelectListItem("— Sin rol específico —", ""));
            }
        }
    }
}
