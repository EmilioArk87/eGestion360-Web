using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace eGestion360Web.Pages
{
    public class LoginModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;

        public LoginModel(ApplicationDbContext context, IPasswordService passwordService)
        {
            _context = context;
            _passwordService = passwordService;
        }

        [BindProperty]
        [Required(ErrorMessage = "El nombre de usuario es requerido")]
        [Display(Name = "Usuario")]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "La contraseña es requerida")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        [BindProperty]
        [Display(Name = "Recordarme")]
        public bool RememberMe { get; set; }

        public IActionResult OnGet()
        {
            // Si el usuario ya ha iniciado sesión, redirigir al menú principal
            if (HttpContext.Session.GetString("UserId") != null)
            {
                return RedirectToPage("/MainMenu");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                // Buscar usuario solo por username/email, sin verificar password aún
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => (u.Username == Username || u.Email == Username) && u.IsActive);

                if (user != null)
                {
                    bool isPasswordValid = false;

                    if (user.Password?.StartsWith("$2") == true)
                    {
                        try
                        {
                            isPasswordValid = _passwordService.VerifyPassword(Password, user.Password);
                        }
                        catch
                        {
                            // hash inválido, isPasswordValid queda false
                        }
                    }
                    else
                    {
                        if (user.Password == Password)
                        {
                            isPasswordValid = true;

                            // Migrar contraseña en texto plano a BCrypt
                            user.Password = _passwordService.HashPassword(Password);
                            _context.Users.Update(user);
                            await _context.SaveChangesAsync();
                        }
                    }

                    if (isPasswordValid)
                    {
                        var role = AuthHelper.ResolveRole(user.Role);

                        if (user.EmpresaId.HasValue && role == AuthHelper.EmpresaUserRole)
                        {
                            var empresa = await _context.Empresas
                                .FirstOrDefaultAsync(e => e.IdEmpresa == user.EmpresaId.Value && !e.Eliminado);

                            if (empresa == null || !empresa.Activa)
                            {
                                ModelState.AddModelError("", "No es posible iniciar sesión en este momento. Código: 39246");
                                return Page();
                            }
                        }

                        HttpContext.Session.SetString("UserId",   user.Id.ToString());
                        HttpContext.Session.SetString("Username", user.Username);
                        HttpContext.Session.SetString("Email",    user.Email);
                        HttpContext.Session.SetString("Role",     role);

                        // Cargar datos de tenant si el usuario pertenece a una empresa
                        if (user.EmpresaId.HasValue)
                        {
                            var modulos = await _context.EmpresaModulos
                                .Where(em => em.IdEmpresa == user.EmpresaId && em.Activo)
                                .Include(em => em.Modulo)
                                .Where(em => em.Modulo.Activo)
                                .Select(em => em.Modulo.Codigo)
                                .ToListAsync();

                            Dictionary<string, HashSet<string>> permisos = new();

                            if (user.EmpresaRolId.HasValue)
                            {
                                var rolPermisos = await _context.EmpresaRolPermisos
                                    .Where(p => p.IdRol == user.EmpresaRolId)
                                    .Include(p => p.Modulo)
                                    .ToListAsync();

                                foreach (var p in rolPermisos)
                                {
                                    var set = new HashSet<string>();
                                    if (p.PuedeVer)      set.Add("ver");
                                    if (p.PuedeCrear)    set.Add("crear");
                                    if (p.PuedeEditar)   set.Add("editar");
                                    if (p.PuedeEliminar) set.Add("eliminar");
                                    permisos[p.Modulo.Codigo] = set;
                                }
                            }
                            else if (role == AuthHelper.EmpresaAdminRole)
                            {
                                // empresa_admin tiene todos los permisos sobre sus módulos
                                foreach (var m in modulos)
                                    permisos[m] = new HashSet<string> { "ver", "crear", "editar", "eliminar" };
                            }

                            AuthHelper.SetSesionTenant(HttpContext, user.EmpresaId.Value,
                                user.EmpresaRolId, modulos, permisos);
                        }

                        return RedirectToPage("/MainMenu");
                    }
                }
                // Si llega aquí, las credenciales son incorrectas
                ModelState.AddModelError("", "Usuario o contraseña incorrectos.");
            }

            return Page();
        }
    }
}
