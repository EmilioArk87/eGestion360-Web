using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;
using eGestion360Web.Tests.Services.Identidad;

namespace eGestion360Web.Tests.Services.Auditoria
{
    /// <summary>
    /// El servicio que escribe la auditoría de seguridad (F1.8a, ADR-030): a qué tabla va cada evento, qué toma de la petición, que nunca
    /// guarda secretos, que un fallo de la auditoría no tumba a quien registra y que los fallos de cuentas inexistentes se agregan.
    /// </summary>
    public class AuditoriaDeSeguridadTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly HttpContextAccessor _accesor = new();
        private readonly ServiceProvider _proveedor;
        private readonly RegistroEnMemoria<AuditoriaDeSeguridad> _log = new();

        public AuditoriaDeSeguridadTests() => _proveedor = _bd.CrearProveedorDeAmbitos(_accesor);

        public void Dispose()
        {
            _proveedor.Dispose();
            _bd.Dispose();
        }

        private AuditoriaDeSeguridad Servicio(IServiceScopeFactory? ambitos = null)
            => new(ambitos ?? _proveedor.GetRequiredService<IServiceScopeFactory>(), _accesor, _log, _reloj);

        /// <summary>Una petición con la identidad de la cookie, una dirección, un agente y un id de correlación.</summary>
        private HttpContext Peticion(string direccion = "203.0.113.7", int? tenant = null, int? empresa = null)
        {
            var http = SesionFalsa.ConIdentidad(DatosDeIdentidadDePrueba.Empresa("empresa_admin", idEmpresa: DatosBase.EmpresaA, idTenant: DatosBase.TenantA));
            http.Connection.RemoteIpAddress = IPAddress.Parse(direccion);
            http.Request.Headers.UserAgent = "Mozilla/5.0 (pruebas)";
            http.TraceIdentifier = "traza-de-prueba-1";
            if (tenant != null) http.Items[TenantMiddleware.ClaveIdTenant] = tenant.Value;
            if (empresa != null) http.Items[TenantMiddleware.ClaveIdEmpresa] = empresa.Value;
            _accesor.HttpContext = http;
            return http;
        }

        private List<AuditoriaSeguridad> DelTenant() { using var db = _bd.Crear(); return db.AuditoriaSeguridad.AsNoTracking().OrderBy(a => a.IdEvento).ToList(); }
        private List<AuditoriaPlataforma> DeLaPlataforma() { using var db = _bd.Crear(); return db.AuditoriaPlataforma.AsNoTracking().OrderBy(a => a.IdEvento).ToList(); }

        // ── A qué tabla va y qué toma de la petición ──────────────────────────

