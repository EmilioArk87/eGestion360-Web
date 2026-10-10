using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Admin.Usuarios
{
    public class CreateModel : PageModel
    {
        private const string ModoExistente = "existente";
        private const string ModoNueva = "nueva";

        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;
        private readonly IUsuarioPersonaService _usuarioPersona;

        public CreateModel(ApplicationDbContext context, IPasswordService passwordService, IUsuarioPersonaService usuarioPersona)
        {
            _context         = context;
            _passwordService = passwordService;
            _usuarioPersona  = usuarioPersona;
        }

        // ── Persona del usuario (script 021): obligatoria al crear ───────────

        /// <summary>«existente» (se elige en la búsqueda) o «nueva» (se crea con los datos mínimos).</summary>
        [BindProperty] public string ModoPersona { get; set; } = ModoExistente;

        [BindProperty] public int? IdPersonaElegida { get; set; }

        [BindProperty] public PersonaNuevaForm PersonaNueva { get; set; } = new();

        /// <summary>Nombre de la persona elegida, para volver a mostrarlo si hay que corregir otro dato.</summary>
        public string? NombrePersonaElegida { get; private set; }

        public IReadOnlyList<PersonaParecida> Parecidas { get; private set; } = Array.Empty<PersonaParecida>();

        public List<SelectListItem> TiposDocumento { get; set; } = new();

        public bool EsAdministradorGeneral { get; private set; }

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

        /// <summary>Búsqueda de personas para elegir la del usuario (la usa usuarios-persona.js).</summary>
        public async Task<IActionResult> OnGetBuscarPersonasAsync(string? texto)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext) || !AuthHelper.IsAnyAdmin(HttpContext))
                return StatusCode(StatusCodes.Status403Forbidden);

            var personas = await _usuarioPersona.BuscarPersonasAsync(PersonaDeUsuarioPagina.Quien(HttpContext), texto);
            return new JsonResult(personas.Select(p => new
            {
                id = p.IdPersona,
                nombre = p.NombreCompleto,
                documento = p.Documento,
                empresas = p.Empresas,
                usuarios = p.Usuarios
            }));
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

            var nueva = ModoPersona == ModoNueva;
            if (!nueva && IdPersonaElegida is not > 0)
            {
                ModelState.AddModelError(nameof(IdPersonaElegida), "Elige la persona que usará este usuario, o créala.");
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

            // El usuario y su persona se guardan juntos: si la persona no se puede vincular, el usuario no se crea.
            await using var transaccion = await _context.Database.BeginTransactionAsync();
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var quien = PersonaDeUsuarioPagina.Quien(HttpContext);
            var r = nueva
                ? await _usuarioPersona.CrearPersonaYVincularAsync(quien, user.Id, PersonaNueva.ADatos(), PersonaNueva.ConfirmarQueEsOtraPersona)
                : await _usuarioPersona.VincularAsync(quien, user.Id, IdPersonaElegida!.Value);

            if (!r.Ok)
            {
                await transaccion.RollbackAsync();
                _context.ChangeTracker.Clear();

                if (nueva)
                    PersonaDeUsuarioPagina.AgregarErrores(ModelState, r.Errores);
                else
                    foreach (var e in r.Errores) ModelState.AddModelError(nameof(IdPersonaElegida), e.Mensaje);
                Parecidas = r.Parecidas;
                await CargarListasAsync();
                return Page();
            }

            await transaccion.CommitAsync();

            TempData["SuccessMessage"] = $"Usuario '{user.Username}' creado correctamente y vinculado a su persona.";
            return RedirectToPage("/UserManagement");
        }

        private async Task CargarListasAsync()
        {
            var quien = PersonaDeUsuarioPagina.Quien(HttpContext);
            EsAdministradorGeneral = quien.EsAdministradorGeneral;

            TiposDocumento = await _context.CatalogoTiposDocumento.AsNoTracking()
                .Where(t => t.Activo && t.EsIdentidad)
                .OrderBy(t => t.Codigo == "DNI" ? 0 : 1).ThenBy(t => t.Nombre)
                .Select(t => new SelectListItem(t.Nombre, t.Codigo))
                .ToListAsync();

            // Si ya se había elegido una persona, se vuelve a mostrar su nombre (solo si quien opera puede verla).
            NombrePersonaElegida = null;
            if (ModoPersona == ModoExistente && IdPersonaElegida is > 0)
            {
                NombrePersonaElegida = (await _usuarioPersona.PersonaVisibleAsync(quien, IdPersonaElegida.Value))?.NombreCompleto;
                if (NombrePersonaElegida == null) IdPersonaElegida = null;
            }

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
