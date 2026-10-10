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
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IUsuarioPersonaService _usuarioPersona;

        public EditModel(ApplicationDbContext context, IUsuarioPersonaService usuarioPersona)
        {
            _context = context;
            _usuarioPersona = usuarioPersona;
        }

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

        // ── Persona del usuario (script 021) ─────────────────────────────────

        /// <summary>La persona vinculada; nula si el usuario todavía no tiene.</summary>
        public PersonaParaUsuario? PersonaActual { get; private set; }

        /// <summary>Texto de búsqueda de personas (por nombre o documento).</summary>
        [BindProperty(SupportsGet = true)] public string? BuscarPersona { get; set; }

        public IReadOnlyList<PersonaParaUsuario> Resultados { get; private set; } = Array.Empty<PersonaParaUsuario>();

        [BindProperty] public PersonaNuevaForm PersonaNueva { get; set; } = new();

        /// <summary>Personas parecidas a la que se quiere crear: hay que confirmar que es otra.</summary>
        public IReadOnlyList<PersonaParecida> Parecidas { get; private set; } = Array.Empty<PersonaParecida>();

        /// <summary>Si se abre la sección para crear la persona (por ejemplo, porque hubo errores al crearla).</summary>
        public bool MostrarCrearPersona { get; private set; }

        public List<SelectListItem> TiposDocumento { get; set; } = new();

        public bool EsAdministradorGeneral { get; private set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            if (!await CargarUsuarioAsync(id)) return NotFound();
            if (AuthHelper.IsEmpresaAdmin(HttpContext) && EmpresaId != AuthHelper.GetEmpresaId(HttpContext))
                return RedirectToPage("/MainMenu");

            await CargarListasAsync();
            await CargarPersonaAsync(buscar: true);
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
                await CargarPersonaAsync(buscar: false);
                return Page();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == Id);
            if (user == null) return NotFound();

            // empresa_admin solo puede editar usuarios de su empresa
            if (AuthHelper.IsEmpresaAdmin(HttpContext) && user.EmpresaId != AuthHelper.GetEmpresaId(HttpContext))
                return RedirectToPage("/MainMenu");

            if (await _context.Users.AnyAsync(u => u.Id != Id && u.Username == Username))
            {
                ModelState.AddModelError(nameof(Username), "Ese nombre de usuario ya está en uso.");
                await CargarListasAsync();
                await CargarPersonaAsync(buscar: false);
                return Page();
            }
            if (await _context.Users.AnyAsync(u => u.Id != Id && u.Email == Email))
            {
                ModelState.AddModelError(nameof(Email), "Ese email ya está registrado.");
                await CargarListasAsync();
                await CargarPersonaAsync(buscar: false);
                return Page();
            }

            var empresaAnterior = user.EmpresaId;

            user.Username              = Username.Trim();
            user.Email                 = Email.Trim().ToLowerInvariant();
            user.IsActive              = IsActive;
            user.RequirePasswordChange = RequirePasswordChange;
            user.Role                  = Role.ToLowerInvariant();
            user.EmpresaId             = EmpresaId;
            user.EmpresaRolId          = string.IsNullOrEmpty(EmpresaRolId?.ToString()) ? null : EmpresaRolId;

            await _context.SaveChangesAsync();

            // Si cambió de empresa, su persona tiene que quedar también dentro de la empresa nueva.
            if (user.PersonaId is { } idPersona && empresaAnterior != user.EmpresaId)
                await _usuarioPersona.VincularAsync(PersonaDeUsuarioPagina.Quien(HttpContext), user.Id, idPersona);

            TempData["SuccessMessage"] = $"Usuario '{user.Username}' actualizado correctamente.";
            return RedirectToPage("/UserManagement");
        }

        /// <summary>Vincula al usuario con una persona que ya existe (elegida en la búsqueda).</summary>
        public async Task<IActionResult> OnPostVincularAsync(int id, int idPersona)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            var r = await _usuarioPersona.VincularAsync(PersonaDeUsuarioPagina.Quien(HttpContext), id, idPersona);
            if (r.Ok) await RefrescarNombreSiEsElPropioAsync(id);
            return VolverConMensaje(id, r, "Usuario vinculado con la persona.");
        }

        /// <summary>Quita la persona del usuario.</summary>
        public async Task<IActionResult> OnPostQuitarPersonaAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            var r = await _usuarioPersona.QuitarAsync(PersonaDeUsuarioPagina.Quien(HttpContext), id);
            return VolverConMensaje(id, r, "Se quitó la persona del usuario.");
        }

        /// <summary>Crea la persona con los datos mínimos y la vincula al usuario.</summary>
        public async Task<IActionResult> OnPostCrearPersonaAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAnyAdmin(HttpContext))     return RedirectToPage("/MainMenu");

            var r = await _usuarioPersona.CrearPersonaYVincularAsync(
                PersonaDeUsuarioPagina.Quien(HttpContext), id, PersonaNueva.ADatos(), PersonaNueva.ConfirmarQueEsOtraPersona);

            if (r.Ok)
            {
                await RefrescarNombreSiEsElPropioAsync(id);
                return VolverConMensaje(id, r, "Persona creada y vinculada al usuario.");
            }
            if (r.Estado == EstadoUsuarioPersona.NoEncontrado) return NotFound();

            // Se vuelve a mostrar la pantalla con lo escrito: solo cuentan los errores de la persona.
            foreach (var clave in ModelState.Keys.Where(k => !k.StartsWith(nameof(PersonaNueva), StringComparison.Ordinal)).ToList())
                ModelState.Remove(clave);

            if (!await CargarUsuarioAsync(id)) return NotFound();
            PersonaDeUsuarioPagina.AgregarErrores(ModelState, r.Errores);
            Parecidas = r.Parecidas;
            MostrarCrearPersona = true;
            await CargarListasAsync();
            await CargarPersonaAsync(buscar: false);
            return Page();
        }

        private IActionResult VolverConMensaje(int id, ResultadoUsuarioPersona r, string mensajeOk)
        {
            if (r.Estado == EstadoUsuarioPersona.NoEncontrado) return NotFound();

            if (r.Ok)
                TempData["SuccessMessage"] = r.Estado == EstadoUsuarioPersona.SinCambios ? "No había nada que cambiar." : mensajeOk;
            else
                TempData["ErrorMessage"] = string.Join(" ", r.Errores.Select(e => e.Mensaje));

            return RedirectToPage(new { id });
        }

        /// <summary>Si quien opera cambió la persona de su propio usuario, el nombre de la barra se actualiza sin volver a entrar.</summary>
        private async Task RefrescarNombreSiEsElPropioAsync(int idUsuario)
        {
            if (HttpContext.Session.GetString("UserId") != idUsuario.ToString()) return;

            var p = await _context.Users.AsNoTracking()
                .Where(u => u.Id == idUsuario && u.Persona != null)
                .Select(u => new { u.Persona!.PrimerNombre, u.Persona.PrimerApellido, u.Persona.Nombres, u.Persona.Apellidos })
                .FirstOrDefaultAsync();
            if (p != null)
                HttpContext.Session.SetString(AuthHelper.ClaveNombrePersona,
                    NombresPersona.Corto(p.PrimerNombre, p.PrimerApellido, p.Nombres, p.Apellidos));
        }

        private async Task<bool> CargarUsuarioAsync(int id)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return false;

            Id                    = user.Id;
            Username              = user.Username;
            Email                 = user.Email;
            IsActive              = user.IsActive;
            RequirePasswordChange = user.RequirePasswordChange;
            Role                  = user.Role;
            EmpresaId             = user.EmpresaId;
            EmpresaRolId          = user.EmpresaRolId;
            return true;
        }

        private async Task CargarPersonaAsync(bool buscar)
        {
            var quien = PersonaDeUsuarioPagina.Quien(HttpContext);
            EsAdministradorGeneral = quien.EsAdministradorGeneral;
            PersonaActual = await _usuarioPersona.PersonaDelUsuarioAsync(quien, Id);
            if (buscar && !string.IsNullOrWhiteSpace(BuscarPersona))
                Resultados = await _usuarioPersona.BuscarPersonasAsync(quien, BuscarPersona);

            TiposDocumento = await _context.CatalogoTiposDocumento.AsNoTracking()
                .Where(t => t.Activo && t.EsIdentidad)
                .OrderBy(t => t.Codigo == "DNI" ? 0 : 1).ThenBy(t => t.Nombre)
                .Select(t => new SelectListItem(t.Nombre, t.Codigo))
                .ToListAsync();
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
