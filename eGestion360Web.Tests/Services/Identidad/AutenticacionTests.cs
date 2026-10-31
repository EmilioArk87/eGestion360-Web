using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OtpNet;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Identidad
{
    /// <summary>Dónde puede trabajar una cuenta y con qué nombre se la busca para entrar (F1.5, D44).</summary>
    public class DirectorioDeCuentasTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly ApplicationDbContext _db;
        private readonly DirectorioDeCuentas _directorio;

        public DirectorioDeCuentasTests()
        {
            _db = _bd.Crear();
            _directorio = new DirectorioDeCuentas(_db, IdentidadDePrueba.Tenant());
        }

        public void Dispose()
        {
            _db.Dispose();
            _bd.Dispose();
        }

        // ── Buscar para entrar ────────────────────────────────────────────────

        [Fact]
        public async Task Se_encuentra_por_correo_por_usuario_y_por_un_alias_que_puede_entrar()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "emilio@prueba.local", usuario: "admin");
            _db.CuentaAlias.Add(new CuentaAlias { IdCuenta = cuenta.IdCuenta, Alias = "egaray", Tipo = TipoAlias.Usuario, PuedeIniciarSesion = true, CreadoPor = "p", FechaCreacion = IdentidadDePrueba.Ahora });
            _db.CuentaAlias.Add(new CuentaAlias { IdCuenta = cuenta.IdCuenta, Alias = "otro@prueba.local", Tipo = TipoAlias.Correo, PuedeIniciarSesion = false, CreadoPor = "p", FechaCreacion = IdentidadDePrueba.Ahora });
            _db.SaveChanges();

            Assert.Equal(cuenta.IdCuenta, (await _directorio.BuscarParaEntrarAsync("emilio@prueba.local"))?.IdCuenta);
            Assert.Equal(cuenta.IdCuenta, (await _directorio.BuscarParaEntrarAsync("admin"))?.IdCuenta);
            Assert.Equal(cuenta.IdCuenta, (await _directorio.BuscarParaEntrarAsync("  egaray  "))?.IdCuenta);
        }

        [Fact]
        public async Task Un_correo_secundario_no_inicia_sesion_y_lo_desconocido_no_existe()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "emilio@prueba.local", usuario: "admin");
            _db.CuentaAlias.Add(new CuentaAlias { IdCuenta = cuenta.IdCuenta, Alias = "otro@prueba.local", Tipo = TipoAlias.Correo, PuedeIniciarSesion = false, CreadoPor = "p", FechaCreacion = IdentidadDePrueba.Ahora });
            _db.SaveChanges();

            Assert.Null(await _directorio.BuscarParaEntrarAsync("otro@prueba.local"));
            Assert.Null(await _directorio.BuscarParaEntrarAsync("nadie"));
            Assert.Null(await _directorio.BuscarParaEntrarAsync("   "));
            Assert.Null(await _directorio.BuscarParaEntrarAsync(""));
        }

        // ── Contextos ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Una_cuenta_de_empresa_trabaja_en_su_empresa_con_su_rol_y_su_persona()
        {
            var (cuenta, membresia, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local", idEmpresa: DatosBase.EmpresaA, rolLegado: "empresa_admin");

            var r = await _directorio.ContextosAsync(cuenta);

            var c = Assert.Single(r.Contextos);
            Assert.Equal($"{membresia.IdMembresia}:{DatosBase.EmpresaA}", c.Clave);
            Assert.False(c.EsPlataforma);
            Assert.Equal("empresa_admin", c.Rol);
            Assert.Equal(DatosBase.TenantA, c.IdTenant);
            Assert.Equal("TENANT_A", c.CodigoTenant);
            Assert.Equal("Empresa de prueba 1", c.NombreEmpresa);
            Assert.Equal("Emilio Garay", c.NombrePersona);
            Assert.Equal(0, r.ExcluidosPorFaltaDePersona);
        }

        [Fact]
        public async Task El_rol_de_la_empresa_se_muestra_por_su_nombre()
        {
            var (cuenta, _, acceso) = IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local");
            var rol = new EmpresaRol { IdEmpresa = DatosBase.EmpresaA, Nombre = "Flota" };
            _db.EmpresaRoles.Add(rol);
            _db.SaveChanges();
            acceso.IdRol = rol.IdRol;
            _db.SaveChanges();

            var c = Assert.Single((await _directorio.ContextosAsync(cuenta)).Contextos);

            Assert.Equal("Flota", c.NombreRol);
            Assert.Equal(rol.IdRol, c.IdRolEmpresa);
        }

        [Fact]
        public async Task Con_membresias_en_dos_tenants_hay_un_sitio_por_empresa_ordenado_por_tenant()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "emilio@prueba.local");
            var m1 = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantB, IdentidadDePrueba.NuevaPersona(_db, DatosBase.EmpresaB));
            var m2 = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantA, IdentidadDePrueba.NuevaPersona(_db, DatosBase.EmpresaA));
            IdentidadDePrueba.NuevoAcceso(_db, m1, DatosBase.EmpresaB);
            IdentidadDePrueba.NuevoAcceso(_db, m2, DatosBase.EmpresaA);
            IdentidadDePrueba.NuevoAcceso(_db, m2, DatosBase.EmpresaA2, principal: false);

            var r = await _directorio.ContextosAsync(cuenta);

            Assert.Equal(new[] { "TENANT_A", "TENANT_A", "TENANT_B" }, r.Contextos.Select(c => c.CodigoTenant));
            Assert.Equal(new[] { DatosBase.EmpresaA, DatosBase.EmpresaA2, DatosBase.EmpresaB }, r.Contextos.Select(c => c.IdEmpresa!.Value));
        }

        [Fact]
        public async Task Una_membresia_suspendida_un_tenant_cancelado_un_acceso_inactivo_o_una_empresa_inactiva_dejan_el_sitio_fuera()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local");
            var mSuspendida = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantA, IdentidadDePrueba.NuevaPersona(_db, DatosBase.EmpresaA), EstadoMembresia.Suspendida);
            IdentidadDePrueba.NuevoAcceso(_db, mSuspendida, DatosBase.EmpresaA);
            Assert.Empty((await _directorio.ContextosAsync(cuenta)).Contextos);

            mSuspendida.Estado = EstadoMembresia.Activa;
            _db.Tenants.Single(t => t.IdTenant == DatosBase.TenantA).Estado = EstadoTenant.Cancelado;
            _db.SaveChanges();
            Assert.Empty((await _directorio.ContextosAsync(cuenta)).Contextos);

            _db.Tenants.Single(t => t.IdTenant == DatosBase.TenantA).Estado = EstadoTenant.Activo;
            _db.AccesosEmpresa.Single(a => a.IdMembresia == mSuspendida.IdMembresia).Activo = false;
            _db.SaveChanges();
            Assert.Empty((await _directorio.ContextosAsync(cuenta)).Contextos);

            _db.AccesosEmpresa.Single(a => a.IdMembresia == mSuspendida.IdMembresia).Activo = true;
            _db.Empresas.Single(e => e.IdEmpresa == DatosBase.EmpresaA).Activa = false;
            _db.SaveChanges();
            Assert.Empty((await _directorio.ContextosAsync(cuenta)).Contextos);

            _db.Empresas.Single(e => e.IdEmpresa == DatosBase.EmpresaA).Activa = true;
            _db.SaveChanges();
            Assert.Single((await _directorio.ContextosAsync(cuenta)).Contextos);
        }

        [Fact]
        public async Task Un_tenant_suspendido_si_aparece_para_que_la_persona_vea_por_que_no_puede_trabajar()
        {
            var (cuenta, _, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local", idEmpresa: DatosBase.EmpresaB);
            _db.Tenants.Single(t => t.IdTenant == DatosBase.TenantB).Estado = EstadoTenant.Suspendido;
            _db.SaveChanges();

            Assert.Single((await _directorio.ContextosAsync(cuenta)).Contextos);   // el estado lo aplica EstadoDelTenantMiddleware
        }

        [Fact]
        public async Task Sin_una_persona_vigente_el_sitio_queda_fuera_y_se_cuenta()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local");
            var sinPersona = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantA, null);
            IdentidadDePrueba.NuevoAcceso(_db, sinPersona, DatosBase.EmpresaA);

            var r = await _directorio.ContextosAsync(cuenta);

            Assert.Empty(r.Contextos);
            Assert.Equal(1, r.ExcluidosPorFaltaDePersona);
        }

        [Fact]
        public async Task Una_persona_eliminada_o_fusionada_tampoco_sirve()
        {
            var eliminada = IdentidadDePrueba.CuentaDeEmpresa(_db, "eliminada@prueba.local");
            var fusionada = IdentidadDePrueba.CuentaDeEmpresa(_db, "fusionada@prueba.local");
            var otra = IdentidadDePrueba.NuevaPersona(_db, DatosBase.EmpresaA, "Otra", "Persona");
            _db.Personas.Single(p => p.IdPersona == eliminada.Membresia.IdPersona).Eliminado = true;
            _db.Personas.Single(p => p.IdPersona == fusionada.Membresia.IdPersona).IdPersonaPrincipal = otra;
            _db.SaveChanges();

            Assert.Empty((await _directorio.ContextosAsync(eliminada.Cuenta)).Contextos);
            Assert.Equal(1, (await _directorio.ContextosAsync(fusionada.Cuenta)).ExcluidosPorFaltaDePersona);
        }

        [Fact]
        public async Task El_operador_tiene_primero_el_sitio_de_plataforma_como_admin_y_despues_sus_empresas()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "emilio@prueba.local", usuario: "admin", operador: true);
            var m = IdentidadDePrueba.NuevaMembresia(_db, cuenta, DatosBase.TenantA, IdentidadDePrueba.NuevaPersona(_db, DatosBase.EmpresaA));
            IdentidadDePrueba.NuevoAcceso(_db, m, DatosBase.EmpresaA);

            var r = await _directorio.ContextosAsync(cuenta);

            Assert.Equal(2, r.Contextos.Count);
            var plataforma = r.Contextos[0];
            Assert.True(plataforma.EsPlataforma);
            Assert.Equal(ContextoDeAcceso.ClaveDePlataforma, plataforma.Clave);
            Assert.Equal("admin", plataforma.Rol);
            Assert.Null(plataforma.IdEmpresa);
            Assert.Null(plataforma.IdTenant);
            Assert.Equal("Emilio Garay", plataforma.NombrePersona);   // la persona de su membresía
            Assert.False(r.Contextos[1].EsPlataforma);
        }

        [Fact]
        public async Task Un_operador_sin_membresias_solo_tiene_la_plataforma_y_se_llama_como_su_usuario()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "soporte@prueba.local", usuario: "soporte", operador: true);

            var c = Assert.Single((await _directorio.ContextosAsync(cuenta)).Contextos);

            Assert.True(c.EsPlataforma);
            Assert.Equal("soporte", c.NombrePersona);
        }

        [Fact]
        public async Task Leer_los_accesos_deja_la_peticion_sin_tenant()
        {
            var tenant = IdentidadDePrueba.Tenant();
            var directorio = new DirectorioDeCuentas(_db, tenant);
            var (cuenta, _, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, "ana@prueba.local", idEmpresa: DatosBase.EmpresaB);

            await directorio.ContextosAsync(cuenta);

            Assert.Null(tenant.IdTenant);   // el tenant solo se fija al elegir el sitio
        }
    }

    /// <summary>Clave, bloqueo y exigencia de segundo factor al iniciar sesión (F1.5, D46 y D47).</summary>
    public class AutenticadorTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ApplicationDbContext _db;

        public AutenticadorTests() => _db = _bd.Crear();

        public void Dispose()
        {
            _db.Dispose();
            _bd.Dispose();
        }

        private Autenticador Armar(Action<OpcionesDeIdentidad>? ajustar = null, IServicioMfa? mfa = null)
        {
            var opciones = IdentidadDePrueba.Opciones(ajustar);
            return new Autenticador(_db, new DirectorioDeCuentas(_db, IdentidadDePrueba.Tenant()), new BloqueoDeCuentas(_db, opciones, _reloj),
                mfa ?? new ServicioMfa(_db, new EphemeralDataProtectionProvider(), opciones, _reloj), new PasswordService(), opciones,
                NullLogger<Autenticador>.Instance, _reloj);
        }

        // ── Credenciales ──────────────────────────────────────────────────────

        [Fact]
        public async Task Una_clave_correcta_entra_y_el_nombre_es_el_usuario_con_el_que_escribio()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "emilio@prueba.local", "Clave-Segura-1", usuario: "admin");

            var porUsuario = await Armar().VerificarCredencialesAsync("admin", "Clave-Segura-1");
            var porCorreo = await Armar().VerificarCredencialesAsync("emilio@prueba.local", "Clave-Segura-1");

            Assert.True(porUsuario.Correctas);
            Assert.Equal(cuenta.IdCuenta, porUsuario.Cuenta!.IdCuenta);
            Assert.Equal("admin", porUsuario.NombreEscrito);
            Assert.Equal("admin", porCorreo.NombreEscrito);   // con el correo, la auditoría usa el usuario de la cuenta
        }

        [Fact]
        public async Task Con_un_alias_el_nombre_de_la_auditoria_es_ese_alias()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "emilio@prueba.local", "Clave-Segura-1", usuario: "admin");
            _db.CuentaAlias.Add(new CuentaAlias { IdCuenta = cuenta.IdCuenta, Alias = "egaray.transgar", Tipo = TipoAlias.Usuario, PuedeIniciarSesion = true, CreadoPor = "p", FechaCreacion = IdentidadDePrueba.Ahora });
            _db.SaveChanges();

            var r = await Armar().VerificarCredencialesAsync("egaray.transgar", "Clave-Segura-1");

            Assert.True(r.Correctas);
            Assert.Equal("egaray.transgar", r.NombreEscrito);
        }

        [Fact]
        public async Task Sin_usuario_nombre_de_usuario_la_auditoria_usa_el_correo()
        {
            IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", "Clave-Segura-1");

            var r = await Armar().VerificarCredencialesAsync("ana@prueba.local", "Clave-Segura-1");

            Assert.Equal("ana@prueba.local", r.NombreEscrito);
        }

        [Fact]
        public async Task Una_clave_equivocada_y_una_cuenta_inexistente_son_lo_mismo_desde_fuera()
        {
            IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", "Clave-Segura-1");

            var equivocada = await Armar().VerificarCredencialesAsync("ana@prueba.local", "otra");
            var inexistente = await Armar().VerificarCredencialesAsync("nadie@prueba.local", "Clave-Segura-1");

            Assert.Equal(FalloDeCredenciales.Incorrectas, equivocada.Fallo);
            Assert.Equal(FalloDeCredenciales.Incorrectas, inexistente.Fallo);
            Assert.Null(equivocada.Cuenta);
        }

        [Fact]
        public async Task Una_cuenta_inactiva_o_invitada_no_entra_aunque_la_clave_sea_correcta()
        {
            IdentidadDePrueba.NuevaCuenta(_db, "inactiva@prueba.local", "Clave-Segura-1", estado: EstadoCuenta.Inactiva);
            IdentidadDePrueba.NuevaCuenta(_db, "invitada@prueba.local", "Clave-Segura-1", estado: EstadoCuenta.Invitada);

            Assert.Equal(FalloDeCredenciales.Incorrectas, (await Armar().VerificarCredencialesAsync("inactiva@prueba.local", "Clave-Segura-1")).Fallo);
            Assert.Equal(FalloDeCredenciales.Incorrectas, (await Armar().VerificarCredencialesAsync("invitada@prueba.local", "Clave-Segura-1")).Fallo);
        }

        [Fact]
        public async Task Una_clave_guardada_sin_BCrypt_se_rechaza_aunque_coincida()
        {
            var cuenta = IdentidadDePrueba.NuevaCuenta(_db, "plana@prueba.local");
            cuenta.HashClave = "Clave-Plana-1";
            _db.SaveChanges();

            Assert.Equal(FalloDeCredenciales.Incorrectas, (await Armar().VerificarCredencialesAsync("plana@prueba.local", "Clave-Plana-1")).Fallo);
        }

        [Fact]
        public async Task Cinco_fallos_bloquean_la_cuenta_y_ni_la_clave_correcta_entra_hasta_que_pase_el_tiempo()
        {
            IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", "Clave-Segura-1");
            var autenticador = Armar();

            for (var i = 0; i < 5; i++) await autenticador.VerificarCredencialesAsync("ana@prueba.local", "mala");

            var bloqueada = await autenticador.VerificarCredencialesAsync("ana@prueba.local", "Clave-Segura-1");
            Assert.Equal(FalloDeCredenciales.Bloqueada, bloqueada.Fallo);
            Assert.NotNull(bloqueada.BloqueadaHasta);

            _reloj.Avanzar(TimeSpan.FromMinutes(16));
            Assert.True((await autenticador.VerificarCredencialesAsync("ana@prueba.local", "Clave-Segura-1")).Correctas);
        }

        [Fact]
        public async Task Un_acierto_limpia_los_fallos_anteriores()
        {
            IdentidadDePrueba.NuevaCuenta(_db, "ana@prueba.local", "Clave-Segura-1");
            var autenticador = Armar();
            for (var i = 0; i < 4; i++) await autenticador.VerificarCredencialesAsync("ana@prueba.local", "mala");

            Assert.True((await autenticador.VerificarCredencialesAsync("ana@prueba.local", "Clave-Segura-1")).Correctas);

            for (var i = 0; i < 4; i++) await autenticador.VerificarCredencialesAsync("ana@prueba.local", "mala");
            Assert.True((await autenticador.VerificarCredencialesAsync("ana@prueba.local", "Clave-Segura-1")).Correctas);
        }

        [Fact]
        public async Task Los_fallos_contra_una_cuenta_inexistente_no_dejan_rastro()
        {
            await Armar().VerificarCredencialesAsync("nadie@prueba.local", "mala");

            Assert.Empty(_db.CuentaIntentos);
        }

        // ── Segundo factor ────────────────────────────────────────────────────

        private async Task<(Cuenta Cuenta, ContextosDeLaCuenta Contextos)> CuentaConSitios(string rolLegado = "empresa_user", bool operador = false, string correo = "x@prueba.local")
        {
            var (cuenta, _, _) = IdentidadDePrueba.CuentaDeEmpresa(_db, correo, rolLegado: rolLegado);
            cuenta.EsOperador = operador;
            _db.SaveChanges();
            var directorio = new DirectorioDeCuentas(_db, IdentidadDePrueba.Tenant());
            return (cuenta, await directorio.ContextosAsync(cuenta));
        }

        [Fact]
        public async Task Al_operador_se_le_exige_activar_la_verificacion_desde_el_primer_dia()
        {
            var (cuenta, contextos) = await CuentaConSitios(operador: true);

            Assert.Equal(ExigenciaDeMfa.Alta, await Armar().ExigenciaDeMfaAsync(cuenta, contextos));
        }

        [Fact]
        public async Task Quien_ya_la_tiene_activa_debe_verificar_sea_cual_sea_su_rol()
        {
            var (cuenta, contextos) = await CuentaConSitios("empresa_user");
            _db.CuentaMfa.Add(new CuentaMfa { IdCuenta = cuenta.IdCuenta, SecretoProtegido = "x", Confirmada = true, FechaConfirmacion = IdentidadDePrueba.Ahora, CreadoPor = "p", FechaCreacion = IdentidadDePrueba.Ahora });
            _db.SaveChanges();

            Assert.Equal(ExigenciaDeMfa.Verificar, await Armar().ExigenciaDeMfaAsync(cuenta, contextos));
        }

        [Fact]
        public async Task Un_secreto_sin_confirmar_no_cuenta_como_activo()
        {
            var (cuenta, contextos) = await CuentaConSitios("empresa_user");
            _db.CuentaMfa.Add(new CuentaMfa { IdCuenta = cuenta.IdCuenta, SecretoProtegido = "x", Confirmada = false, CreadoPor = "p", FechaCreacion = IdentidadDePrueba.Ahora });
            _db.SaveChanges();

            Assert.Equal(ExigenciaDeMfa.NoRequerida, await Armar().ExigenciaDeMfaAsync(cuenta, contextos));
        }

        [Fact]
        public async Task Un_usuario_de_empresa_sin_verificacion_entra_sin_que_se_le_pida()
        {
            var (cuenta, contextos) = await CuentaConSitios("empresa_user");

            Assert.Equal(ExigenciaDeMfa.NoRequerida, await Armar().ExigenciaDeMfaAsync(cuenta, contextos));
        }

        [Fact]
        public async Task Al_administrador_de_empresa_solo_se_le_avisa_hasta_que_termine_su_ventana_de_gracia()
        {
            var (cuenta, contextos) = await CuentaConSitios("empresa_admin");

            // Sin fecha configurada todavía: aviso. Con fecha futura: aviso. El día de la fecha y después: se le exige.
            Assert.Equal(ExigenciaDeMfa.Aviso, await Armar().ExigenciaDeMfaAsync(cuenta, contextos));
            Assert.Equal(ExigenciaDeMfa.Aviso, await Armar(o => o.MfaObligatoriaAdminTenantDesde = "2026-10-15").ExigenciaDeMfaAsync(cuenta, contextos));
            Assert.Equal(ExigenciaDeMfa.Alta, await Armar(o => o.MfaObligatoriaAdminTenantDesde = "2026-10-01").ExigenciaDeMfaAsync(cuenta, contextos));
            Assert.Equal(ExigenciaDeMfa.Alta, await Armar(o => o.MfaObligatoriaAdminTenantDesde = "2026-09-01").ExigenciaDeMfaAsync(cuenta, contextos));
        }

        [Fact]
        public async Task Una_fecha_mal_escrita_no_exige_nada_todavia()
        {
            var (cuenta, contextos) = await CuentaConSitios("empresa_admin");

            Assert.Equal(ExigenciaDeMfa.Aviso, await Armar(o => o.MfaObligatoriaAdminTenantDesde = "pronto").ExigenciaDeMfaAsync(cuenta, contextos));
        }

        // ── Identidad ─────────────────────────────────────────────────────────

        [Fact]
        public async Task La_identidad_lleva_la_cuenta_el_sitio_elegido_y_el_nombre_con_el_que_entro()
        {
            var (cuenta, contextos) = await CuentaConSitios("empresa_admin");
            cuenta.IdUserLegado = 4;
            _db.SaveChanges();

            var datos = Armar().ConstruirIdentidad(cuenta, contextos.Contextos[0], "egaray", mfaVerificada: true);

            Assert.Equal(cuenta.IdCuenta, datos.IdCuenta);
            Assert.Equal(4, datos.IdUserLegado);
            Assert.Equal("egaray", datos.NombreUsuario);
            Assert.Equal("empresa_admin", datos.Rol);
            Assert.Equal(DatosBase.EmpresaA, datos.IdEmpresa);
            Assert.Equal(DatosBase.TenantA, datos.IdTenant);
            Assert.Equal("TENANT_A", datos.CodigoTenant);
            Assert.True(datos.MfaVerificada);
            Assert.False(datos.EsOperador);
        }

        [Fact]
        public async Task Sin_nombre_escrito_la_identidad_usa_el_usuario_de_la_cuenta_o_su_correo()
        {
            var (cuenta, contextos) = await CuentaConSitios();

            Assert.Equal("x@prueba.local", Armar().ConstruirIdentidad(cuenta, contextos.Contextos[0], "", false).NombreUsuario);
            cuenta.NombreUsuario = "equis";
            Assert.Equal("equis", Armar().ConstruirIdentidad(cuenta, contextos.Contextos[0], " ", false).NombreUsuario);
        }
    }

    /// <summary>La cookie se compara con la base en cada petición: cambios de rol y revocación sin cerrar sesión (F1.5, D50).</summary>
    public class ValidadorDeIdentidadTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly ServiceProvider _servicios;
        private readonly ValidadorDeIdentidad _validador;
        private readonly Cuenta _cuenta;
        private readonly Membresia _membresia;
        private readonly AccesoEmpresa _acceso;

        public ValidadorDeIdentidadTests()
        {
            var servicios = new ServiceCollection();
            servicios.AddScoped<ApplicationDbContext>(_ => _bd.Crear());
            servicios.AddScoped<TenantContext>(_ => IdentidadDePrueba.Tenant());
            _servicios = servicios.BuildServiceProvider();
            _validador = new ValidadorDeIdentidad(_servicios.GetRequiredService<IServiceScopeFactory>(),
                new MemoryCache(new MemoryCacheOptions()), IdentidadDePrueba.Opciones());

            using var db = _bd.Crear();
            (_cuenta, _membresia, _acceso) = IdentidadDePrueba.CuentaDeEmpresa(db, "ana@prueba.local", rolLegado: "empresa_user");
        }

        public void Dispose()
        {
            _servicios.Dispose();
            _bd.Dispose();
        }

        /// <summary>La identidad que el inicio de sesión emitiría hoy para la cuenta de prueba.</summary>
        private DatosDeIdentidad Identidad()
        {
            using var db = _bd.Crear();
            var cuenta = db.Cuentas.Single(c => c.IdCuenta == _cuenta.IdCuenta);
            var directorio = new DirectorioDeCuentas(db, IdentidadDePrueba.Tenant());
            var opciones = IdentidadDePrueba.Opciones();
            var autenticador = new Autenticador(db, directorio, new BloqueoDeCuentas(db, opciones), new ServicioMfa(db, new EphemeralDataProtectionProvider(), opciones),
                new PasswordService(), opciones, NullLogger<Autenticador>.Instance);
            var contexto = directorio.ContextosAsync(cuenta).GetAwaiter().GetResult().Contextos.Single();
            return autenticador.ConstruirIdentidad(cuenta, contexto, "ana", false);
        }

        private void Cambiar(Action<ApplicationDbContext> cambio)
        {
            using var db = _bd.Crear();
            cambio(db);
            db.SaveChanges();
            _validador.Invalidar(_cuenta.IdCuenta);
        }

        [Fact]
        public async Task Una_identidad_recien_emitida_no_necesita_renovarse()
        {
            var resultado = await _validador.ValidarAsync(ReclamosDeIdentidad.Crear(Identidad()));

            Assert.True(resultado.Valida);
            Assert.Null(resultado.Renovada);
        }

        [Fact]
        public async Task Una_cuenta_desactivada_deja_de_entrar_en_la_peticion_siguiente()
        {
            var principal = ReclamosDeIdentidad.Crear(Identidad());
            Cambiar(db => db.Cuentas.Single(c => c.IdCuenta == _cuenta.IdCuenta).Estado = EstadoCuenta.Inactiva);

            Assert.False((await _validador.ValidarAsync(principal)).Valida);
        }

        [Fact]
        public async Task Una_membresia_suspendida_o_un_acceso_inactivo_dejan_de_entrar()
        {
            var principal = ReclamosDeIdentidad.Crear(Identidad());

            Cambiar(db => db.Membresias.Single(m => m.IdMembresia == _membresia.IdMembresia).Estado = EstadoMembresia.Suspendida);
            Assert.False((await _validador.ValidarAsync(principal)).Valida);

            Cambiar(db => db.Membresias.Single(m => m.IdMembresia == _membresia.IdMembresia).Estado = EstadoMembresia.Activa);
            Assert.True((await _validador.ValidarAsync(principal)).Valida);

            Cambiar(db => db.AccesosEmpresa.Single(a => a.IdAccesoEmpresa == _acceso.IdAccesoEmpresa).Activo = false);
            Assert.False((await _validador.ValidarAsync(principal)).Valida);
        }

        [Fact]
        public async Task Un_cambio_de_rol_renueva_la_cookie_con_el_rol_nuevo_sin_cerrar_la_sesion()
        {
            var principal = ReclamosDeIdentidad.Crear(Identidad());
            Cambiar(db => db.AccesosEmpresa.Single(a => a.IdAccesoEmpresa == _acceso.IdAccesoEmpresa).RolLegado = "empresa_admin");

            var resultado = await _validador.ValidarAsync(principal);

            Assert.True(resultado.Valida);
            var nueva = ReclamosDeIdentidad.Leer(resultado.Renovada)!;
            Assert.Equal("empresa_admin", nueva.Rol);
            Assert.Equal(DatosBase.EmpresaA, nueva.IdEmpresa);   // el sitio sigue siendo el mismo

            // La cookie renovada ya está al día.
            var otra = await _validador.ValidarAsync(resultado.Renovada!);
            Assert.True(otra.Valida);
            Assert.Null(otra.Renovada);
        }

        [Fact]
        public async Task Un_cambio_del_rol_de_empresa_se_aplica_en_la_peticion_siguiente()
        {
            var principal = ReclamosDeIdentidad.Crear(Identidad());
            int idRol = 0;
            Cambiar(db =>
            {
                var rol = new EmpresaRol { IdEmpresa = DatosBase.EmpresaA, Nombre = "Flota" };
                db.EmpresaRoles.Add(rol);
                db.SaveChanges();
                idRol = rol.IdRol;
                db.AccesosEmpresa.Single(a => a.IdAccesoEmpresa == _acceso.IdAccesoEmpresa).IdRol = rol.IdRol;
            });

            var resultado = await _validador.ValidarAsync(principal);

            Assert.Equal(idRol, ReclamosDeIdentidad.Leer(resultado.Renovada)!.IdRolEmpresa);
        }

        [Fact]
        public async Task Pedir_el_cambio_de_clave_renueva_la_cookie_con_la_marca()
        {
            var principal = ReclamosDeIdentidad.Crear(Identidad());
            Cambiar(db => db.Cuentas.Single(c => c.IdCuenta == _cuenta.IdCuenta).DebeCambiarClave = true);

            var nueva = ReclamosDeIdentidad.Leer((await _validador.ValidarAsync(principal)).Renovada)!;

            Assert.True(nueva.DebeCambiarClave);
        }

        [Fact]
        public async Task Lo_leido_se_recuerda_hasta_que_se_invalide()
        {
            var principal = ReclamosDeIdentidad.Crear(Identidad());
            Assert.True((await _validador.ValidarAsync(principal)).Valida);

            // Un cambio directo en la base no se ve mientras dure lo recordado...
            using (var db = _bd.Crear())
            {
                db.Cuentas.Single(c => c.IdCuenta == _cuenta.IdCuenta).Estado = EstadoCuenta.Inactiva;
                db.SaveChanges();
            }
            Assert.True((await _validador.ValidarAsync(principal)).Valida);

            // ...y se nota en cuanto se invalida (por ejemplo, al cambiar la clave o el rol desde una pantalla).
            _validador.Invalidar(_cuenta.IdCuenta);
            Assert.False((await _validador.ValidarAsync(principal)).Valida);
        }

        [Fact]
        public async Task El_operador_en_plataforma_vale_mientras_siga_siendo_operador()
        {
            using (var db = _bd.Crear())
            {
                db.Cuentas.Single(c => c.IdCuenta == _cuenta.IdCuenta).EsOperador = true;
                db.SaveChanges();
            }
            var sello = ReclamosDeIdentidad.CalcularSello(EstadoCuenta.Activa, true, false, null, null, null, null, true);
            var principal = ReclamosDeIdentidad.Crear(DatosDeIdentidadDePrueba.Plataforma() with { IdCuenta = _cuenta.IdCuenta, Sello = sello });

            Assert.True((await _validador.ValidarAsync(principal)).Valida);

            Cambiar(db => db.Cuentas.Single(c => c.IdCuenta == _cuenta.IdCuenta).EsOperador = false);
            Assert.False((await _validador.ValidarAsync(principal)).Valida);
        }

        [Fact]
        public async Task Una_cookie_sin_cuenta_no_vale()
        {
            Assert.False((await _validador.ValidarAsync(new System.Security.Claims.ClaimsPrincipal())).Valida);
            Assert.False((await _validador.ValidarAsync(ReclamosDeIdentidad.Crear(Identidad() with { IdCuenta = 9999 }))).Valida);
        }

        [Fact]
        public async Task Una_membresia_de_otra_cuenta_no_vale_aunque_la_cookie_la_nombre()
        {
            var otra = IdentidadDePrueba.CuentaDeEmpresa(_bd.Crear(), "otra@prueba.local");
            var principal = ReclamosDeIdentidad.Crear(Identidad() with { IdMembresia = otra.Membresia.IdMembresia });

            Assert.False((await _validador.ValidarAsync(principal)).Valida);
        }
    }
}
