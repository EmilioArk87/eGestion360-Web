using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Auditoria;

namespace eGestion360Web.Services.Identidad
{
    public enum FalloDeCredenciales
    {
        /// <summary>La clave es correcta y la cuenta puede entrar.</summary>
        Ninguno,

        /// <summary>No existe esa cuenta, la clave es otra, o la cuenta no está activa. Todas se muestran igual para no revelar cuáles cuentas existen.</summary>
        Incorrectas,

        /// <summary>La cuenta está bloqueada por demasiados fallos seguidos.</summary>
        Bloqueada
    }

    public sealed record ResultadoDeCredenciales(FalloDeCredenciales Fallo, Cuenta? Cuenta = null, string NombreEscrito = "", DateTime? BloqueadaHasta = null)
    {
        public bool Correctas => Fallo == FalloDeCredenciales.Ninguno;
    }

    /// <summary>Qué se le pide a la persona sobre la verificación en dos pasos al iniciar sesión (D47).</summary>
    public enum ExigenciaDeMfa
    {
        NoRequerida,

        /// <summary>Ya la activó (o se la exigen y la tiene): debe escribir un código.</summary>
        Verificar,

        /// <summary>Se la exigen y todavía no la activó: debe activarla antes de entrar.</summary>
        Alta,

        /// <summary>Se le exigirá pronto (administrador de empresa en su ventana de gracia): entra, con un aviso.</summary>
        Aviso
    }

    public interface IAutenticador
    {
        /// <summary>Comprueba el usuario o correo y la clave, llevando la cuenta de fallos y el bloqueo temporal.</summary>
        Task<ResultadoDeCredenciales> VerificarCredencialesAsync(string identificador, string clave, CancellationToken ct = default);

        /// <summary>Qué le toca a esta cuenta sobre la verificación en dos pasos, según dónde puede trabajar.</summary>
        Task<ExigenciaDeMfa> ExigenciaDeMfaAsync(Cuenta cuenta, ContextosDeLaCuenta contextos, CancellationToken ct = default);

        /// <summary>Arma los datos de la cookie para entrar a un sitio. La sello es la huella que luego usa el validador.</summary>
        DatosDeIdentidad ConstruirIdentidad(Cuenta cuenta, ContextoDeAcceso contexto, string nombreEscrito, bool mfaVerificada);

        /// <summary>Inicia la sesión: emite la cookie, anota el último acceso y deja la sesión lista para el sitio elegido.</summary>
        Task EntrarAsync(HttpContext http, Cuenta cuenta, DatosDeIdentidad datos, CancellationToken ct = default);

        /// <summary>Cierra la sesión: borra la cookie y vacía la sesión.</summary>
        Task SalirAsync(HttpContext http);
    }

