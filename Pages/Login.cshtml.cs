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
                    // Debug: Agregar información para diagnóstico
                    ViewData["Debug"] = $"Usuario encontrado: {user.Username}, Password DB longitud: {user.Password?.Length}, Password ingresado: {Password}";

                    bool isPasswordValid = false;

                    // Verificar si la contraseña está hasheada (empieza con $2a$, $2b$, o $2y$)
                    if (user.Password.StartsWith("$2"))
                    {
                        // Contraseña hasheada - usar verificación bcrypt
                        ViewData["Debug"] += $" | Hash: {user.Password}";
                        try
                        {
                            isPasswordValid = _passwordService.VerifyPassword(Password, user.Password);
                            ViewData["Debug"] += $" | BCrypt Verify Result: {isPasswordValid}";
                        }
                        catch (Exception ex)
                        {
                            ViewData["Debug"] += $" | BCrypt Error: {ex.Message}";
                        }
                    }
                    else
                    {
                        // Contraseña en texto plano - verificar directamente y luego hashear
                        if (user.Password == Password)
                        {
                            isPasswordValid = true;
                            ViewData["Debug"] += " | Método: Texto plano - COINCIDE";
                            
                            // Actualizar automáticamente la contraseña a formato hasheado
                            user.Password = _passwordService.HashPassword(Password);
                            _context.Users.Update(user);
                            await _context.SaveChangesAsync();
                            ViewData["Debug"] += " | Actualizada a BCrypt";
                        }
                        else
                        {
                            ViewData["Debug"] += $" | Método: Texto plano - NO COINCIDE ('{user.Password}' != '{Password}')";
                        }
                    }

                    if (isPasswordValid)
                    {
                        var role = AuthHelper.ResolveRole(user.Role);

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
                else
                {
                    ViewData["Debug"] = $"No se encontró usuario con username/email: {Username}";
                }
                
                // Si llega aquí, las credenciales son incorrectas
                ModelState.AddModelError("", "Usuario o contraseña incorrectos.");
            }

            return Page();
        }
    }
}