        [Fact]
        public async Task Un_evento_de_tenant_deja_su_fila_con_cuenta_tenant_direccion_agente_y_correlacion()
        {
            Peticion();

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.ContextoElegido)
            {
                IdTenant = DatosBase.TenantA, IdEmpresa = DatosBase.EmpresaA, Detalle = new { sitio = "tenant", rol = "empresa_admin" },
            });

            var fila = Assert.Single(DelTenant());
            Assert.Equal(TiposDeEvento.ContextoElegido, fila.Tipo);
            Assert.Equal(ResultadosDeEvento.Ok, fila.Resultado);
            Assert.Equal(DatosBase.TenantA, fila.IdTenant);
            Assert.Equal(DatosBase.EmpresaA, fila.IdEmpresa);
            Assert.Equal(10, fila.IdCuenta);                    // la cuenta de la cookie
            Assert.Equal("egaray", fila.Usuario);
            Assert.False(fila.EsOperador);
            Assert.Equal("203.0.113.7", fila.Ip);
            Assert.Equal("Mozilla/5.0 (pruebas)", fila.Agente);
            Assert.Equal("traza-de-prueba-1", fila.IdCorrelacion);   // el mismo valor de X-Correlation-Id
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime, fila.FechaHora);
            Assert.Equal("{\"sitio\":\"tenant\",\"rol\":\"empresa_admin\"}", fila.Detalle);
            Assert.Empty(DeLaPlataforma());
        }

        [Fact]
        public async Task Un_evento_de_cuenta_va_a_la_plataforma_aunque_traiga_el_tenant_sobre_el_que_se_actuo()
        {
            Peticion();

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada) { IdTenant = DatosBase.TenantB, IdEmpresa = DatosBase.EmpresaB });

            var fila = Assert.Single(DeLaPlataforma());
            Assert.Equal(DatosBase.TenantB, fila.IdTenant);      // referencia, no filtro
            Assert.Equal(TiposDeEvento.SesionEntrada, fila.Tipo);
            Assert.Empty(DelTenant());
        }

        [Fact]
        public async Task El_contexto_elegido_sin_tenant_va_a_la_plataforma()
        {
            Peticion();

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.ContextoElegido) { EsOperador = true, Detalle = new { sitio = "plataforma" } });

            Assert.Empty(DelTenant());
            var fila = Assert.Single(DeLaPlataforma());
            Assert.True(fila.EsOperador);
            Assert.Null(fila.IdTenant);
        }

        [Fact]
        public async Task El_tenant_y_la_empresa_salen_de_la_peticion_si_el_evento_no_los_trae()
        {
            Peticion(tenant: DatosBase.TenantB, empresa: DatosBase.EmpresaB);

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.ContextoElegido));

            var fila = Assert.Single(DelTenant());
            Assert.Equal(DatosBase.TenantB, fila.IdTenant);
            Assert.Equal(DatosBase.EmpresaB, fila.IdEmpresa);
        }

        [Fact]
        public async Task Sin_peticion_el_evento_se_guarda_con_un_id_de_correlacion_propio_y_sin_direccion()
        {
            _accesor.HttpContext = null;

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada) { IdCuenta = 5, Usuario = "proceso" });

            var fila = Assert.Single(DeLaPlataforma());
            Assert.Equal(32, fila.IdCorrelacion.Length);
            Assert.Null(fila.Ip);
            Assert.Null(fila.Agente);
            Assert.Equal(5, fila.IdCuenta);
        }

        [Fact]
        public async Task Una_direccion_IPv4_dentro_de_IPv6_se_guarda_como_IPv4()
        {
            Peticion("::ffff:198.51.100.9");

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionSalida));

            Assert.Equal("198.51.100.9", Assert.Single(DeLaPlataforma()).Ip);
        }

        [Fact]
        public async Task El_nombre_y_la_cuenta_se_buscan_cuando_el_evento_solo_trae_la_cuenta_o_el_usuario_anterior()
        {
            using var db = _bd.Crear();
            var cuenta = IdentidadDePrueba.NuevaCuenta(db, "ana@prueba.local", usuario: "ana", idUserLegado: 77);
            _accesor.HttpContext = new DefaultHttpContext();   // sin sesión

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.MfaVerificada) { IdCuenta = cuenta.IdCuenta });
            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.ClaveRestablecida) { IdUserLegado = 77 });

            var filas = DeLaPlataforma();
            Assert.Equal(2, filas.Count);
            Assert.All(filas, f => Assert.Equal(cuenta.IdCuenta, f.IdCuenta));
            Assert.All(filas, f => Assert.Equal("ana", f.Usuario));
        }

        [Fact]
        public async Task Un_evento_de_la_entrada_de_una_cuenta_del_personal_sin_cookie_queda_marcado_como_operador_salvo_que_el_evento_diga_otra_cosa()
        {
            using var db = _bd.Crear();
            var operador = IdentidadDePrueba.NuevaCuenta(db, "op@prueba.local", usuario: "admin", operador: true);
            var comun = IdentidadDePrueba.NuevaCuenta(db, "ana@prueba.local", usuario: "ana");
            _accesor.HttpContext = new DefaultHttpContext();   // en la entrada la cookie todavía no existe

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.MfaVerificada) { IdCuenta = operador.IdCuenta });
            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.MfaVerificada) { IdCuenta = comun.IdCuenta });
            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.MfaVerificada) { IdCuenta = operador.IdCuenta, EsOperador = false });

            Assert.Equal(new[] { true, false, false }, DeLaPlataforma().Select(f => f.EsOperador).ToArray());
        }

        // ── Estricto y «Ambos» (F1.8b) ────────────────────────────────────────

        [Fact]
        public async Task Un_evento_de_soporte_deja_las_dos_filas_con_el_mismo_tenant_la_misma_correlacion_y_la_misma_hora()
        {
            Peticion();

            await Servicio().RegistrarEstrictoAsync(new EventoDeSeguridad(TiposDeEvento.SoporteEntrada)
            {
                IdTenant = DatosBase.TenantB, IdEmpresa = DatosBase.EmpresaB, EsOperador = true, ObjetoTipo = "tenant", ObjetoId = "2",
                Detalle = new { tenant = "TENANT_B", estado = "activo" },
            });

            var deTenant = Assert.Single(DelTenant());
            var dePlataforma = Assert.Single(DeLaPlataforma());
            Assert.Equal(TiposDeEvento.SoporteEntrada, deTenant.Tipo);
            Assert.Equal(DatosBase.TenantB, deTenant.IdTenant);
            Assert.Equal(DatosBase.TenantB, dePlataforma.IdTenant);
            Assert.Equal(deTenant.IdCorrelacion, dePlataforma.IdCorrelacion);
            Assert.Equal(deTenant.FechaHora, dePlataforma.FechaHora);
            Assert.Equal(deTenant.Detalle, dePlataforma.Detalle);
            Assert.True(deTenant.EsOperador);
        }

        [Fact]
        public async Task El_modo_estricto_lanza_si_no_se_puede_guardar_y_no_deja_ninguna_de_las_dos_filas()
        {
            Peticion();
            var roto = new ServiceCollection();
            roto.AddScoped<TenantContext>(_ => new TenantContext(_accesor));
            roto.AddScoped<ApplicationDbContext>(_ => throw new InvalidOperationException("base caída"));
            await using var proveedorRoto = roto.BuildServiceProvider();
            var servicio = Servicio(proveedorRoto.GetRequiredService<IServiceScopeFactory>());

            var ex = await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() =>
                servicio.RegistrarEstrictoAsync(new EventoDeSeguridad(TiposDeEvento.SoporteEntrada) { IdTenant = DatosBase.TenantA }));

            Assert.Contains(TiposDeEvento.SoporteEntrada, ex.Message);
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Empty(DelTenant());
            Assert.Empty(DeLaPlataforma());
            Assert.Contains(_log.Mensajes, m => m.Nivel == LogLevel.Error && m.Mensaje.Contains("la acción no se hace"));
        }

        [Fact]
        public async Task El_modo_estricto_tambien_rechaza_un_evento_que_no_es_del_catalogo_o_con_un_campo_prohibido()
        {
            Peticion();

            await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => Servicio().RegistrarEstrictoAsync(new EventoDeSeguridad("cosa.rara")));
            await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => Servicio().RegistrarEstrictoAsync(
                new EventoDeSeguridad(TiposDeEvento.SoporteEntrada) { IdTenant = DatosBase.TenantA, Detalle = new { password = "x" } }));

            Assert.Empty(DelTenant());
            Assert.Empty(DeLaPlataforma());
        }

        [Fact]
        public async Task El_modo_estricto_propaga_la_cancelacion_sin_envolverla()
        {
            Peticion();
            using var cancelacion = new CancellationTokenSource();
            cancelacion.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Servicio().RegistrarEstrictoAsync(new EventoDeSeguridad(TiposDeEvento.SoporteEntrada) { IdTenant = DatosBase.TenantA }, cancelacion.Token));
        }

        [Fact]
        public async Task Un_evento_de_administracion_por_el_servicio_va_al_tenant_y_sin_tenant_a_la_plataforma()
        {
            Peticion();

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.InvitacionEnviada) { IdTenant = DatosBase.TenantA, IdEmpresa = DatosBase.EmpresaA });
            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.InvitacionAnulada));

            Assert.Equal(TiposDeEvento.InvitacionEnviada, Assert.Single(DelTenant()).Tipo);
            Assert.Equal(TiposDeEvento.InvitacionAnulada, Assert.Single(DeLaPlataforma()).Tipo);   // sin tenant no se pierde
        }

        // ── Lo que nunca se guarda y lo que no tumba ──────────────────────────

        [Fact]
        public async Task Un_tipo_fuera_del_catalogo_no_se_guarda_y_no_tumba_a_quien_registra()
        {
            Peticion();

            await Servicio().RegistrarAsync(new EventoDeSeguridad("cosa.rara"));

            Assert.Empty(DelTenant());
            Assert.Empty(DeLaPlataforma());
            Assert.Contains(_log.Mensajes, m => m.Nivel == LogLevel.Error && m.Mensaje.Contains("cosa.rara"));
        }

        [Theory]
        [InlineData("password")]
        [InlineData("Clave")]
        [InlineData("token")]
        [InlineData("codigo")]
        [InlineData("hash")]
        [InlineData("documento")]
        public async Task Un_detalle_con_un_campo_que_nunca_se_guarda_se_rechaza_entero(string campo)
        {
            Peticion();
            var detalle = new Dictionary<string, object?> { ["motivo"] = "x", [campo] = "valor-secreto" };

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada) { Detalle = detalle });

            Assert.Empty(DeLaPlataforma());
            Assert.DoesNotContain(_log.Mensajes, m => m.Mensaje.Contains("valor-secreto"));
        }

        [Fact]
        public async Task Un_resultado_invalido_no_se_guarda()
        {
            Peticion();

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada) { Resultado = "quizas" });

            Assert.Empty(DeLaPlataforma());
        }

        [Fact]
        public async Task Un_detalle_demasiado_largo_se_reemplaza_por_una_marca_y_la_fila_se_guarda()
        {
            Peticion();

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada) { Detalle = new { motivo = new string('x', 5000) } });

            Assert.Equal("{\"truncado\":true}", Assert.Single(DeLaPlataforma()).Detalle);
        }

        [Fact]
        public async Task Los_campos_largos_se_recortan_a_lo_que_cabe_en_la_tabla()
        {
            var http = Peticion();
            http.Request.Headers.UserAgent = new string('a', 500);

            await Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada)
            {
                Usuario = new string('u', 300), ObjetoTipo = new string('t', 90), ObjetoId = new string('i', 200),
            });

            var fila = Assert.Single(DeLaPlataforma());
            Assert.Equal(200, fila.Agente!.Length);
            Assert.Equal(100, fila.Usuario!.Length);
            Assert.Equal(40, fila.ObjetoTipo!.Length);
            Assert.Equal(60, fila.ObjetoId!.Length);
        }

        [Fact]
        public async Task Si_la_base_falla_el_error_no_sube_y_queda_en_el_log()
        {
            Peticion();
            var roto = new ServiceCollection();
            roto.AddScoped<TenantContext>(_ => new TenantContext(_accesor));
            roto.AddScoped<ApplicationDbContext>(_ => throw new InvalidOperationException("base caída"));
            await using var proveedorRoto = roto.BuildServiceProvider();

            await Servicio(proveedorRoto.GetRequiredService<IServiceScopeFactory>()).RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada));

            Assert.Contains(_log.Mensajes, m => m.Nivel == LogLevel.Error && m.Mensaje.Contains(TiposDeEvento.SesionEntrada));
            Assert.Empty(DeLaPlataforma());
        }

        [Fact]
        public async Task Cancelar_si_se_propaga()
        {
            Peticion();
            using var cancelacion = new CancellationTokenSource();
            cancelacion.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Servicio().RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada), cancelacion.Token));
        }

        // ── Agregación de los intentos con una cuenta que no existe (D71) ─────

        private EventoDeSeguridad Intento(string motivo = "cuenta_inexistente") => new(TiposDeEvento.SesionEntrada)
        {
            Resultado = ResultadosDeEvento.Fallo, Agregable = true, ClaveDeAgregado = motivo, Usuario = "ma***@e***.hn", Detalle = new { motivo },
        };

        [Fact]
        public async Task Los_intentos_agregables_de_una_direccion_dejan_una_fila_por_hora_y_cuentan_los_omitidos()
        {
            Peticion();
            var servicio = Servicio();

            for (var i = 0; i < 5; i++) await servicio.RegistrarAsync(Intento());
            Assert.Single(DeLaPlataforma());                       // 4 omitidos en esta hora

            _reloj.Avanzar(TimeSpan.FromHours(1));
            await servicio.RegistrarAsync(Intento());

            var filas = DeLaPlataforma();
            Assert.Equal(2, filas.Count);
            Assert.Contains("\"omitidos_antes\":4", filas[1].Detalle);
            Assert.DoesNotContain("omitidos_antes", filas[0].Detalle);
        }

        [Fact]
        public async Task Otra_direccion_u_otro_motivo_cuentan_aparte()
        {
            var servicio = Servicio();

            Peticion("203.0.113.1");
            await servicio.RegistrarAsync(Intento());
            await servicio.RegistrarAsync(Intento("bloqueada"));    // otro motivo, misma dirección
            Peticion("203.0.113.2");
            await servicio.RegistrarAsync(Intento());                // otra dirección

            Assert.Equal(3, DeLaPlataforma().Count);
        }

        [Fact]
        public async Task Un_ataque_desde_muchas_direcciones_no_pasa_del_tope_por_hora()
        {
            var servicio = Servicio();

            for (var i = 0; i < AuditoriaDeSeguridad.TopeDeAgregadosPorHora + 25; i++)
            {
                Peticion($"10.1.{i / 250}.{i % 250 + 1}");
                await servicio.RegistrarAsync(Intento());
            }

            Assert.Equal(AuditoriaDeSeguridad.TopeDeAgregadosPorHora, DeLaPlataforma().Count);
        }

        [Fact]
        public async Task Los_eventos_que_no_son_agregables_se_guardan_todos()
        {
            Peticion();
            var servicio = Servicio();

            for (var i = 0; i < 4; i++)
                await servicio.RegistrarAsync(new EventoDeSeguridad(TiposDeEvento.SesionEntrada) { Resultado = ResultadosDeEvento.Fallo, IdCuenta = 3 });

            Assert.Equal(4, DeLaPlataforma().Count);
        }

        // ── Enmascarado ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("maria.lopez@empresa.hn", "ma***@e***.hn")]
        [InlineData("a@b.co", "a***@b***.co")]
        [InlineData("admin", "ad***")]
        [InlineData("x", "x***")]
        [InlineData("  juan  ", "ju***")]
        public void El_enmascarado_nunca_devuelve_el_texto_completo(string texto, string esperado)
        {
            var enmascarado = EnmascaradoDeAuditoria.Identificador(texto);

            Assert.Equal(esperado, enmascarado);
            Assert.NotEqual(texto.Trim(), enmascarado);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Sin_texto_no_hay_nada_que_enmascarar(string? texto) => Assert.Null(EnmascaradoDeAuditoria.Identificador(texto));

        // ── El catálogo ───────────────────────────────────────────────────────

        [Fact]
        public void Todo_tipo_del_catalogo_tiene_la_forma_grupo_punto_accion_y_un_destino()
        {
            Assert.NotEmpty(TiposDeEvento.Todos);
            foreach (var tipo in TiposDeEvento.Todos)
            {
                Assert.Matches("^[a-z0-9_]+\\.[a-z0-9_]+$", tipo);   // lo mismo que exige CK_auditoria_*_tipo
                Assert.True(tipo.Length <= 60);
                _ = TiposDeEvento.DestinoDe(tipo);
            }
        }
    }
}