    public sealed class Autenticador : IAutenticador
    {
        // Para que una cuenta inexistente tarde lo mismo que una real en responder (BCrypt es lento a propósito).
        private static readonly Lazy<string> HashFalso = new(() => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), 11));

        private readonly ApplicationDbContext _db;
        private readonly IDirectorioDeCuentas _directorio;
        private readonly IBloqueoDeCuentas _bloqueo;
        private readonly IServicioMfa _mfa;
        private readonly IPasswordService _claves;
        private readonly OpcionesDeIdentidad _opciones;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<Autenticador> _log;
        private readonly IAuditoriaDeSeguridad? _auditoria;

        public Autenticador(ApplicationDbContext db, IDirectorioDeCuentas directorio, IBloqueoDeCuentas bloqueo, IServicioMfa mfa,
            IPasswordService claves, IOptions<OpcionesDeIdentidad> opciones, ILogger<Autenticador> log, TimeProvider? tiempo = null,
            IAuditoriaDeSeguridad? auditoria = null)
        {
            _auditoria = auditoria;
            _db = db;
            _directorio = directorio;
            _bloqueo = bloqueo;
            _mfa = mfa;
            _claves = claves;
            _opciones = opciones.Value;
            _log = log;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        public async Task<ResultadoDeCredenciales> VerificarCredencialesAsync(string identificador, string clave, CancellationToken ct = default)
        {
            var cuenta = await _directorio.BuscarParaEntrarAsync(identificador, ct);
            if (cuenta == null)
            {
                SimularVerificacion(clave);
                // Una cuenta que no existe deja una fila por dirección y por hora, con el nombre enmascarado (D71, D72).
                await RegistrarFalloAsync(null, EnmascaradoDeAuditoria.Identificador(identificador), "cuenta_inexistente", agregable: true, ct: ct);
                return new ResultadoDeCredenciales(FalloDeCredenciales.Incorrectas);
            }

            if (await _bloqueo.BloqueadaHastaAsync(cuenta.IdCuenta, ct) is { } hasta)
            {
                _log.LogWarning("Inicio de sesión rechazado: la cuenta {IdCuenta} está bloqueada hasta {Hasta:u}.", cuenta.IdCuenta, hasta);
                // Quien insiste sobre una cuenta bloqueada no llena la tabla: una fila por dirección y por hora.
                await RegistrarFalloAsync(cuenta, null, "bloqueada", agregable: true, ct: ct);
                return new ResultadoDeCredenciales(FalloDeCredenciales.Bloqueada, BloqueadaHasta: hasta);
            }

            if (!ClaveCorrecta(clave, cuenta.HashClave))
            {
                var sinBcrypt = cuenta.HashClave.StartsWith("$2", StringComparison.Ordinal) == false;
                if (sinBcrypt)
                    _log.LogWarning("Inicio de sesión rechazado: la cuenta {IdCuenta} tiene la clave guardada sin BCrypt.", cuenta.IdCuenta);

                // Cada clave incorrecta de una cuenta que existe queda registrada: son pocas, porque a los fallos seguidos los corta el bloqueo.
                await RegistrarFalloAsync(cuenta, null, sinBcrypt ? "clave_sin_bcrypt" : "clave_incorrecta", agregable: false, ct: ct);
                var bloqueada = await _bloqueo.RegistrarFalloAsync(cuenta.IdCuenta, ct);
                if (bloqueada != null)
                    _log.LogWarning("La cuenta {IdCuenta} quedó bloqueada hasta {Hasta:u} por fallos de clave seguidos.", cuenta.IdCuenta, bloqueada);
                return new ResultadoDeCredenciales(FalloDeCredenciales.Incorrectas);
            }

            // Una cuenta que no está activa no entra aunque la clave sea correcta, y se ve igual que una clave errónea.
            if (cuenta.Estado != EstadoCuenta.Activa)
            {
                _log.LogWarning("Inicio de sesión rechazado: la cuenta {IdCuenta} está {Estado}.", cuenta.IdCuenta, cuenta.Estado);
                await RegistrarFalloAsync(cuenta, null, "cuenta_" + cuenta.Estado, agregable: false, ct: ct);
                return new ResultadoDeCredenciales(FalloDeCredenciales.Incorrectas);
            }

            await _bloqueo.LimpiarAsync(cuenta.IdCuenta, ct);

            // Con qué nombre queda en la auditoría: el usuario con el que entró, o el nombre de usuario de la cuenta si entró con el correo.
            var escrito = (identificador ?? string.Empty).Trim();
            var nombre = escrito.Contains('@') ? (cuenta.NombreUsuario ?? cuenta.Correo) : escrito;
            return new ResultadoDeCredenciales(FalloDeCredenciales.Ninguno, cuenta, nombre);
        }

        /// <summary>Un inicio de sesión que no entra, con el motivo. La auditoría es de mejor esfuerzo: nunca cambia lo que la persona ve.</summary>
        private Task RegistrarFalloAsync(Cuenta? cuenta, string? usuarioEnmascarado, string motivo, bool agregable, CancellationToken ct)
            => _auditoria == null ? Task.CompletedTask : _auditoria.RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada)
            {
                Resultado = ResultadosDeEvento.Fallo,
                IdCuenta = cuenta?.IdCuenta,
                Usuario = cuenta != null ? (cuenta.NombreUsuario ?? EnmascaradoDeAuditoria.Identificador(cuenta.Correo)) : usuarioEnmascarado,
                EsOperador = cuenta?.EsOperador ?? false,
                Agregable = agregable,
                ClaveDeAgregado = motivo,
                Detalle = new { motivo },
            }, ct);

        private bool ClaveCorrecta(string clave, string hash)
        {
            if (string.IsNullOrEmpty(hash) || !hash.StartsWith("$2", StringComparison.Ordinal)) return false;
            try { return _claves.VerifyPassword(clave, hash); }
            catch { return false; }   // hash inválido
        }

        private void SimularVerificacion(string clave)
        {
            try { _claves.VerifyPassword(clave ?? string.Empty, HashFalso.Value); } catch { /* da igual */ }
        }

        public async Task<ExigenciaDeMfa> ExigenciaDeMfaAsync(Cuenta cuenta, ContextosDeLaCuenta contextos, CancellationToken ct = default)
        {
            if (await _mfa.EstaActivaAsync(cuenta.IdCuenta, ct)) return ExigenciaDeMfa.Verificar;

            // Al operador se le exige desde el primer día (D47).
            if (cuenta.EsOperador) return ExigenciaDeMfa.Alta;

            var esAdminDeEmpresa = contextos.Contextos.Any(c => !c.EsPlataforma && c.RolLegado == AuthHelper.EmpresaAdminRole);
            if (!esAdminDeEmpresa) return ExigenciaDeMfa.NoRequerida;

            // Al administrador de empresa se le exige al terminar su ventana de gracia; antes, solo se le avisa.
            var hoy = DateOnly.FromDateTime(_tiempo.GetUtcNow().UtcDateTime);
            return _opciones.FechaMfaAdminTenant is { } desde && hoy >= desde ? ExigenciaDeMfa.Alta : ExigenciaDeMfa.Aviso;
        }

        public DatosDeIdentidad ConstruirIdentidad(Cuenta cuenta, ContextoDeAcceso contexto, string nombreEscrito, bool mfaVerificada)
        {
            var nombre = string.IsNullOrWhiteSpace(nombreEscrito) ? (cuenta.NombreUsuario ?? cuenta.Correo) : nombreEscrito;
            var sello = ReclamosDeIdentidad.CalcularSello(cuenta.Estado, cuenta.EsOperador, cuenta.DebeCambiarClave,
                contexto.EsPlataforma ? null : EstadoMembresia.Activa, contexto.EsPlataforma ? null : contexto.IdPersona,
                contexto.EsPlataforma ? null : contexto.RolLegado, contexto.EsPlataforma ? null : contexto.IdRolEmpresa, true);

            return new DatosDeIdentidad(
                cuenta.IdCuenta, cuenta.IdUserLegado, nombre, cuenta.Correo, contexto.NombrePersona, contexto.Rol, cuenta.EsOperador,
                cuenta.DebeCambiarClave, contexto.IdMembresia, contexto.IdTenant, contexto.CodigoTenant, contexto.IdEmpresa,
                contexto.IdRolEmpresa, mfaVerificada, sello, EstilosDeMenu.Normalizar(cuenta.EstiloMenu));
        }

        public async Task EntrarAsync(HttpContext http, Cuenta cuenta, DatosDeIdentidad datos, CancellationToken ct = default)
        {
            // Si ya había una cuenta con sesión, es un cambio de sitio y no una entrada nueva.
            var esCambio = AuthHelper.HayCuenta(http);

            var principal = ReclamosDeIdentidad.Crear(datos);
            await http.SignInAsync(ReclamosDeIdentidad.Esquema, principal, new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
            http.User = principal;

            cuenta.FechaUltimoAcceso = _tiempo.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);

            // El espejo de la sesión se rehace con el sitio elegido; el tenant de soporte de una sesión anterior no se hereda.
            SesionDesdeIdentidadMiddleware.Vaciar(http.Session);
            AuthHelper.SalirDeSoporte(http);
            _log.LogInformation("Inicio de sesión: cuenta {IdCuenta} en {Sitio} (rol {Rol}).", cuenta.IdCuenta,
                datos.IdMembresia == null ? "plataforma" : $"tenant {datos.IdTenant}/empresa {datos.IdEmpresa}", datos.Rol);

            // La cookie todavía no lleva el sitio nuevo para esta petición: el evento trae el suyo (ADR-030).
            if (_auditoria != null)
            {
                var sitio = datos.IdMembresia == null ? "plataforma" : "tenant";
                if (!esCambio)
                    await _auditoria.RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada)
                    {
                        IdCuenta = cuenta.IdCuenta, Usuario = datos.NombreUsuario, EsOperador = cuenta.EsOperador,
                        IdTenant = datos.IdTenant, IdEmpresa = datos.IdEmpresa,
                        Detalle = new { sitio, rol = datos.Rol, mfa = datos.MfaVerificada },
                    }, ct);

                await _auditoria.RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.ContextoElegido)
                {
                    IdCuenta = cuenta.IdCuenta, Usuario = datos.NombreUsuario, EsOperador = cuenta.EsOperador,
                    IdTenant = datos.IdTenant, IdEmpresa = datos.IdEmpresa,
                    Detalle = new { sitio, rol = datos.Rol, cambio = esCambio },
                }, ct);
            }
        }

        public async Task SalirAsync(HttpContext http)
        {
            // Con la cookie todavía puesta, el evento sabe quién sale y de qué sitio.
            if (_auditoria != null && AuthHelper.HayCuenta(http))
                await _auditoria.RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionSalida), http.RequestAborted);

            await http.SignOutAsync(ReclamosDeIdentidad.Esquema);
            http.Session.Clear();
        }
    }
}
