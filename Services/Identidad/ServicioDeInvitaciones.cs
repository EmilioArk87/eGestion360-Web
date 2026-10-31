using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>Qué se pide al invitar: a quién (correo y persona), a qué empresa y con qué rol.</summary>
    /// <param name="RolLegado"><c>empresa_admin</c> o <c>empresa_user</c>; el administrador de empresa no lleva rol, el usuario sí.</param>
    public sealed record SolicitudDeInvitacion(string Correo, int IdEmpresa, string RolLegado, int? IdRol, int IdPersona);

    public enum EstadoDeInvitacion { Pendiente, Usada, Vencida }

    /// <summary>Una fila de la lista de invitaciones (sin el token, que solo existió en el correo).</summary>
    public sealed record InvitacionListada(
        int IdInvitacion, string Correo, string Empresa, string? Persona, string Rol, EstadoDeInvitacion Estado,
        DateTime FechaCreacion, DateTime VenceEn, DateTime? UsadaEn, string CreadaPor);

    public sealed record ResultadoDeInvitar(bool Ok, IReadOnlyList<string> Errores, int? IdInvitacion = null, bool CorreoEnviado = false)
    {
        public static ResultadoDeInvitar Fallo(params string[] errores) => new(false, errores);
    }

    public enum EstadoDeConsulta { Valida, NoEncontrada, Vencida, Usada, NoDisponible }

    /// <summary>Lo que la página de aceptación puede mostrar sin haber entrado: a qué empresa se invita y con qué correo.</summary>
    public sealed record ConsultaDeInvitacion(EstadoDeConsulta Estado, string? Correo = null, string? Empresa = null);

    public sealed record ResultadoDeAceptar(bool Ok, IReadOnlyList<string> Errores)
    {
        public static ResultadoDeAceptar Fallo(params string[] errores) => new(false, errores);
    }

    /// <summary>
    /// Invitaciones por correo (F1.5, ADR-028). Reemplazan el camino en que un administrador escribe la clave de otra persona: el
    /// administrador invita, la persona recibe un enlace de un solo uso y elige ella su clave. Mientras <c>dbo.Users</c> siga siendo
    /// donde se escribe (hasta F1.10), invitar crea el usuario <b>sin activar</b> (con la persona ya vinculada y una clave que nadie
    /// conoce) y aceptar lo activa: así los datos de tenant (vínculo de la persona, acceso a la empresa) se escriben con la
    /// autoridad del administrador que invita y no desde una página anónima, y volver al código anterior no pierde al usuario.
    /// Del token solo se guarda su SHA-256; viaja una vez, por correo.
    /// </summary>
    public interface IServicioDeInvitaciones
    {
        /// <param name="enlaceDe">Arma el enlace absoluto de aceptación a partir del token (lo conoce quien atiende la petición).</param>
        Task<ResultadoDeInvitar> InvitarAsync(QuienOperaUsuarios quien, SolicitudDeInvitacion solicitud, Func<string, string> enlaceDe, CancellationToken ct = default);

        /// <summary>Un enlace nuevo para una invitación sin usar; el anterior deja de servir.</summary>
        Task<ResultadoDeInvitar> ReenviarAsync(QuienOperaUsuarios quien, int idInvitacion, Func<string, string> enlaceDe, CancellationToken ct = default);

        /// <summary>El enlace deja de servir. El usuario sigue sin activar: se puede reenviar o editar después.</summary>
        Task<bool> AnularAsync(QuienOperaUsuarios quien, int idInvitacion, CancellationToken ct = default);

        Task<IReadOnlyList<InvitacionListada>> ListarAsync(QuienOperaUsuarios quien, CancellationToken ct = default);

        Task<ConsultaDeInvitacion> ConsultarAsync(string? token, CancellationToken ct = default);

        /// <summary>Activa al usuario con la clave que eligió. Falla sin dejar nada a medias si el token no sirve o la clave es débil.</summary>
        Task<ResultadoDeAceptar> AceptarAsync(string? token, string? clave, CancellationToken ct = default);
    }

    public sealed class ServicioDeInvitaciones : IServicioDeInvitaciones
    {
        private const int LargoMaximoDelCorreo = 100;   // dbo.Users.Email

        private readonly ApplicationDbContext _db;
        private readonly IUsuarioPersonaService _personas;
        private readonly ISincronizadorDeCuentas _cuentas;
        private readonly IPasswordService _claves;
        private readonly IEmailService _correo;
        private readonly ITenantContext _tenant;
        private readonly TenantContext? _contextoFijable;
        private readonly IOptions<OpcionesDeIdentidad> _opciones;
        private readonly IDerechosDeUso? _derechos;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<ServicioDeInvitaciones> _log;

        public ServicioDeInvitaciones(ApplicationDbContext db, IUsuarioPersonaService personas, ISincronizadorDeCuentas cuentas,
            IPasswordService claves, IEmailService correo, ITenantContext tenant, IOptions<OpcionesDeIdentidad> opciones,
            ILogger<ServicioDeInvitaciones> log, IDerechosDeUso? derechos = null, TimeProvider? tiempo = null, TenantContext? contextoFijable = null)
        {
            _db = db;
            _personas = personas;
            _cuentas = cuentas;
            _claves = claves;
            _correo = correo;
            _tenant = tenant;
            _contextoFijable = contextoFijable;
            _opciones = opciones;
            _log = log;
            _derechos = derechos;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        private DateTime Ahora => _tiempo.GetUtcNow().UtcDateTime;

        // ── Token ─────────────────────────────────────────────────────────────

        /// <summary>256 bits al azar en base64url (43 caracteres): inadivinable, sin caracteres que pidan escape en una URL.</summary>
        private static string NuevoToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        /// <summary>SHA-256 en hexadecimal minúscula (64 caracteres): lo único del token que se guarda.</summary>
        public static string Huella(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        // ── Invitar ───────────────────────────────────────────────────────────

        public async Task<ResultadoDeInvitar> InvitarAsync(QuienOperaUsuarios quien, SolicitudDeInvitacion solicitud, Func<string, string> enlaceDe,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            ArgumentNullException.ThrowIfNull(solicitud);
            ArgumentNullException.ThrowIfNull(enlaceDe);

            if (!PuedeOperar(quien)) return ResultadoDeInvitar.Fallo("No tienes permiso para invitar.");
            if (!quien.EsAdministradorGeneral && solicitud.IdEmpresa != quien.IdEmpresa)
                return ResultadoDeInvitar.Fallo("Solo puedes invitar a personas de tu propia empresa.");

            var correo = (solicitud.Correo ?? string.Empty).Trim().ToLowerInvariant();
            if (correo.Length == 0 || correo.Length > LargoMaximoDelCorreo || !new EmailAddressAttribute().IsValid(correo))
                return ResultadoDeInvitar.Fallo("Escribe un correo válido (hasta 100 caracteres).");

            if (solicitud.RolLegado is not (AuthHelper.EmpresaAdminRole or AuthHelper.EmpresaUserRole))
                return ResultadoDeInvitar.Fallo("Elige si la persona será administradora de la empresa o usuaria.");

            var empresa = await _db.Empresas.AsNoTracking()
                .Where(e => e.IdEmpresa == solicitud.IdEmpresa && !e.Eliminado && e.Activa)
                .Select(e => new { e.IdEmpresa, e.IdTenant, e.RazonSocial })
                .FirstOrDefaultAsync(ct);
            if (empresa == null) return ResultadoDeInvitar.Fallo("La empresa no existe o no está disponible.");

            if (!await TenantActivoAsync(empresa.IdTenant, ct))
                return ResultadoDeInvitar.Fallo("El tenant de la empresa no está activo: no se pueden enviar invitaciones.");

            int? idRol = null;
            if (solicitud.RolLegado == AuthHelper.EmpresaUserRole)
            {
                if (solicitud.IdRol is not > 0) return ResultadoDeInvitar.Fallo("Elige el rol que tendrá en la empresa.");
                var rolValido = await _db.EmpresaRoles.AsNoTracking()
                    .AnyAsync(r => r.IdRol == solicitud.IdRol && r.IdEmpresa == empresa.IdEmpresa && r.Activo, ct);
                if (!rolValido) return ResultadoDeInvitar.Fallo("El rol elegido no es de esa empresa o está inactivo.");
                idRol = solicitud.IdRol;
            }

            var persona = await _personas.PersonaVisibleAsync(quien, solicitud.IdPersona, ct);
            if (persona == null) return ResultadoDeInvitar.Fallo("Elige a la persona a la que invitas: debe estar registrada en tu empresa.");

            var conflicto = await ConflictoDeCorreoAsync(correo, ct);
            if (conflicto != null) return ResultadoDeInvitar.Fallo(conflicto);

            var limite = await VerificarLimiteAsync(empresa.IdTenant, ct);
            if (limite != null) return ResultadoDeInvitar.Fallo(limite);

            var token = NuevoToken();
            var ahora = Ahora;
            var username = await NombreDeUsuarioDisponibleAsync(correo, ct);

            // El usuario, el vínculo con su persona y la invitación se guardan juntos o no se guarda nada.
            await using var transaccion = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync(ct) : null;
            try
            {
                var usuario = new User
                {
                    Username = username, Email = correo, Password = _claves.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                    IsActive = false, RequirePasswordChange = false, Role = solicitud.RolLegado, EmpresaId = empresa.IdEmpresa,
                    EmpresaRolId = idRol, CreatedAt = ahora
                };
                _db.Users.Add(usuario);
                await _db.SaveChangesAsync(ct);

                var vinculo = await _personas.VincularAsync(quien, usuario.Id, solicitud.IdPersona, ct);
                if (!vinculo.Ok)
                {
                    await RevertirAsync(transaccion, ct);
                    return new ResultadoDeInvitar(false, vinculo.Errores.Select(e => e.Mensaje).DefaultIfEmpty("No se pudo vincular a la persona.").ToList());
                }

                await _cuentas.SincronizarAsync(usuario.Id, ct);
                var cuenta = await _db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.IdUserLegado == usuario.Id, ct);
                if (cuenta == null)
                {
                    await RevertirAsync(transaccion, ct);
                    return ResultadoDeInvitar.Fallo("No se pudo preparar la cuenta de la persona. Intenta de nuevo.");
                }

                var invitacion = new Invitacion
                {
                    IdTenant = empresa.IdTenant, Correo = correo, IdEmpresa = empresa.IdEmpresa, IdRol = idRol, TokenHash = Huella(token),
                    VenceEn = ahora.AddDays(Math.Max(1, _opciones.Value.DiasDeInvitacion)), IdCuenta = cuenta.IdCuenta,
                    CreadaPor = quien.Usuario, FechaCreacion = ahora
                };
                _db.Invitaciones.Add(invitacion);
                await _db.SaveChangesAsync(ct);

                if (transaccion != null) await transaccion.CommitAsync(ct);

                var enviado = await EnviarAsync(invitacion, empresa.RazonSocial, quien.Usuario, enlaceDe(token), ct);
                return new ResultadoDeInvitar(true, Array.Empty<string>(), invitacion.IdInvitacion, enviado);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo crear la invitación para {Correo}", correo);
                _db.ChangeTracker.Clear();
                return ResultadoDeInvitar.Fallo("No se pudo guardar la invitación. Revisa los datos e intenta de nuevo.");
            }
        }

        public async Task<ResultadoDeInvitar> ReenviarAsync(QuienOperaUsuarios quien, int idInvitacion, Func<string, string> enlaceDe, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            ArgumentNullException.ThrowIfNull(enlaceDe);
            if (!PuedeOperar(quien)) return ResultadoDeInvitar.Fallo("No tienes permiso para invitar.");

            var invitacion = await InvitacionVisibleAsync(quien, idInvitacion, ct);
            if (invitacion == null) return ResultadoDeInvitar.Fallo("No se encontró la invitación.");
            if (invitacion.UsadaEn != null) return ResultadoDeInvitar.Fallo("Esa invitación ya se aceptó.");
            if (!await TenantActivoAsync(invitacion.IdTenant, ct))
                return ResultadoDeInvitar.Fallo("El tenant de la empresa no está activo: no se pueden enviar invitaciones.");

            var limite = await VerificarLimiteAsync(invitacion.IdTenant, ct);
            if (limite != null) return ResultadoDeInvitar.Fallo(limite);

            var token = NuevoToken();
            invitacion.TokenHash = Huella(token);
            invitacion.VenceEn = Ahora.AddDays(Math.Max(1, _opciones.Value.DiasDeInvitacion));
            await _db.SaveChangesAsync(ct);

            var empresa = await NombreDeLaEmpresaAsync(invitacion.IdEmpresa, ct);
            var enviado = await EnviarAsync(invitacion, empresa, quien.Usuario, enlaceDe(token), ct);
            return new ResultadoDeInvitar(true, Array.Empty<string>(), invitacion.IdInvitacion, enviado);
        }

        public async Task<bool> AnularAsync(QuienOperaUsuarios quien, int idInvitacion, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            if (!PuedeOperar(quien)) return false;

            var invitacion = await InvitacionVisibleAsync(quien, idInvitacion, ct);
            if (invitacion == null || invitacion.UsadaEn != null) return false;

            // El enlace deja de servir en el acto; la fila queda como rastro.
            invitacion.VenceEn = Ahora;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<InvitacionListada>> ListarAsync(QuienOperaUsuarios quien, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            if (!PuedeOperar(quien)) return Array.Empty<InvitacionListada>();

            var filas = await Visibles(quien).AsNoTracking()
                .OrderByDescending(i => i.FechaCreacion).ThenByDescending(i => i.IdInvitacion)
                .Take(200)
                .ToListAsync(ct);
            if (filas.Count == 0) return Array.Empty<InvitacionListada>();

            var idsEmpresa = filas.Where(f => f.IdEmpresa != null).Select(f => f.IdEmpresa!.Value).Distinct().ToList();
            var empresas = await _db.Empresas.AsNoTracking().Where(e => idsEmpresa.Contains(e.IdEmpresa))
                .Select(e => new { e.IdEmpresa, e.RazonSocial }).ToDictionaryAsync(e => e.IdEmpresa, e => e.RazonSocial, ct);
            var idsRol = filas.Where(f => f.IdRol != null).Select(f => f.IdRol!.Value).Distinct().ToList();
            var roles = await _db.EmpresaRoles.AsNoTracking().Where(r => idsRol.Contains(r.IdRol))
                .Select(r => new { r.IdRol, r.Nombre }).ToDictionaryAsync(r => r.IdRol, r => r.Nombre, ct);

            // El usuario de cada invitación (por su cuenta) da la persona y si es administrador de empresa.
            var idsCuenta = filas.Where(f => f.IdCuenta != null).Select(f => f.IdCuenta!.Value).Distinct().ToList();
            var usuarios = await (from c in _db.Cuentas.AsNoTracking()
                                  join u in _db.Users.AsNoTracking() on c.IdUserLegado equals u.Id
                                  where idsCuenta.Contains(c.IdCuenta)
                                  select new { c.IdCuenta, u.Role, u.PersonaId }).ToListAsync(ct);
            var idsPersona = usuarios.Where(u => u.PersonaId != null).Select(u => u.PersonaId!.Value).Distinct().ToList();
            var nombres = idsPersona.Count == 0
                ? new Dictionary<int, string>()
                : await _db.Personas.AsNoTracking().Where(p => idsPersona.Contains(p.IdPersona))
                    .Select(p => new { p.IdPersona, p.Apellidos, p.Nombres }).ToDictionaryAsync(p => p.IdPersona, p => $"{p.Apellidos}, {p.Nombres}", ct);

            var ahora = Ahora;
            return filas.Select(f =>
            {
                var usuario = usuarios.FirstOrDefault(u => u.IdCuenta == f.IdCuenta);
                var rol = f.IdRol != null && roles.TryGetValue(f.IdRol.Value, out var nombreRol)
                    ? nombreRol
                    : usuario?.Role == AuthHelper.EmpresaAdminRole ? "Administrador de empresa" : "Sin rol";
                var persona = usuario?.PersonaId is { } idPersona && nombres.TryGetValue(idPersona, out var nombre) ? nombre : null;
                var estado = f.UsadaEn != null ? EstadoDeInvitacion.Usada : f.VenceEn <= ahora ? EstadoDeInvitacion.Vencida : EstadoDeInvitacion.Pendiente;
                return new InvitacionListada(
                    f.IdInvitacion, f.Correo, f.IdEmpresa != null && empresas.TryGetValue(f.IdEmpresa.Value, out var razon) ? razon : "—",
                    persona, rol, estado, f.FechaCreacion, f.VenceEn, f.UsadaEn, f.CreadaPor);
            }).ToList();
        }

        // ── Aceptar ───────────────────────────────────────────────────────────

        public async Task<ConsultaDeInvitacion> ConsultarAsync(string? token, CancellationToken ct = default)
        {
            var (invitacion, estado) = await BuscarAsync(token, ct);
            if (invitacion == null || estado != EstadoDeConsulta.Valida) return new ConsultaDeInvitacion(estado);

            if (!await TenantActivoAsync(invitacion.IdTenant, ct)) return new ConsultaDeInvitacion(EstadoDeConsulta.NoDisponible);
            var usuario = await UsuarioPendienteAsync(invitacion, ct);
            if (usuario == null) return new ConsultaDeInvitacion(EstadoDeConsulta.NoDisponible);

            return new ConsultaDeInvitacion(EstadoDeConsulta.Valida, invitacion.Correo, await NombreDeLaEmpresaAsync(invitacion.IdEmpresa, ct));
        }

        public async Task<ResultadoDeAceptar> AceptarAsync(string? token, string? clave, CancellationToken ct = default)
        {
            var (invitacion, estado) = await BuscarAsync(token, ct);
            if (invitacion == null || estado != EstadoDeConsulta.Valida) return ResultadoDeAceptar.Fallo(MensajeDeEstado(estado));
            if (!await TenantActivoAsync(invitacion.IdTenant, ct)) return ResultadoDeAceptar.Fallo(MensajeDeEstado(EstadoDeConsulta.NoDisponible));

            var usuario = await UsuarioPendienteAsync(invitacion, ct);
            if (usuario == null) return ResultadoDeAceptar.Fallo(MensajeDeEstado(EstadoDeConsulta.NoDisponible));

            var errorDeClave = PasswordPolicy.Validar(clave, usuario.Username, usuario.Email);
            if (errorDeClave != null || string.IsNullOrEmpty(clave)) return ResultadoDeAceptar.Fallo(errorDeClave ?? "Escribe tu contraseña.");

            var limite = await VerificarLimiteAsync(invitacion.IdTenant, ct);
            if (limite != null) return ResultadoDeAceptar.Fallo(limite);

            // Esta pantalla corre sin sesión (ámbito de plataforma) y solo conoce el tenant por la invitación. Activar al usuario escribe su
            // acceso a la empresa, que es una tabla de tenant: el tenant se fija ANTES de abrir la transacción, porque la base no deja
            // cambiarlo con una conexión o una transacción abiertas (ContextoDeSesionInterceptor).
            _contextoFijable?.Fijar(invitacion.IdTenant, invitacion.IdEmpresa);

            await using var transaccion = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync(ct) : null;
            try
            {
                // Se reclama la invitación primero y de un solo golpe: si dos personas abren el enlace a la vez, solo una gana.
                var ahora = Ahora;
                var id = invitacion.IdInvitacion;
                var reclamadas = await _db.Invitaciones
                    .Where(i => i.IdInvitacion == id && i.UsadaEn == null && i.VenceEn > ahora)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsadaEn, ahora), ct);
                if (reclamadas == 0)
                {
                    await RevertirAsync(transaccion, ct);
                    return ResultadoDeAceptar.Fallo(MensajeDeEstado(EstadoDeConsulta.Usada));
                }
                await _db.Entry(invitacion).ReloadAsync(ct);   // ExecuteUpdate no pasa por el seguimiento: que la invitación en memoria vea el cambio

                usuario.Password = _claves.HashPassword(clave);
                usuario.IsActive = true;
                usuario.RequirePasswordChange = false;
                await _db.SaveChangesAsync(ct);

                await _cuentas.SincronizarAsync(usuario.Id, ct);

                var activa = await _db.Cuentas.AsNoTracking().AnyAsync(c => c.IdUserLegado == usuario.Id && c.Estado == EstadoCuenta.Activa, ct);
                if (!activa)
                {
                    await RevertirAsync(transaccion, ct);
                    _log.LogError("La invitación {IdInvitacion} no activó la cuenta del usuario {IdUsuario}", id, usuario.Id);
                    return ResultadoDeAceptar.Fallo("No se pudo activar tu cuenta. Pide que te reenvíen la invitación.");
                }

                if (transaccion != null) await transaccion.CommitAsync(ct);
                return new ResultadoDeAceptar(true, Array.Empty<string>());
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo aceptar la invitación {IdInvitacion}", invitacion.IdInvitacion);
                _db.ChangeTracker.Clear();
                return ResultadoDeAceptar.Fallo("No se pudo activar tu cuenta. Intenta de nuevo.");
            }
        }

        // ── Utilidades ────────────────────────────────────────────────────────

        public static string MensajeDeEstado(EstadoDeConsulta estado) => estado switch
        {
            EstadoDeConsulta.NoEncontrada => "El enlace de invitación no es válido. Revisa que lo hayas copiado completo o pide que te envíen otro.",
            EstadoDeConsulta.Vencida => "Esta invitación venció. Pide que te envíen una nueva.",
            EstadoDeConsulta.Usada => "Esta invitación ya se usó. Si ya activaste tu cuenta, inicia sesión con tu correo y tu contraseña.",
            _ => "Esta invitación ya no está disponible. Pide que te envíen una nueva."
        };

        private static bool PuedeOperar(QuienOperaUsuarios quien) => quien.EsAdministradorGeneral || quien.IdEmpresa is > 0;

        /// <summary>Las invitaciones que quien opera puede ver: un administrador de empresa, las de su empresa; el general, las del tenant en que trabaja.</summary>
        private IQueryable<Invitacion> Visibles(QuienOperaUsuarios quien)
        {
            var consulta = _db.Invitaciones.AsQueryable();
            if (!quien.EsAdministradorGeneral) return consulta.Where(i => i.IdEmpresa == quien.IdEmpresa);

            var idTenant = _tenant.IdTenant;
            return idTenant == null ? consulta : consulta.Where(i => i.IdTenant == idTenant);
        }

        private Task<Invitacion?> InvitacionVisibleAsync(QuienOperaUsuarios quien, int idInvitacion, CancellationToken ct)
            => Visibles(quien).FirstOrDefaultAsync(i => i.IdInvitacion == idInvitacion, ct);

        /// <summary>La invitación del token y su estado. Sin token, o con uno que no existe, es «no encontrada»: no se distingue.</summary>
        private async Task<(Invitacion? Invitacion, EstadoDeConsulta Estado)> BuscarAsync(string? token, CancellationToken ct)
        {
            token = token?.Trim();
            if (string.IsNullOrEmpty(token) || token.Length > 200) return (null, EstadoDeConsulta.NoEncontrada);

            var huella = Huella(token);
            var invitacion = await _db.Invitaciones.FirstOrDefaultAsync(i => i.TokenHash == huella, ct);
            if (invitacion == null) return (null, EstadoDeConsulta.NoEncontrada);
            if (invitacion.UsadaEn != null) return (invitacion, EstadoDeConsulta.Usada);
            if (invitacion.VenceEn <= Ahora) return (invitacion, EstadoDeConsulta.Vencida);
            return (invitacion, EstadoDeConsulta.Valida);
        }

        /// <summary>El usuario que la invitación dejó sin activar (por su cuenta); nulo si ya no es el mismo o alguien lo activó por otro camino.</summary>
        private async Task<User?> UsuarioPendienteAsync(Invitacion invitacion, CancellationToken ct)
        {
            if (invitacion.IdCuenta is not { } idCuenta) return null;
            var idUsuario = await _db.Cuentas.AsNoTracking().Where(c => c.IdCuenta == idCuenta).Select(c => c.IdUserLegado).FirstOrDefaultAsync(ct);
            if (idUsuario == null) return null;

            var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == idUsuario, ct);
            return usuario is { IsActive: false } && string.Equals(usuario.Email, invitacion.Correo, StringComparison.OrdinalIgnoreCase) ? usuario : null;
        }

        private async Task<bool> TenantActivoAsync(int idTenant, CancellationToken ct)
            => await _db.Tenants.AsNoTracking().AnyAsync(t => t.IdTenant == idTenant && t.Estado == EstadoTenant.Activo, ct);

        private async Task<string?> VerificarLimiteAsync(int idTenant, CancellationToken ct)
        {
            if (_derechos == null) return null;
            var limite = await _derechos.PuedeAgregarAsync(idTenant, CodigosDeDerecho.LimiteUsuarios, ct);
            return limite.Permitido ? null : limite.Mensaje("usuarios activos");
        }

        private async Task<string> NombreDeLaEmpresaAsync(int? idEmpresa, CancellationToken ct)
            => idEmpresa == null
                ? "tu empresa"
                : await _db.Empresas.AsNoTracking().Where(e => e.IdEmpresa == idEmpresa).Select(e => e.RazonSocial).FirstOrDefaultAsync(ct) ?? "tu empresa";

        /// <summary>Por qué no se puede invitar a ese correo: ya hay un usuario, una cuenta o un alias con él (o una invitación esperándolo).</summary>
        private async Task<string?> ConflictoDeCorreoAsync(string correo, CancellationToken ct)
        {
            var pendiente = await (from i in _db.Invitaciones.AsNoTracking()
                                   where i.Correo == correo && i.UsadaEn == null
                                   select i.IdInvitacion).AnyAsync(ct);
            if (pendiente) return "Ese correo ya tiene una invitación pendiente: reenvíala o anúlala desde la lista de invitaciones.";

            var existe = await _db.Users.AsNoTracking().AnyAsync(u => u.Email.ToLower() == correo, ct)
                         || await _db.Cuentas.AsNoTracking().AnyAsync(c => c.Correo.ToLower() == correo, ct)
                         || await _db.CuentaAlias.AsNoTracking().AnyAsync(a => a.Alias.ToLower() == correo, ct);
            return existe
                ? "Ese correo ya tiene un usuario. Por ahora las invitaciones son para personas que aún no tienen cuenta."
                : null;
        }

        /// <summary>El nombre de usuario sale del correo (la parte antes de la @) y se numera si ya existe; la persona entra con su correo.</summary>
        private async Task<string> NombreDeUsuarioDisponibleAsync(string correo, CancellationToken ct)
        {
            var local = correo.Split('@')[0];
            var baseNombre = new string(local.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
            if (baseNombre.Length < 3) baseNombre = "usuario";
            if (baseNombre.Length > 40) baseNombre = baseNombre[..40];

            for (var i = 0; ; i++)
            {
                var candidato = i == 0 ? baseNombre : baseNombre + i;
                var minuscula = candidato.ToLowerInvariant();
                var usado = await _db.Users.AsNoTracking().AnyAsync(u => u.Username.ToLower() == minuscula, ct)
                            || await _db.Cuentas.AsNoTracking().AnyAsync(c => c.NombreUsuario != null && c.NombreUsuario.ToLower() == minuscula, ct)
                            || await _db.CuentaAlias.AsNoTracking().AnyAsync(a => a.Alias.ToLower() == minuscula, ct);
                if (!usado) return candidato;
            }
        }

        private async Task<bool> EnviarAsync(Invitacion invitacion, string empresa, string invitadoPor, string enlace, CancellationToken ct)
        {
            try
            {
                var cuerpo = CorreoDeInvitacion(empresa, invitadoPor, enlace, invitacion.VenceEn);
                var enviado = await _correo.SendHtmlEmailAsync(invitacion.Correo, "Te invitaron a eGestion360", cuerpo);
                if (!enviado) _log.LogWarning("No se pudo enviar el correo de la invitación {IdInvitacion}", invitacion.IdInvitacion);
                return enviado;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error enviando el correo de la invitación {IdInvitacion}", invitacion.IdInvitacion);
                return false;
            }
        }

        private static string CorreoDeInvitacion(string empresa, string invitadoPor, string enlace, DateTime venceUtc)
        {
            var vence = venceUtc.AddHours(-6).ToString("dd/MM/yyyy HH:mm");   // hora de Honduras
            return $@"
<h3>Hola,</h3>

<p><strong>{WebUtility.HtmlEncode(invitadoPor)}</strong> te invitó a usar <strong>eGestion360</strong> en <strong>{WebUtility.HtmlEncode(empresa)}</strong>.</p>

<p>Para activar tu cuenta elige tu contraseña desde este enlace:</p>

<p style='text-align:center; margin: 24px 0;'>
    <a href='{WebUtility.HtmlEncode(enlace)}' style='background:#007bff; color:#ffffff; padding:12px 24px; text-decoration:none; border-radius:4px; font-weight:bold;'>Activar mi cuenta</a>
</p>

<p style='font-size:12px; color:#555;'>Si el botón no funciona, copia esta dirección en tu navegador:<br>{WebUtility.HtmlEncode(enlace)}</p>

<div style='background: #fff3cd; padding: 15px; border-left: 4px solid #ffc107; margin: 20px 0;'>
    <strong>Importante:</strong>
    <ul>
        <li>El enlace sirve una sola vez y vence el <strong>{vence}</strong> (hora de Honduras).</li>
        <li>Nadie te va a pedir tu contraseña por correo ni por teléfono.</li>
        <li>Si no esperabas esta invitación, ignora este mensaje: tu cuenta no se activa sin tu contraseña.</li>
    </ul>
</div>

<p>Saludos,<br>
<strong>Equipo eGestion360</strong><br>
SIP Tecnología</p>";
        }

        /// <summary>Deshace lo guardado en esta operación (si la transacción es nuestra) y descarta lo que el contexto traía en memoria.</summary>
        private async Task RevertirAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaccion, CancellationToken ct)
        {
            if (transaccion != null) await transaccion.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
        }
    }
}
