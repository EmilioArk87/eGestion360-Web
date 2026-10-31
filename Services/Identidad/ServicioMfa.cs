using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OtpNet;
using QRCoder;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>Lo que ve la persona al activar la verificación en dos pasos.</summary>
    /// <param name="Secreto">La clave en texto (base 32), por si no puede escanear el QR.</param>
    /// <param name="UriOtp">La dirección <c>otpauth://</c> que lee la aplicación autenticadora.</param>
    /// <param name="QrSvg">El QR de la dirección, como imagen SVG lista para mostrar.</param>
    public sealed record EnrolamientoMfa(string Secreto, string UriOtp, string QrSvg);

    /// <summary>Verificación en dos pasos con códigos de un solo uso por tiempo (TOTP, RFC 6238) y códigos de recuperación (D22, D47).</summary>
    public interface IServicioMfa
    {
        /// <summary>True si la cuenta ya activó la verificación en dos pasos (probó un código de su aplicación).</summary>
        Task<bool> EstaActivaAsync(int idCuenta, CancellationToken ct = default);

        /// <summary>
        /// Genera un secreto y lo guarda sin confirmar; si ya había uno a medias, lo reutiliza. Falla si la cuenta ya tiene la
        /// verificación activa.
        /// </summary>
        Task<EnrolamientoMfa> IniciarEnrolamientoAsync(int idCuenta, string correo, CancellationToken ct = default);

        /// <summary>
        /// Confirma el alta con un código de la aplicación. Si es correcto, activa la verificación y devuelve los códigos de recuperación
        /// (en claro, esta única vez); si no, <c>null</c> y la cuenta sigue sin verificación activa.
        /// </summary>
        Task<IReadOnlyList<string>?> ConfirmarEnrolamientoAsync(int idCuenta, string codigo, CancellationToken ct = default);

        /// <summary>
        /// Comprueba un código de la aplicación (con una ventana de 30 s hacia cada lado; el mismo código no sirve dos veces) o, si no
        /// lo es, un código de recuperación sin usar, que queda gastado.
        /// </summary>
        Task<bool> VerificarAsync(int idCuenta, string codigo, CancellationToken ct = default);

        /// <summary>Quita la verificación en dos pasos de la cuenta (secreto y códigos). Lo usa quien administra, no la propia persona.</summary>
        Task DesactivarAsync(int idCuenta, string quien, CancellationToken ct = default);
    }

    public sealed class ServicioMfa : IServicioMfa
    {
        public const string PropositoDelSecreto = "eGestion360.Mfa.Secreto.v1";
        public const int CodigosDeRecuperacion = 10;

        // Sin 0, O, 1 ni I: se leen y se escriben a mano.
        private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly ApplicationDbContext _db;
        private readonly IDataProtector _protector;
        private readonly OpcionesDeIdentidad _opciones;
        private readonly TimeProvider _tiempo;

        public ServicioMfa(ApplicationDbContext db, IDataProtectionProvider proteccion, IOptions<OpcionesDeIdentidad> opciones, TimeProvider? tiempo = null)
        {
            _db = db;
            _protector = proteccion.CreateProtector(PropositoDelSecreto);
            _opciones = opciones.Value;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        private DateTime Ahora => _tiempo.GetUtcNow().UtcDateTime;

        public Task<bool> EstaActivaAsync(int idCuenta, CancellationToken ct = default)
            => _db.CuentaMfa.AsNoTracking().AnyAsync(m => m.IdCuenta == idCuenta && m.Confirmada, ct);

        public async Task<EnrolamientoMfa> IniciarEnrolamientoAsync(int idCuenta, string correo, CancellationToken ct = default)
        {
            var fila = await _db.CuentaMfa.FirstOrDefaultAsync(m => m.IdCuenta == idCuenta, ct);
            if (fila is { Confirmada: true })
                throw new InvalidOperationException("La cuenta ya tiene la verificación en dos pasos activa.");

            // Un alta a medias (secreto guardado pero sin confirmar) se reutiliza: recargar la página o equivocarse con el código no debe
            // invalidar el QR que la persona ya escaneó.
            string secreto;
            if (fila != null)
            {
                secreto = Desproteger(fila);
            }
            else
            {
                secreto = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
                _db.CuentaMfa.Add(new CuentaMfa
                {
                    IdCuenta = idCuenta, SecretoProtegido = _protector.Protect(secreto), Confirmada = false,
                    CreadoPor = correo, FechaCreacion = Ahora
                });
                await _db.SaveChangesAsync(ct);
            }

            var uri = UriDeAlta(_opciones.Emisor, correo, secreto);
            return new EnrolamientoMfa(secreto, uri, QrComoSvg(uri));
        }

        public async Task<IReadOnlyList<string>?> ConfirmarEnrolamientoAsync(int idCuenta, string codigo, CancellationToken ct = default)
        {
            var fila = await _db.CuentaMfa.FirstOrDefaultAsync(m => m.IdCuenta == idCuenta, ct);
            if (fila == null || fila.Confirmada) return null;

            if (!TryVerificarTotp(Desproteger(fila), codigo, fila.UltimoPasoUsado, out var paso)) return null;

            var ahora = Ahora;
            fila.Confirmada = true;
            fila.FechaConfirmacion = ahora;
            fila.UltimoPasoUsado = paso;
            fila.FechaModificacion = ahora;

            // Códigos de recuperación nuevos: se reemplazan los que hubiera de un alta anterior.
            var anteriores = await _db.CuentaMfaRecuperacion.Where(r => r.IdCuenta == idCuenta).ToListAsync(ct);
            _db.CuentaMfaRecuperacion.RemoveRange(anteriores);

            var codigos = new List<string>(CodigosDeRecuperacion);
            for (var i = 0; i < CodigosDeRecuperacion; i++)
            {
                var nuevo = NuevoCodigoDeRecuperacion();
                codigos.Add(nuevo);
                _db.CuentaMfaRecuperacion.Add(new CuentaMfaRecuperacion { IdCuenta = idCuenta, HashCodigo = Huella(nuevo), FechaCreacion = ahora });
            }

            await _db.SaveChangesAsync(ct);
            return codigos;
        }

        public async Task<bool> VerificarAsync(int idCuenta, string codigo, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return false;

            var fila = await _db.CuentaMfa.FirstOrDefaultAsync(m => m.IdCuenta == idCuenta && m.Confirmada, ct);
            if (fila == null) return false;

            var soloDigitos = new string(codigo.Where(char.IsDigit).ToArray());
            if (soloDigitos.Length == 6 && TryVerificarTotp(Desproteger(fila), soloDigitos, fila.UltimoPasoUsado, out var paso))
            {
                fila.UltimoPasoUsado = paso;
                fila.FechaModificacion = Ahora;
                await _db.SaveChangesAsync(ct);
                return true;
            }

            // Código de recuperación: de un solo uso.
            var huella = Huella(codigo);
            var recuperacion = await _db.CuentaMfaRecuperacion
                .FirstOrDefaultAsync(r => r.IdCuenta == idCuenta && r.HashCodigo == huella && r.UsadoEn == null, ct);
            if (recuperacion == null) return false;

            recuperacion.UsadoEn = Ahora;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task DesactivarAsync(int idCuenta, string quien, CancellationToken ct = default)
        {
            _db.CuentaMfaRecuperacion.RemoveRange(await _db.CuentaMfaRecuperacion.Where(r => r.IdCuenta == idCuenta).ToListAsync(ct));
            _db.CuentaMfa.RemoveRange(await _db.CuentaMfa.Where(m => m.IdCuenta == idCuenta).ToListAsync(ct));
            await _db.SaveChangesAsync(ct);
        }

        // ── Ayudas ────────────────────────────────────────────────────────────

        private string Desproteger(CuentaMfa fila) => _protector.Unprotect(fila.SecretoProtegido);

        private bool TryVerificarTotp(string secretoBase32, string codigo, long? ultimoPaso, out long paso)
        {
            paso = 0;
            var totp = new Totp(Base32Encoding.ToBytes(secretoBase32));
            if (!totp.VerifyTotp(Ahora, codigo, out var coincidente, new VerificationWindow(previous: 1, future: 1))) return false;
            // El mismo paso (o uno anterior) no sirve otra vez: un código espiado no se puede reutilizar.
            if (ultimoPaso is { } usado && coincidente <= usado) return false;
            paso = coincidente;
            return true;
        }

        /// <summary>La dirección que lee la aplicación autenticadora (formato Google Authenticator).</summary>
        public static string UriDeAlta(string emisor, string correo, string secretoBase32)
            => $"otpauth://totp/{Uri.EscapeDataString(emisor)}:{Uri.EscapeDataString(correo)}?secret={secretoBase32}&issuer={Uri.EscapeDataString(emisor)}&algorithm=SHA1&digits=6&period=30";

        private static string QrComoSvg(string uri)
        {
            using var generador = new QRCodeGenerator();
            using var datos = generador.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
            return new SvgQRCode(datos).GetGraphic(5);
        }

        private static string NuevoCodigoDeRecuperacion()
        {
            Span<char> c = stackalloc char[11];
            for (var i = 0; i < c.Length; i++)
                c[i] = i == 5 ? '-' : Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
            return new string(c);
        }

        /// <summary>SHA-256 en hexadecimal del código normalizado (mayúsculas, sin guiones ni espacios).</summary>
        public static string Huella(string codigo)
        {
            var normalizado = new string(codigo.Where(ch => ch != '-' && !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizado))).ToLowerInvariant();
        }
    }
}
