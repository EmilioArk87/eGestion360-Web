using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages.Admin.Usuarios
{
    [RequierePermiso("administracion.usuarios.editar")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IUsuarioPersonaService _usuarioPersona;
        private readonly eGestion360Web.Services.Identidad.ISincronizadorDeCuentas? _cuentas;
        private readonly ILogger<EditModel>? _log;

        public EditModel(ApplicationDbContext context, IUsuarioPersonaService usuarioPersona,
            eGestion360Web.Services.Identidad.ISincronizadorDeCuentas? cuentas = null, ILogger<EditModel>? log = null)
        {
            _context = context;
            _usuarioPersona = usuarioPersona;
            _cuentas = cuentas;
            _log = log;
        }

        public const string MensajeEmpresaDeOtroTenant =
            "La empresa elegida es de otro tenant. Un usuario no se pasa de un tenant a otro: su persona y su cuenta son de su tenant. " +
            "Para darle acceso en otro tenant, créale el usuario desde ese tenant.";

        public const string MensajeRolDeOtraEmpresa =
            "El rol elegido no es de esa empresa. Deja «Sin rol específico», guarda y luego elige uno de los roles de la empresa.";

        public const string MensajeCuentaNoSincronizada =
            "No se pudo actualizar el acceso de la cuenta de este usuario, así que no se guardó ningún cambio. " +
            "Revisa la empresa y el rol, o avisa a soporte.";

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

            if (!await CargarUsuarioAsync(id)) return NotFound();
            if (!PuedeEditarUsuarioDe(EmpresaId))
                return RedirectToPage("/MainMenu");
            if (await EsDeOtroTenantAsync(EmpresaId)) return AElegirTenantDelUsuario();

            await CargarListasAsync();
            await CargarPersonaAsync(buscar: true);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            if (!AuthHelper.EsOperador(HttpContext))
                EmpresaId = AuthHelper.GetEmpresaId(HttpContext);

            if (!ModelState.IsValid)
            {
                await CargarListasAsync();
                await CargarPersonaAsync(buscar: false);
                return Page();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == Id);
            if (user == null) return NotFound();

            // Quien no es del personal de la plataforma solo edita usuarios de su empresa.
            if (!PuedeEditarUsuarioDe(user.EmpresaId))
                return RedirectToPage("/MainMenu");
            if (await EsDeOtroTenantAsync(user.EmpresaId)) return AElegirTenantDelUsuario();

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

            // La empresa y el rol se comprueban ANTES de guardar nada: la cuenta del usuario no se puede llevar a otro tenant desde aquí
            // (su membresía exige una persona de ese tenant) y, si se guardaba Users primero, el fallo de la cuenta lo dejaba a medias
            // (pasó el 2026-11-18 con el usuario egaray y la empresa de CAPAE).
            if (await ErrorDeEmpresaYRolAsync(user) is { } error)
            {
                ModelState.AddModelError(error.Campo, error.Mensaje);
                await CargarListasAsync();
                await CargarPersonaAsync(buscar: false);
                return Page();
            }

            var empresaAnterior = user.EmpresaId;
            var antes = (user.Username, user.Email, user.IsActive, user.RequirePasswordChange, user.Role, user.EmpresaId, user.EmpresaRolId);

            user.Username              = Username.Trim();
            user.Email                 = Email.Trim().ToLowerInvariant();
            user.IsActive              = IsActive;
            user.RequirePasswordChange = RequirePasswordChange;
            user.Role                  = Role.ToLowerInvariant();
            user.EmpresaId             = EmpresaId;
            user.EmpresaRolId          = string.IsNullOrEmpty(EmpresaRolId?.ToString()) ? null : EmpresaRolId;

            await _context.SaveChangesAsync();

            // La cuenta global sigue a Users (F1.5): clave pendiente, estado, empresa y rol llegan a su acceso. Si eso falla, Users vuelve a
            // como estaba: nunca quedan el usuario con una empresa y la cuenta con otra.
            if (_cuentas != null)
            {
                try
                {
                    await _cuentas.SincronizarAsync(user.Id);
                }
                catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
                {
                    _log?.LogError(ex, "No se pudo sincronizar la cuenta del usuario {IdUsuario}: se deshace la edición.", user.Id);
                    await DeshacerEdicionAsync(user.Id, antes);
                    TempData["ErrorMessage"] = MensajeCuentaNoSincronizada;
                    return RedirectToPage(new { id = user.Id });
                }
            }

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

            var r = await _usuarioPersona.VincularAsync(PersonaDeUsuarioPagina.Quien(HttpContext), id, idPersona);
            if (r.Ok) await RefrescarNombreSiEsElPropioAsync(id);
            return VolverConMensaje(id, r, "Usuario vinculado con la persona.");
        }

        /// <summary>Quita la persona del usuario.</summary>
        public async Task<IActionResult> OnPostQuitarPersonaAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

            var r = await _usuarioPersona.QuitarAsync(PersonaDeUsuarioPagina.Quien(HttpContext), id);
            return VolverConMensaje(id, r, "Se quitó la persona del usuario.");
        }

        /// <summary>Crea la persona con los datos mínimos y la vincula al usuario.</summary>
        public async Task<IActionResult> OnPostCrearPersonaAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");

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

        /// <summary>
        /// El alcance de quien edita: el personal de la plataforma, cualquier usuario de las empresas que ve su contexto; el resto, solo
        /// los de la empresa de su sesión. Un usuario sin empresa (superadmin) solo se edita desde el contexto de plataforma: en una
        /// empresa la lista no ofrece «sin empresa» y guardarlo lo asignaría a otra.
        /// </summary>
        private bool PuedeEditarUsuarioDe(int? empresaDelUsuario)
            => AuthHelper.PuedeActuarSobreEmpresa(HttpContext, empresaDelUsuario)
               && (empresaDelUsuario is > 0 || AuthHelper.EnPlataforma(HttpContext));

        /// <summary>
        /// Un usuario de otro tenant no se edita desde el tenant de soporte equivocado: la lista de empresas solo trae las del
        /// tenant de soporte, y guardar sin su empresa la dejaría en blanco (F1.4c, ADR-025). Solo aplica al personal de la
        /// plataforma; quien trabaja en una empresa ya queda acotado a ella.
        /// </summary>
        private async Task<bool> EsDeOtroTenantAsync(int? empresaDelUsuario)
            => AuthHelper.EsOperador(HttpContext) && empresaDelUsuario is > 0
               && !await _context.Empresas.AsNoTracking().AnyAsync(e => e.IdEmpresa == empresaDelUsuario);

        /// <summary>
        /// El tenant al que pertenece el usuario: el de su persona (la membresía de la cuenta exige una persona de su mismo tenant) o, si
        /// no tiene persona, el de su empresa actual. <c>Conocido</c> es falso para un usuario sin persona ni empresa, que puede ir a
        /// cualquiera. Una persona que no se ve desde este contexto es de otro tenant: queda conocido con tenant nulo.
        /// </summary>
        private async Task<(bool Conocido, int? IdTenant)> TenantDelUsuarioAsync(int? idPersona, int? idEmpresa)
        {
            if (idPersona is { } persona)
                return (true, await _context.Personas.AsNoTracking()
                    .Where(p => p.IdPersona == persona).Select(p => (int?)p.IdTenant).FirstOrDefaultAsync());

            if (idEmpresa is { } empresa)
            {
                var tenant = await _context.Empresas.AsNoTracking()
                    .Where(e => e.IdEmpresa == empresa).Select(e => (int?)e.IdTenant).FirstOrDefaultAsync();
                return (tenant != null, tenant);
            }
            return (false, null);
        }

        /// <summary>
        /// Por qué no se puede guardar la empresa y el rol elegidos, o nulo si se puede: la empresa tiene que existir y ser del tenant del
        /// usuario, y el rol, de esa empresa. La lista de la pantalla ya no ofrece otras, pero esto también ataja un formulario alterado.
        /// </summary>
        private async Task<(string Campo, string Mensaje)?> ErrorDeEmpresaYRolAsync(User user)
        {
            if (EmpresaId is not { } idEmpresa) return null;

            var empresa = await _context.Empresas.AsNoTracking()
                .Where(e => e.IdEmpresa == idEmpresa && !e.Eliminado)
                .Select(e => new { e.IdTenant })
                .FirstOrDefaultAsync();
            if (empresa == null) return (nameof(EmpresaId), "La empresa elegida no existe o no se ve desde aquí.");

            var (conocido, tenantDelUsuario) = await TenantDelUsuarioAsync(user.PersonaId, user.EmpresaId);
            if (conocido && tenantDelUsuario != empresa.IdTenant) return (nameof(EmpresaId), MensajeEmpresaDeOtroTenant);

            if (EmpresaRolId is { } idRol
                && !await _context.EmpresaRoles.AsNoTracking().AnyAsync(r => r.IdRol == idRol && r.IdEmpresa == idEmpresa))
                return (nameof(EmpresaRolId), MensajeRolDeOtraEmpresa);

            return null;
        }

        /// <summary>
        /// Devuelve el usuario a los datos que tenía antes de esta edición y vuelve a sincronizar su cuenta con ellos, para que nada quede a
        /// medias cuando la cuenta no se pudo actualizar.
        /// </summary>
        private async Task DeshacerEdicionAsync(int idUsuario,
            (string Username, string Email, bool IsActive, bool RequirePasswordChange, string Role, int? EmpresaId, int? EmpresaRolId) antes)
        {
            _context.ChangeTracker.Clear();
            var user = await _context.Users.FirstAsync(u => u.Id == idUsuario);
            (user.Username, user.Email, user.IsActive, user.RequirePasswordChange, user.Role, user.EmpresaId, user.EmpresaRolId) = antes;
            await _context.SaveChangesAsync();

            try
            {
                await _cuentas!.SincronizarAsync(idUsuario);
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                _context.ChangeTracker.Clear();
                _log?.LogError(ex, "Tampoco se pudo volver a sincronizar la cuenta del usuario {IdUsuario} con sus datos anteriores.", idUsuario);
            }
        }

        private IActionResult AElegirTenantDelUsuario()
        {
            TempData["SoporteError"] = "El usuario pertenece a otro tenant. Entra al tenant de su empresa y vuelve a abrirlo.";
            return RedirectToPage("/Admin/Soporte/Index");
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
            if (AuthHelper.EsOperador(HttpContext))
            {
                // Solo las empresas del tenant del usuario: desde aquí no se lo pasa a otro (ver ErrorDeEmpresaYRolAsync).
                var usuario = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == Id).Select(u => new { u.PersonaId, u.EmpresaId }).FirstOrDefaultAsync();
                var (conocido, tenantDelUsuario) = usuario == null ? (false, null) : await TenantDelUsuarioAsync(usuario.PersonaId, usuario.EmpresaId);

                Empresas = await _context.Empresas
                    .Where(e => !e.Eliminado && e.Activa)
                    .Where(e => !conocido || e.IdTenant == tenantDelUsuario)
                    .OrderBy(e => e.RazonSocial)
                    .Select(e => new SelectListItem(e.RazonSocial, e.IdEmpresa.ToString()))
                    .ToListAsync();
                if (AuthHelper.EnPlataforma(HttpContext))
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
