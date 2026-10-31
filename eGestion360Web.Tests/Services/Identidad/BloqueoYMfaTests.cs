using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using eGestion360Web.Data;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Identidad
{
    /// <summary>Bloqueo temporal tras fallos de clave seguidos (D46).</summary>
    public class BloqueoDeCuentasTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();

        public void Dispose() => _bd.Dispose();

        private (ApplicationDbContext Db, BloqueoDeCuentas Servicio, int IdCuenta) Preparar(Action<OpcionesDeIdentidad>? ajustar = null)
        {
            var db = _bd.Crear();
            var cuenta = IdentidadDePrueba.NuevaCuenta(db, $"{Guid.NewGuid():N}@prueba.local");
            return (db, new BloqueoDeCuentas(db, IdentidadDePrueba.Opciones(ajustar), _reloj), cuenta.IdCuenta);
        }

        [Fact]
        public async Task Cuatro_fallos_no_bloquean_y_el_quinto_si()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;

            for (var i = 1; i <= 4; i++) Assert.Null(await servicio.RegistrarFalloAsync(id));
            Assert.Null(await servicio.BloqueadaHastaAsync(id));

            var hasta = await servicio.RegistrarFalloAsync(id);

            Assert.Equal(_reloj.GetUtcNow().UtcDateTime.AddMinutes(15), hasta);
            Assert.Equal(hasta, await servicio.BloqueadaHastaAsync(id));
        }

        [Fact]
        public async Task El_bloqueo_se_levanta_solo_a_los_quince_minutos()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            for (var i = 0; i < 5; i++) await servicio.RegistrarFalloAsync(id);
            Assert.NotNull(await servicio.BloqueadaHastaAsync(id));

            _reloj.Avanzar(TimeSpan.FromMinutes(14));
            Assert.NotNull(await servicio.BloqueadaHastaAsync(id));

            _reloj.Avanzar(TimeSpan.FromMinutes(2));
            Assert.Null(await servicio.BloqueadaHastaAsync(id));
        }

        [Fact]
        public async Task Tras_el_bloqueo_se_cuenta_de_cero_otra_vez()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            for (var i = 0; i < 5; i++) await servicio.RegistrarFalloAsync(id);
            _reloj.Avanzar(TimeSpan.FromMinutes(16));

            // Cuatro fallos nuevos no vuelven a bloquear: los cinco primeros ya se pagaron.
            for (var i = 0; i < 4; i++) Assert.Null(await servicio.RegistrarFalloAsync(id));
            Assert.NotNull(await servicio.RegistrarFalloAsync(id));
        }

        [Fact]
        public async Task Un_acierto_limpia_el_contador()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            for (var i = 0; i < 4; i++) await servicio.RegistrarFalloAsync(id);

            await servicio.LimpiarAsync(id);

            for (var i = 0; i < 4; i++) Assert.Null(await servicio.RegistrarFalloAsync(id));
        }

        [Fact]
        public async Task Limpiar_levanta_un_bloqueo_vigente()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            for (var i = 0; i < 5; i++) await servicio.RegistrarFalloAsync(id);

            await servicio.LimpiarAsync(id);

            Assert.Null(await servicio.BloqueadaHastaAsync(id));
        }

        [Fact]
        public async Task Los_topes_salen_de_las_opciones()
        {
            var (db, servicio, id) = Preparar(o => { o.IntentosParaBloquear = 2; o.MinutosDeBloqueo = 1; });
            using var _ = db;

            Assert.Null(await servicio.RegistrarFalloAsync(id));
            var hasta = await servicio.RegistrarFalloAsync(id);

            Assert.Equal(_reloj.GetUtcNow().UtcDateTime.AddMinutes(1), hasta);
        }

        [Fact]
        public async Task Una_cuenta_sin_fallos_no_esta_bloqueada_ni_deja_fila_al_limpiar()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;

            Assert.Null(await servicio.BloqueadaHastaAsync(id));
            await servicio.LimpiarAsync(id);

            Assert.False(db.CuentaIntentos.Any());
        }
    }

    /// <summary>Verificación en dos pasos: alta, código por tiempo, códigos de recuperación (D22, D47).</summary>
    public class ServicioMfaTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly EphemeralDataProtectionProvider _proteccion = new();

        public void Dispose() => _bd.Dispose();

        private (ApplicationDbContext Db, ServicioMfa Servicio, int IdCuenta) Preparar()
        {
            var db = _bd.Crear();
            var cuenta = IdentidadDePrueba.NuevaCuenta(db, $"{Guid.NewGuid():N}@prueba.local");
            return (db, new ServicioMfa(db, _proteccion, IdentidadDePrueba.Opciones(), _reloj), cuenta.IdCuenta);
        }

        private string CodigoDe(string secreto, DateTime? en = null)
            => new Totp(Base32Encoding.ToBytes(secreto)).ComputeTotp(en ?? _reloj.GetUtcNow().UtcDateTime);

        [Fact]
        public async Task El_alta_entrega_el_secreto_la_direccion_y_el_QR_y_no_activa_nada_todavia()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;

            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");

            Assert.NotEmpty(alta.Secreto);
            Assert.StartsWith("otpauth://totp/eGestion360:emilio%40prueba.local?secret=" + alta.Secreto, alta.UriOtp);
            Assert.Contains("issuer=eGestion360", alta.UriOtp);
            Assert.Contains("<svg", alta.QrSvg);
            Assert.False(await servicio.EstaActivaAsync(id));
        }

        [Fact]
        public async Task El_secreto_se_guarda_protegido_nunca_en_claro()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;

            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");

            var guardado = db.CuentaMfa.Single(m => m.IdCuenta == id).SecretoProtegido;
            Assert.DoesNotContain(alta.Secreto, guardado);
            Assert.Equal(alta.Secreto, _proteccion.CreateProtector(ServicioMfa.PropositoDelSecreto).Unprotect(guardado));
        }

        [Fact]
        public async Task Un_alta_a_medias_se_reutiliza_para_no_invalidar_el_QR_ya_escaneado()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;

            var primera = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");
            var segunda = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");

            Assert.Equal(primera.Secreto, segunda.Secreto);
            Assert.Single(db.CuentaMfa);
        }

        [Fact]
        public async Task Confirmar_con_un_codigo_correcto_activa_y_entrega_diez_codigos_de_recuperacion_distintos()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");

            var codigos = await servicio.ConfirmarEnrolamientoAsync(id, CodigoDe(alta.Secreto));

            Assert.NotNull(codigos);
            Assert.Equal(10, codigos!.Count);
            Assert.Equal(10, codigos.Distinct().Count());
            Assert.All(codigos, c => Assert.Matches(@"^[A-HJ-NP-Z2-9]{5}-[A-HJ-NP-Z2-9]{5}$", c));
            Assert.True(await servicio.EstaActivaAsync(id));
            Assert.NotNull(db.CuentaMfa.Single().FechaConfirmacion);
        }

        [Fact]
        public async Task Los_codigos_de_recuperacion_se_guardan_solo_como_hash()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");
            var codigos = (await servicio.ConfirmarEnrolamientoAsync(id, CodigoDe(alta.Secreto)))!;

            var guardados = db.CuentaMfaRecuperacion.Select(r => r.HashCodigo).ToList();

            Assert.Equal(10, guardados.Count);
            Assert.All(guardados, h => Assert.Matches("^[0-9a-f]{64}$", h));
            Assert.All(codigos, c => Assert.DoesNotContain(c.Replace("-", ""), string.Join(' ', guardados), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(ServicioMfa.Huella(codigos[0]), guardados);
        }

        [Fact]
        public async Task Confirmar_con_un_codigo_equivocado_no_activa_nada()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");

            Assert.Null(await servicio.ConfirmarEnrolamientoAsync(id, "000000"));

            Assert.False(await servicio.EstaActivaAsync(id));
            Assert.Empty(db.CuentaMfaRecuperacion);
        }

        [Fact]
        public async Task Un_codigo_de_la_aplicacion_sirve_una_sola_vez()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");
            await servicio.ConfirmarEnrolamientoAsync(id, CodigoDe(alta.Secreto));

            // El código con el que se confirmó el alta ya se usó: espiarlo no sirve para entrar.
            Assert.False(await servicio.VerificarAsync(id, CodigoDe(alta.Secreto)));

            _reloj.Avanzar(TimeSpan.FromSeconds(30));
            var siguiente = CodigoDe(alta.Secreto);
            Assert.True(await servicio.VerificarAsync(id, siguiente));
            Assert.False(await servicio.VerificarAsync(id, siguiente));
        }

        [Fact]
        public async Task Se_acepta_el_codigo_del_paso_anterior_y_del_siguiente_pero_no_uno_mas_lejano()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");
            await servicio.ConfirmarEnrolamientoAsync(id, CodigoDe(alta.Secreto));
            _reloj.Avanzar(TimeSpan.FromMinutes(5));
            var ahora = _reloj.GetUtcNow().UtcDateTime;

            Assert.False(await servicio.VerificarAsync(id, CodigoDe(alta.Secreto, ahora.AddSeconds(-90))));
            Assert.False(await servicio.VerificarAsync(id, CodigoDe(alta.Secreto, ahora.AddSeconds(90))));
            Assert.True(await servicio.VerificarAsync(id, CodigoDe(alta.Secreto, ahora.AddSeconds(-30))));
        }

        [Fact]
        public async Task Un_codigo_de_recuperacion_sirve_una_sola_vez_sin_importar_guiones_ni_mayusculas()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");
            var codigos = (await servicio.ConfirmarEnrolamientoAsync(id, CodigoDe(alta.Secreto)))!;

            var escrito = codigos[3].Replace("-", " ").ToLowerInvariant();
            Assert.True(await servicio.VerificarAsync(id, escrito));
            Assert.False(await servicio.VerificarAsync(id, codigos[3]));
            Assert.True(await servicio.VerificarAsync(id, codigos[4]));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("123456")]
        [InlineData("ZZZZZ-ZZZZZ")]
        public async Task Un_codigo_cualquiera_no_entra(string codigo)
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");
            await servicio.ConfirmarEnrolamientoAsync(id, CodigoDe(alta.Secreto));

            Assert.False(await servicio.VerificarAsync(id, codigo));
        }

        [Fact]
        public async Task Sin_la_verificacion_activa_no_se_acepta_ningun_codigo()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");

            // Sin confirmar, ni siquiera un código válido de la aplicación vale para entrar.
            Assert.False(await servicio.VerificarAsync(id, CodigoDe(alta.Secreto)));
        }

        [Fact]
        public async Task Una_cuenta_con_la_verificacion_activa_no_puede_volver_a_darse_de_alta()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");
            await servicio.ConfirmarEnrolamientoAsync(id, CodigoDe(alta.Secreto));

            await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local"));
        }

        [Fact]
        public async Task Desactivar_quita_el_secreto_y_los_codigos()
        {
            var (db, servicio, id) = Preparar();
            using var _ = db;
            var alta = await servicio.IniciarEnrolamientoAsync(id, "emilio@prueba.local");
            await servicio.ConfirmarEnrolamientoAsync(id, CodigoDe(alta.Secreto));

            await servicio.DesactivarAsync(id, "admin");

            Assert.False(await servicio.EstaActivaAsync(id));
            Assert.Empty(db.CuentaMfa);
            Assert.Empty(db.CuentaMfaRecuperacion);
        }

        [Fact]
        public void Los_vectores_oficiales_de_TOTP_de_la_RFC_6238_dan_el_mismo_codigo()
        {
            // RFC 6238, apéndice B (SHA-1, secreto "12345678901234567890"): 59 s -> 94287082, 1111111109 s -> 07081804.
            var secreto = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
            var totp = new Totp(secreto, step: 30, mode: OtpHashMode.Sha1, totpSize: 8);

            Assert.Equal("94287082", totp.ComputeTotp(DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime));
            Assert.Equal("07081804", totp.ComputeTotp(DateTimeOffset.FromUnixTimeSeconds(1111111109).UtcDateTime));
        }
    }
}
