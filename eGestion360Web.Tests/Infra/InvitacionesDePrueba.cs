using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Services;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Personas;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Tests.Infra
{
    /// <summary>Validador de la cookie que no hace nada: las invitaciones no dependen de él.</summary>
    public sealed class ValidadorDeIdentidadInerte : IValidadorDeIdentidad
    {
        public Task<ResultadoDeValidacion> ValidarAsync(ClaimsPrincipal principal, CancellationToken ct = default) => Task.FromResult(ResultadoDeValidacion.SinCambios);
        public void Invalidar(int idCuenta) { }
    }

    /// <summary>BCrypt de costo bajo: las pruebas no esperan lo que espera producción.</summary>
    public sealed class ClavesRapidas : IPasswordService
    {
        public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, 4);
        public bool VerifyPassword(string password, string hashedPassword) => BCrypt.Net.BCrypt.Verify(password, hashedPassword);
    }

    /// <summary>Correo que guarda lo que se le pidió enviar y responde lo que la prueba disponga.</summary>
    public sealed class CorreoEspia : IEmailService
    {
        public bool Responde { get; set; } = true;
        public List<(string Para, string Asunto, string Html)> Enviados { get; } = new();
        public Task<bool> SendPasswordResetCodeAsync(string email, string username, string code) => Task.FromResult(true);
        public Task<bool> SendPasswordResetConfirmationAsync(string email, string username) => Task.FromResult(true);
        public Task<bool> SendTestEmailAsync(string toEmail, string subject, string message) => Task.FromResult(true);
        public Task<bool> SendHtmlEmailAsync(string toEmail, string subject, string htmlContent)
        {
            Enviados.Add((toEmail, subject, htmlContent));
            return Task.FromResult(Responde);
        }
    }

    /// <summary>Plan con o sin cupo de usuarios, según la prueba.</summary>
    public sealed class DerechosConTope : IDerechosDeUso
    {
        public bool Permite { get; set; } = true;
        public Task<DerechosDelTenant?> DelTenantAsync(int idTenant, CancellationToken ct = default) => Task.FromResult<DerechosDelTenant?>(null);
        public Task<IReadOnlySet<string>> ModulosActivosDeLaEmpresaAsync(int idEmpresa, CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<ResultadoDeLimite> PuedeAgregarAsync(int idTenant, string codigoDeLimite, CancellationToken ct = default)
            => Task.FromResult(new ResultadoDeLimite(Permite, Permite ? null : 3, Permite ? 0 : 3));
        public void Invalidar(int idTenant, int? idEmpresa = null) { }
    }

    public static class InvitacionesDePrueba
    {
        public static SincronizadorDeCuentas Sincronizador(ApplicationDbContext db, TimeProvider reloj)
            => new(db, IdentidadDePrueba.Tenant(), new ValidadorDeIdentidadInerte(), NullLogger<SincronizadorDeCuentas>.Instance, reloj);

        /// <summary>El servicio real que vincula usuarios con personas, ya conectado a las cuentas globales.</summary>
        public static UsuarioPersonaService Personas(ApplicationDbContext db, TimeProvider reloj)
            => new(db,
                new PersonaValidacionService(db, Options.Create(new PersonaValidacionOptions()), reloj),
                Options.Create(new PlataformaOptions { IdEmpresaPropia = DatosBase.EmpresaA }),
                reloj, NullLogger<UsuarioPersonaService>.Instance, Sincronizador(db, reloj));

        /// <summary>El servicio real de invitaciones sobre la base de prueba, con los servicios reales de personas y de cuentas.</summary>
        /// <param name="contextoFijable">
        /// Un <see cref="TenantContext"/> real, para ver a qué tenant queda fijado. Sin él, la petición es del tenant
        /// <paramref name="idTenantDeLaPeticion"/> y no se puede fijar otro.
        /// </param>
        public static ServicioDeInvitaciones Servicio(ApplicationDbContext db, TimeProvider reloj, IEmailService correo, IDerechosDeUso? derechos,
            int idTenantDeLaPeticion = DatosBase.TenantA, TenantContext? contextoFijable = null)
            => new(db, Personas(db, reloj), Sincronizador(db, reloj), new ClavesRapidas(), correo,
                contextoFijable != null ? contextoFijable : new TenantContextFijo { IdTenant = idTenantDeLaPeticion }, IdentidadDePrueba.Opciones(),
                NullLogger<ServicioDeInvitaciones>.Instance, derechos, reloj, contextoFijable);
    }
}
