using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Plataforma;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Plataforma
{
    /// <summary>
    /// F1.9a (ADR-032): la consola del operador sobre un suscriptor. Con el interceptor de auditoría real sobre SQLite: qué cambios de estado
    /// y de plan se permiten, qué se guarda, qué filas de auditoría quedan en las dos tablas, y que sin rastro no hay cambio.
    /// El tenant A de las pruebas es el interno (la plataforma) y el B es un cliente.
    /// </summary>
    public class ServicioDeSuscriptoresTests : IDisposable
    {
        private const string Operador = "operador.pruebas";
        private const int PlanBasico = 11, PlanGrande = 12, PlanRetirado = 13, PlanFuturo = 14;

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly MemoryCache _memoria = new(new MemoryCacheOptions());
        private readonly ContextoAuditoriaFijo _quien = new()
        {
            Usuario = Operador, IdCuenta = 1, IdCorrelacion = "corr-consola", EsOperador = true, Ip = "198.51.100.4",
        };

        public ServicioDeSuscriptoresTests()
        {
            using var db = _bd.Crear();
            var antes = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
            var derechos = new[]
            {
                new Derecho { IdDerecho = 101, Codigo = "modulo.flota", Tipo = TipoDerecho.Modulo, Descripcion = "Módulo de Flota", CreadoPor = "pruebas", FechaCreacion = antes },
                new Derecho { IdDerecho = 102, Codigo = "modulo.facturacion", Tipo = TipoDerecho.Modulo, Descripcion = "Módulo de Facturación", CreadoPor = "pruebas", FechaCreacion = antes },
                new Derecho { IdDerecho = 103, Codigo = "limite.usuarios", Tipo = TipoDerecho.Limite, Descripcion = "Usuarios activos", CreadoPor = "pruebas", FechaCreacion = antes },
                new Derecho { IdDerecho = 104, Codigo = "limite.empresas", Tipo = TipoDerecho.Limite, Descripcion = "Empresas", CreadoPor = "pruebas", FechaCreacion = antes },
            };
            db.Derechos.AddRange(derechos);

            Plan Plan(int id, string codigo, bool activo = true) => new() { IdPlan = id, Codigo = codigo, Nombre = "Plan " + codigo, Activo = activo, CreadoPor = "pruebas", FechaCreacion = antes };
            db.Planes.AddRange(Plan(1, "basico"), Plan(2, "grande"), Plan(3, "retirado", activo: false), Plan(4, "futuro"));

            PlanVersion Version(int id, int plan, DateOnly desde) => new() { IdPlanVersion = id, IdPlan = plan, Numero = 1, VigenteDesde = desde, CreadoPor = "pruebas", FechaCreacion = antes };
            db.PlanVersiones.AddRange(
                Version(PlanBasico, 1, new DateOnly(2026, 1, 1)), Version(PlanGrande, 2, new DateOnly(2026, 1, 1)),
                Version(PlanRetirado, 3, new DateOnly(2026, 1, 1)), Version(PlanFuturo, 4, RelojFijo.Hoy.AddDays(1)));

            void Concede(int version, string codigo, int? tope = null)
            {
                var d = derechos.Single(x => x.Codigo == codigo);
                db.PlanVersionDerechos.Add(new PlanVersionDerecho
                {
                    IdPlanVersion = version, IdDerecho = d.IdDerecho, TipoDelDerecho = d.Tipo, ValorLimite = tope, CreadoPor = "pruebas", FechaCreacion = antes,
                });
            }
            Concede(PlanBasico, "modulo.flota"); Concede(PlanBasico, "limite.usuarios", 5); Concede(PlanBasico, "limite.empresas", 3);
            Concede(PlanGrande, "modulo.facturacion"); Concede(PlanGrande, "limite.usuarios", 1); Concede(PlanGrande, "limite.empresas");

            foreach (var tenant in new[] { DatosBase.TenantA, DatosBase.TenantB })
                db.TenantPlanes.Add(new TenantPlan { IdTenant = tenant, IdPlanVersion = PlanBasico, VigenteDesde = antes, Motivo = "alta", CreadoPor = "pruebas", FechaCreacion = antes });

            foreach (var (nombre, activo) in new[] { ("uno", true), ("dos", true), ("baja", false) })
                db.Users.Add(new User { Username = nombre, Email = nombre + "@prueba.local", Password = "x", Role = "empresa_user", IsActive = activo, EmpresaId = DatosBase.EmpresaB });
            db.SaveChanges();
        }

        public void Dispose()
        {
            _bd.Dispose();
            _memoria.Dispose();
        }

        private ApplicationDbContext Auditado(IContextoAuditoria? quien = null)
        {
            var q = quien ?? _quien;
            return _bd.Crear(new AuditoriaCambiosInterceptor(q, new ConstructorDeEventosDeAdministracion(q, _bd.Resolvedor, _reloj)));
        }

        private ServicioDeSuscriptores Servicio(ApplicationDbContext db, TenantContext? tenant = null, IDerechosDeUso? derechos = null)
            => new(db, tenant, derechos, _reloj);

        private Tenant Suscriptor(int id = DatosBase.TenantB)
        {
            using var db = _bd.Crear();
            return db.Tenants.AsNoTracking().Single(t => t.IdTenant == id);
        }

        private List<TenantPlan> PlanesDe(int id = DatosBase.TenantB)
        {
            using var db = _bd.Crear();
            return db.TenantPlanes.AsNoTracking().Where(p => p.IdTenant == id).OrderBy(p => p.IdTenantPlan).ToList();
        }

        private (List<AuditoriaSeguridad> DelTenant, List<AuditoriaPlataforma> DeLaPlataforma) Auditoria()
        {
            using var db = _bd.Crear();
            return (db.AuditoriaSeguridad.AsNoTracking().OrderBy(a => a.IdEvento).ToList(), db.AuditoriaPlataforma.AsNoTracking().OrderBy(a => a.IdEvento).ToList());
        }

        private static JsonElement Detalle(string? json) => JsonDocument.Parse(json!).RootElement;

        private static CambioDeEstado Estado(AccionDeEstado accion, string? modo = ModoDeSuspension.Lectura, string? motivo = "Pago pendiente",
            string? codigo = "TENANT_B", int idTenant = DatosBase.TenantB, string? token = "")
            => new(idTenant, accion, modo, motivo, codigo, token);

        private async Task<ResultadoDeSuscriptor> CambiarEstadoAsync(CambioDeEstado cambio, TenantContext? tenant = null, IDerechosDeUso? derechos = null)
        {
            using var db = Auditado();
            return await Servicio(db, tenant, derechos).CambiarEstadoAsync(cambio, Operador);
        }

        private async Task<ResultadoDeSuscriptor> CambiarPlanAsync(int nuevo, int actual = PlanBasico, string? motivo = "Cambio de contrato",
            string? codigo = "TENANT_B", int idTenant = DatosBase.TenantB)
        {
            using var db = Auditado();
            return await Servicio(db).CambiarPlanAsync(new CambioDePlan(idTenant, nuevo, actual, motivo, codigo), Operador);
        }

        // ── Lecturas ────────────────────────────────────────────────────────

        [Fact]
        public async Task La_lista_trae_plan_empresas_usuarios_activos_y_topes()
        {
            using var db = _bd.Crear();

            var lista = await Servicio(db).ListarAsync();

            Assert.Equal(new[] { "TENANT_A", "TENANT_B" }, lista.Select(s => s.Codigo));
            var b = lista.Single(s => s.IdTenant == DatosBase.TenantB);
            Assert.Equal("basico v1", b.Plan);
            Assert.Equal(1, b.Empresas);
            Assert.Equal(3, b.TopeDeEmpresas);
            Assert.Equal(2, b.Usuarios);          // el inactivo no cuenta, igual que al aplicar el tope
            Assert.Equal(5, b.TopeDeUsuarios);
            Assert.Equal(2, lista.Single(s => s.IdTenant == DatosBase.TenantA).Empresas);
        }

        [Fact]
        public async Task El_detalle_ofrece_solo_planes_activos_y_ya_vigentes()
        {
            using var db = _bd.Crear();

            var detalle = await Servicio(db).ObtenerAsync(DatosBase.TenantB);

            Assert.NotNull(detalle);
            Assert.Equal("basico v1", detalle!.PlanActual!.Etiqueta);
            Assert.Equal(new[] { "basico v1", "grande v1" }, detalle.PlanesDisponibles.Select(v => v.Etiqueta));
            Assert.Single(detalle.Historial);
            Assert.Null(await Servicio(db).ObtenerAsync(999));
        }

        [Fact]
        public async Task Revisar_un_cambio_dice_que_se_gana_que_se_pierde_y_que_tope_queda_superado()
        {
            using var db = _bd.Crear();

            var previsto = await Servicio(db).PrevisualizarPlanAsync(DatosBase.TenantB, PlanGrande);

            Assert.NotNull(previsto);
            // En pantalla, el nombre del módulo y el del tope, no la descripción técnica guardada con el derecho.
            Assert.Contains("Módulo Facturación", previsto!.Gana);
            Assert.Contains("Empresas: de 3 a sin tope", previsto.Gana);
            Assert.Contains("Módulo Control de Flota", previsto.Pierde);
            Assert.Contains("Usuarios activos: de 5 a 1", previsto.Pierde);
            Assert.Contains(previsto.Avisos, a => a.Contains("hasta 1 usuarios activos y hoy hay 2"));
            Assert.Contains(previsto.Avisos, a => a.Contains("dejan de abrirse"));
            Assert.Null(await Servicio(db).PrevisualizarPlanAsync(DatosBase.TenantB, PlanRetirado));
        }

        // ── Estado ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Suspender_guarda_estado_modo_y_motivo_y_deja_su_fila_en_las_dos_auditorias()
        {
            var resultado = await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender, ModoDeSuspension.Bloqueo));

            Assert.True(resultado.Ok, resultado.Error);
            var t = Suscriptor();
            Assert.Equal(EstadoTenant.Suspendido, t.Estado);
            Assert.Equal(ModoDeSuspension.Bloqueo, t.ModoSuspension);
            Assert.Equal("Pago pendiente", t.MotivoEstado);
            Assert.Equal(Operador, t.ModificadoPor);
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime, t.FechaEstado);

            var (delTenant, dePlataforma) = Auditoria();
            var fila = Assert.Single(delTenant);
            var otra = Assert.Single(dePlataforma);
            Assert.Equal(TiposDeEvento.TenantEstado, fila.Tipo);
            Assert.Equal(DatosBase.TenantB, fila.IdTenant);
            Assert.Equal(DatosBase.TenantB, otra.IdTenant);
            Assert.True(otra.EsOperador);
            Assert.Equal("corr-consola", otra.IdCorrelacion);
            var detalle = Detalle(fila.Detalle);
            Assert.Equal("TENANT_B", detalle.GetProperty("suscriptor").GetString());
            Assert.Equal("Pago pendiente", detalle.GetProperty("motivo").GetString());
            Assert.Equal(ModoDeSuspension.Bloqueo, detalle.GetProperty("modoDeSuspension").GetString());
            Assert.Equal(EstadoTenant.Suspendido, detalle.GetProperty("estado")[1].GetString());
        }

        [Fact]
        public async Task El_correo_escrito_en_el_motivo_queda_enmascarado_en_la_auditoria()
        {
            await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender, motivo: "Lo pidió maria.lopez@empresa.hn"));

            var (delTenant, dePlataforma) = Auditoria();
            Assert.DoesNotContain("maria.lopez@empresa.hn", delTenant.Single().Detalle);
            Assert.DoesNotContain("maria.lopez@empresa.hn", dePlataforma.Single().Detalle);
        }

        [Fact]
        public async Task El_cambio_se_nota_de_inmediato_y_el_contexto_queda_en_el_tenant_del_suscriptor()
        {
            using var lectura = _bd.Crear();
            var derechos = new DerechosDeUso(lectura, _memoria, _reloj);
            Assert.True((await derechos.DelTenantAsync(DatosBase.TenantB))!.Opera);   // queda en memoria
            var contexto = new TenantContext(new HttpContextAccessor());

            await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender), contexto, derechos);

            Assert.True((await derechos.DelTenantAsync(DatosBase.TenantB))!.EnSoloLectura);
            Assert.Equal(DatosBase.TenantB, contexto.IdTenant);   // con la política encendida la base solo admite su fila en su contexto
        }

        [Fact]
        public async Task El_recorrido_completo_suspender_cambiar_modo_cancelar_recuperar_y_reactivar()
        {
            Assert.True((await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender))).Ok);
            Assert.True((await CambiarEstadoAsync(Estado(AccionDeEstado.CambiarModo, ModoDeSuspension.Bloqueo))).Ok);
            Assert.True((await CambiarEstadoAsync(Estado(AccionDeEstado.Cancelar, modo: null))).Ok);
            Assert.Equal(EstadoTenant.Cancelado, Suscriptor().Estado);
            Assert.True((await CambiarEstadoAsync(Estado(AccionDeEstado.RecuperarCancelado, ModoDeSuspension.Lectura))).Ok);
            Assert.Equal(EstadoTenant.Suspendido, Suscriptor().Estado);
            Assert.True((await CambiarEstadoAsync(Estado(AccionDeEstado.Reactivar, modo: null))).Ok);

            Assert.Equal(EstadoTenant.Activo, Suscriptor().Estado);
            Assert.Equal(5, Auditoria().DeLaPlataforma.Count(a => a.Tipo == TiposDeEvento.TenantEstado));
        }

        [Theory]
        [InlineData(AccionDeEstado.Cancelar)]            // de activo no se cancela: primero se suspende (D80)
        [InlineData(AccionDeEstado.Reactivar)]
        [InlineData(AccionDeEstado.CambiarModo)]
        [InlineData(AccionDeEstado.RecuperarCancelado)]
        public async Task Desde_activo_solo_se_puede_suspender(AccionDeEstado accion)
        {
            var resultado = await CambiarEstadoAsync(Estado(accion));

            Assert.False(resultado.Ok);
            Assert.Equal(EstadoTenant.Activo, Suscriptor().Estado);
            Assert.Empty(Auditoria().DeLaPlataforma);
        }

        [Fact]
        public async Task Un_cancelado_no_se_reactiva_de_golpe_y_el_mismo_modo_no_es_un_cambio()
        {
            await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender));
            Assert.False((await CambiarEstadoAsync(Estado(AccionDeEstado.CambiarModo, ModoDeSuspension.Lectura))).Ok);
            await CambiarEstadoAsync(Estado(AccionDeEstado.Cancelar));

            Assert.False((await CambiarEstadoAsync(Estado(AccionDeEstado.Reactivar))).Ok);
            Assert.Equal(EstadoTenant.Cancelado, Suscriptor().Estado);
        }

        [Theory]
        [InlineData(null, "TENANT_B")]
        [InlineData("   ", "TENANT_B")]
        [InlineData("Motivo", null)]
        [InlineData("Motivo", "TENANT_A")]
        [InlineData("Motivo", "TENANT")]
        public async Task Sin_motivo_o_sin_escribir_el_codigo_no_cambia_nada(string? motivo, string? codigo)
        {
            var resultado = await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender, motivo: motivo, codigo: codigo));

            Assert.False(resultado.Ok);
            Assert.Equal(EstadoTenant.Activo, Suscriptor().Estado);
            Assert.Empty(Auditoria().DelTenant);
        }

        [Fact]
        public async Task El_codigo_se_acepta_sin_importar_mayusculas_ni_espacios_y_el_motivo_tiene_tope()
        {
            Assert.False((await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender, motivo: new string('a', 301)))).Ok);

            Assert.True((await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender, motivo: new string('a', 300), codigo: " tenant_b "))).Ok);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("modo-inventado")]
        public async Task Suspender_exige_un_modo_valido(string? modo)
        {
            Assert.False((await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender, modo))).Ok);
            Assert.Equal(EstadoTenant.Activo, Suscriptor().Estado);
        }

        [Fact]
        public async Task El_suscriptor_interno_no_se_toca()
        {
            var estado = await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender, codigo: "TENANT_A", idTenant: DatosBase.TenantA));
            var plan = await CambiarPlanAsync(PlanGrande, codigo: "TENANT_A", idTenant: DatosBase.TenantA);

            Assert.False(estado.Ok);
            Assert.False(plan.Ok);
            Assert.Equal(EstadoTenant.Activo, Suscriptor(DatosBase.TenantA).Estado);
            Assert.Single(PlanesDe(DatosBase.TenantA));
            Assert.Empty(Auditoria().DeLaPlataforma);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("b3RyYS12ZXJzaW9u")]
        public async Task Si_otro_cambio_el_suscriptor_mientras_tanto_no_se_pisa(string? token)
        {
            var resultado = await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender, token: token));

            Assert.False(resultado.Ok);
            Assert.Contains("Alguien cambió", resultado.Error);
            Assert.Equal(EstadoTenant.Activo, Suscriptor().Estado);
        }

        [Fact]
        public async Task Si_la_auditoria_no_se_puede_armar_el_estado_no_cambia()
        {
            using var db = Auditado(new ContextoQueNoResponde());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Servicio(db).CambiarEstadoAsync(Estado(AccionDeEstado.Suspender), Operador));

            Assert.Equal(EstadoTenant.Activo, Suscriptor().Estado);
        }

        // ── Plan ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Cambiar_el_plan_cierra_el_vigente_abre_el_nuevo_y_dice_de_cual_a_cual()
        {
            var resultado = await CambiarPlanAsync(PlanGrande);

            Assert.True(resultado.Ok, resultado.Error);
            var planes = PlanesDe();
            Assert.Equal(2, planes.Count);
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime, planes[0].VigenteHasta);
            Assert.Null(planes[1].VigenteHasta);
            Assert.Equal(PlanGrande, planes[1].IdPlanVersion);
            Assert.Equal("Cambio de contrato", planes[1].Motivo);
            Assert.Equal(Operador, planes[1].CreadoPor);

            var (delTenant, dePlataforma) = Auditoria();
            var fila = Assert.Single(delTenant);
            Assert.Single(dePlataforma);
            Assert.Equal(TiposDeEvento.TenantPlanCambiado, fila.Tipo);
            Assert.Equal(DatosBase.TenantB, fila.IdTenant);
            var detalle = Detalle(fila.Detalle);
            Assert.Equal("basico v1", detalle.GetProperty("planAntes").GetString());
            Assert.Equal("grande v1", detalle.GetProperty("planDespues").GetString());
            Assert.Equal("Cambio de contrato", detalle.GetProperty("motivo").GetString());
        }

        [Theory]
        [InlineData(PlanRetirado)]
        [InlineData(PlanFuturo)]
        [InlineData(PlanBasico)]     // ya lo tiene
        [InlineData(999)]
        public async Task Solo_se_asigna_un_plan_disponible_y_distinto(int version)
        {
            Assert.False((await CambiarPlanAsync(version)).Ok);

            Assert.Single(PlanesDe());
            Assert.Empty(Auditoria().DeLaPlataforma);
        }

        [Fact]
        public async Task Si_el_plan_vigente_ya_no_es_el_que_se_veia_no_se_cambia()
        {
            Assert.False((await CambiarPlanAsync(PlanGrande, actual: PlanGrande)).Ok);

            Assert.Single(PlanesDe());
            Assert.Null(PlanesDe().Single().VigenteHasta);
        }

        [Fact]
        public async Task Un_cancelado_no_cambia_de_plan_y_un_suspendido_si()
        {
            await CambiarEstadoAsync(Estado(AccionDeEstado.Suspender));
            Assert.True((await CambiarPlanAsync(PlanGrande)).Ok);
            await CambiarEstadoAsync(Estado(AccionDeEstado.Cancelar));

            Assert.False((await CambiarPlanAsync(PlanBasico, actual: PlanGrande)).Ok);
        }

        [Fact]
        public async Task Si_la_auditoria_no_se_puede_armar_el_plan_vigente_sigue_abierto()
        {
            using var db = Auditado(new ContextoQueNoResponde());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Servicio(db).CambiarPlanAsync(new CambioDePlan(DatosBase.TenantB, PlanGrande, PlanBasico, "Motivo", "TENANT_B"), Operador));

            var plan = Assert.Single(PlanesDe());
            Assert.Null(plan.VigenteHasta);
        }

        /// <summary>Un contexto de auditoría que falla al pedirle quién actúa: la fila no se puede armar.</summary>
        private sealed class ContextoQueNoResponde : IContextoAuditoria
        {
            public string Usuario => throw new InvalidOperationException("sin auditoría");
            public int? IdEmpresa => null;
            public string Origen => "app";
            public bool EsOperador => true;
        }
    }
}
