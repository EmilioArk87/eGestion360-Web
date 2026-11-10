using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using System.ComponentModel.DataAnnotations;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    [PaginaSinPermiso("Cada persona cambia su propia clave")]
    public class ChangePasswordModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ISincronizadorDeCuentas _cuentas;
        private readonly IAutenticador _autenticador;

        public ChangePasswordModel(ApplicationDbContext context, ISincronizadorDeCuentas cuentas, IAutenticador autenticador)
        {
            _context = context;
            _cuentas = cuentas;
            _autenticador = autenticador;
        }

        /// <summary>El usuario de <c>dbo.Users</c> de quien tiene la cookie (aunque deba cambiar su clave antes de seguir).</summary>
        private int? UsuarioActual() => AuthHelper.HayCuenta(HttpContext) ? AuthHelper.GetUserLegadoId(HttpContext) : null;

        [BindProperty]
        [Required(ErrorMessage = "La contraseña actual es requerida")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña actual")]
        public string CurrentPassword { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "La nueva contraseña es requerida")]
        [PasswordSeguro]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva contraseña")]
        public string NewPassword { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Debes confirmar la nueva contraseña")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar nueva contraseña")]
        [Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas no coinciden")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public bool IsForcedChange { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (UsuarioActual() is not { } userId)
            {
                return RedirectToPage("/Login");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
            if (user == null)
            {
                await _autenticador.SalirAsync(HttpContext);
                return RedirectToPage("/Login");
            }

            IsForcedChange = user.RequirePasswordChange;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (UsuarioActual() is not { } userId)
            {
                return RedirectToPage("/Login");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
            if (user == null)
            {
                await _autenticador.SalirAsync(HttpContext);
                return RedirectToPage("/Login");
            }

            IsForcedChange = user.RequirePasswordChange;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            if (!BCrypt.Net.BCrypt.Verify(CurrentPassword, user.Password))
            {
                ModelState.AddModelError(nameof(CurrentPassword), "La contraseña actual es incorrecta.");
                return Page();
            }

            if (BCrypt.Net.BCrypt.Verify(NewPassword, user.Password))
            {
                ModelState.AddModelError(nameof(NewPassword), "La nueva contraseña debe ser distinta a la actual.");
                return Page();
            }

            // El atributo [PasswordSeguro] no alcanza a mirar el usuario ni el email
            // porque este modelo no los expone: se comprueban acá, ya con el usuario cargado.
            var errorPolitica = PasswordPolicy.Validar(NewPassword, user.Username, user.Email);
            if (errorPolitica != null)
            {
                ModelState.AddModelError(nameof(NewPassword), errorPolitica);
                return Page();
            }

            user.Password = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            var wasForced = user.RequirePasswordChange;
            user.RequirePasswordChange = false;
            await _context.SaveChangesAsync();
            await _cuentas.SincronizarClaveAsync(user.Id);

            if (wasForced)
            {
                await _autenticador.SalirAsync(HttpContext);
                TempData["SuccessMessage"] = "Contraseña actualizada correctamente. Inicia sesión con tu nueva contraseña.";
                return RedirectToPage("/Login");
            }

            TempData["UserManagementMessage"] = "Contraseña actualizada correctamente.";
            return RedirectToPage("/UserManagement");
        }
    }
}
