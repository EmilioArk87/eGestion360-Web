using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Derechos
{
    /// <summary>
    /// Derechos de uso y estado de un tenant (paso F1.7, script 040): lo que concede el plan vigente, los módulos de la empresa,
    /// los límites y la memoria de 30 segundos. SQLite en memoria; las restricciones de la base las cubren el script 040 y su POSTCHECK.
    /// </summary>
    public class DerechosDeUsoTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _base = new();
        private readonly MemoryCache _memoria = new(new MemoryCacheOptions());

        public void Dispose()
        {
            _base.Dispose();
            _memoria.Dispose();
        }

        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();   // 1 de octubre de 2026, mediodía en Honduras

        private DerechosDeUso Servicio(ApplicationDbContext db) => new(db, _memoria, _reloj);

        /// <summary>
        /// Tenant A con el plan «flota» (flota; hasta 2 usuarios y 1 empresa) y tenant B con el plan «demo» (flota y catálogos, sin
        /// topes). La empresa A tiene activos flota y catálogos; la B, solo flota.
        /// </summary>
        private void Sembrar()
        {
            using var db = _base.Crear();
            var ahora = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

            // Los módulos vienen del modelo (HasData, como en la base real); facturación se apaga en el catálogo para probarlo.
            var flota = db.Modulos.Single(m => m.Codigo == "flota");
            var catalogos = db.Modulos.Single(m => m.Codigo == "catalogos");
            var facturacion = db.Modulos.Single(m => m.Codigo == "facturacion");
            facturacion.Activo = false;

            var derFlota = new Derecho { Codigo = "modulo.flota", Tipo = TipoDerecho.Modulo, Descripcion = "Flota", CreadoPor = "pruebas", FechaCreacion = ahora };
            var derCatalogos = new Derecho { Codigo = "modulo.catalogos", Tipo = TipoDerecho.Modulo, Descripcion = "Catálogos", CreadoPor = "pruebas", FechaCreacion = ahora };
            var derFacturacion = new Derecho { Codigo = "modulo.facturacion", Tipo = TipoDerecho.Modulo, Descripcion = "Facturación", CreadoPor = "pruebas", FechaCreacion = ahora };
            var limUsuarios = new Derecho { Codigo = CodigosDeDerecho.LimiteUsuarios, Tipo = TipoDerecho.Limite, Descripcion = "Usuarios", CreadoPor = "pruebas", FechaCreacion = ahora };
            var limEmpresas = new Derecho { Codigo = CodigosDeDerecho.LimiteEmpresas, Tipo = TipoDerecho.Limite, Descripcion = "Empresas", CreadoPor = "pruebas", FechaCreacion = ahora };
            db.Derechos.AddRange(derFlota, derCatalogos, derFacturacion, limUsuarios, limEmpresas);

            var planFlota = new Plan { Codigo = "flota", Nombre = "Flota", CreadoPor = "pruebas", FechaCreacion = ahora };
            var planDemo = new Plan { Codigo = "demo", Nombre = "Demo", CreadoPor = "pruebas", FechaCreacion = ahora };
            db.Planes.AddRange(planFlota, planDemo);
            db.SaveChanges();

            var vFlota = new PlanVersion { IdPlan = planFlota.IdPlan, Numero = 1, VigenteDesde = new DateOnly(2026, 10, 15), CreadoPor = "pruebas", FechaCreacion = ahora };
            var vDemo = new PlanVersion { IdPlan = planDemo.IdPlan, Numero = 1, VigenteDesde = new DateOnly(2026, 10, 15), CreadoPor = "pruebas", FechaCreacion = ahora };
            db.PlanVersiones.AddRange(vFlota, vDemo);
            db.SaveChanges();

            db.PlanVersionDerechos.AddRange(
                new PlanVersionDerecho { IdPlanVersion = vFlota.IdPlanVersion, IdDerecho = derFlota.IdDerecho, TipoDelDerecho = TipoDerecho.Modulo, CreadoPor = "pruebas", FechaCreacion = ahora },
                new PlanVersionDerecho { IdPlanVersion = vFlota.IdPlanVersion, IdDerecho = limUsuarios.IdDerecho, TipoDelDerecho = TipoDerecho.Limite, ValorLimite = 2, CreadoPor = "pruebas", FechaCreacion = ahora },
                new PlanVersionDerecho { IdPlanVersion = vFlota.IdPlanVersion, IdDerecho = limEmpresas.IdDerecho, TipoDelDerecho = TipoDerecho.Limite, ValorLimite = 1, CreadoPor = "pruebas", FechaCreacion = ahora },
                new PlanVersionDerecho { IdPlanVersion = vDemo.IdPlanVersion, IdDerecho = derFlota.IdDerecho, TipoDelDerecho = TipoDerecho.Modulo, CreadoPor = "pruebas", FechaCreacion = ahora },
                new PlanVersionDerecho { IdPlanVersion = vDemo.IdPlanVersion, IdDerecho = derCatalogos.IdDerecho, TipoDelDerecho = TipoDerecho.Modulo, CreadoPor = "pruebas", FechaCreacion = ahora },
                new PlanVersionDerecho { IdPlanVersion = vDemo.IdPlanVersion, IdDerecho = limUsuarios.IdDerecho, TipoDelDerecho = TipoDerecho.Limite, ValorLimite = null, CreadoPor = "pruebas", FechaCreacion = ahora });

            db.TenantPlanes.AddRange(
                new TenantPlan { IdTenant = DatosBase.TenantA, IdPlanVersion = vFlota.IdPlanVersion, VigenteDesde = ahora, CreadoPor = "pruebas", FechaCreacion = ahora },
                new TenantPlan { IdTenant = DatosBase.TenantB, IdPlanVersion = vDemo.IdPlanVersion, VigenteDesde = ahora, CreadoPor = "pruebas", FechaCreacion = ahora });
            db.SaveChanges();

            // Activación por empresa: la A tiene flota y catálogos; la B, flota.
            db.EmpresaModulos.AddRange(
                new EmpresaModulo { IdEmpresa = DatosBase.EmpresaA, IdModulo = flota.IdModulo, FechaActivacion = ahora, Activo = true },
                new EmpresaModulo { IdEmpresa = DatosBase.EmpresaA, IdModulo = catalogos.IdModulo, FechaActivacion = ahora, Activo = true },
                new EmpresaModulo { IdEmpresa = DatosBase.EmpresaB, IdModulo = flota.IdModulo, FechaActivacion = ahora, Activo = true });
            db.SaveChanges();
        }

        [Fact]
        public async Task El_tenant_trae_su_plan_sus_derechos_y_sus_limites()
        {
            Sembrar();
            await using var db = _base.Crear();

            var tenant = await Servicio(db).DelTenantAsync(DatosBase.TenantA);

            Assert.NotNull(tenant);
            Assert.Equal("flota", tenant!.PlanCodigo);
            Assert.True(tenant.Concede("modulo.flota"));
            Assert.False(tenant.Concede("modulo.catalogos"));
            Assert.Equal(2, tenant.Limite(CodigosDeDerecho.LimiteUsuarios));
            Assert.Equal(1, tenant.Limite(CodigosDeDerecho.LimiteEmpresas));
        }

        [Fact]
        public async Task Un_limite_sin_tope_devuelve_nulo_y_un_tenant_que_no_existe_devuelve_nulo()
        {
            Sembrar();
            await using var db = _base.Crear();
            var servicio = Servicio(db);

            var demo = await servicio.DelTenantAsync(DatosBase.TenantB);
            Assert.NotNull(demo);
            Assert.True(demo!.Concede(CodigosDeDerecho.LimiteUsuarios));
            Assert.Null(demo.Limite(CodigosDeDerecho.LimiteUsuarios));            // concedido sin tope
            Assert.Null(demo.Limite(CodigosDeDerecho.LimiteEmpresas));            // ni siquiera lo concede

            Assert.Null(await servicio.DelTenantAsync(999));
        }

        [Fact]
        public async Task Un_tenant_sin_plan_no_concede_ningun_derecho()
        {
            Sembrar();
            await using var db = _base.Crear();
            foreach (var plan in db.TenantPlanes.Where(p => p.IdTenant == DatosBase.TenantB)) plan.VigenteHasta = plan.VigenteDesde.AddDays(1);
            await db.SaveChangesAsync();

            var tenant = await Servicio(db).DelTenantAsync(DatosBase.TenantB);

            Assert.NotNull(tenant);
            Assert.Null(tenant!.PlanCodigo);
            Assert.Empty(tenant.Derechos);
            Assert.False(new DerechosDeLaPeticion(tenant, new HashSet<string> { "flota" }).TieneModulo("flota"));
        }

        [Theory]
        [InlineData(EstadoTenant.Activo, ModoDeSuspension.Lectura, true, false, false)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, false, true, false)]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo, false, false, true)]
        [InlineData(EstadoTenant.Cancelado, ModoDeSuspension.Lectura, false, false, true)]
        public async Task El_estado_y_el_modo_dicen_si_opera_si_es_de_solo_lectura_o_si_esta_bloqueado(
            string estado, string modo, bool opera, bool soloLectura, bool bloqueado)
        {
            Sembrar();
            await using var db = _base.Crear();
            var t = db.Tenants.Single(x => x.IdTenant == DatosBase.TenantB);
            t.Estado = estado;
            t.ModoSuspension = modo;
            await db.SaveChangesAsync();

            var tenant = await Servicio(db).DelTenantAsync(DatosBase.TenantB);

            Assert.Equal(opera, tenant!.Opera);
            Assert.Equal(soloLectura, tenant.EnSoloLectura);
            Assert.Equal(bloqueado, tenant.Bloqueado);
        }

        [Fact]
        public async Task Los_modulos_de_la_empresa_son_los_activos_y_vigentes_de_un_modulo_activo()
        {
            Sembrar();
            await using var db = _base.Crear();
            var hoy = new DateTime(2026, 10, 1);
            var facturacion = db.Modulos.Single(m => m.Codigo == "facturacion");   // módulo apagado en el catálogo
            var catalogos = db.Modulos.Single(m => m.Codigo == "catalogos");
            // Catálogos de la empresa A vence ayer; facturación está activada pero el módulo está apagado en el catálogo.
            db.EmpresaModulos.Single(em => em.IdEmpresa == DatosBase.EmpresaA && em.IdModulo == catalogos.IdModulo).FechaVencimiento = hoy.AddDays(-1);
            db.EmpresaModulos.Add(new EmpresaModulo { IdEmpresa = DatosBase.EmpresaA, IdModulo = facturacion.IdModulo, FechaActivacion = hoy, Activo = true });
            await db.SaveChangesAsync();

            var modulos = await Servicio(db).ModulosActivosDeLaEmpresaAsync(DatosBase.EmpresaA);

            Assert.Equal(new[] { "flota" }, modulos.OrderBy(m => m).ToArray());
        }

        [Fact]
        public async Task El_dia_del_vencimiento_el_modulo_todavia_vale_y_sin_vencimiento_siempre_vale()
        {
            Sembrar();
            await using var db = _base.Crear();
            var hoy = new DateTime(2026, 10, 1);
            var catalogos = db.Modulos.Single(m => m.Codigo == "catalogos");
            db.EmpresaModulos.Single(em => em.IdEmpresa == DatosBase.EmpresaA && em.IdModulo == catalogos.IdModulo).FechaVencimiento = hoy;
            await db.SaveChangesAsync();

            var modulos = await Servicio(db).ModulosActivosDeLaEmpresaAsync(DatosBase.EmpresaA);

            Assert.Contains("catalogos", modulos);
            Assert.Contains("flota", modulos);
        }

        [Fact]
        public async Task Tener_el_modulo_exige_que_el_plan_lo_conceda_y_que_la_empresa_lo_tenga_activo()
        {
            Sembrar();
            await using var db = _base.Crear();
            var servicio = Servicio(db);

            // Tenant A: el plan solo concede flota, aunque su empresa tenga activados flota y catálogos.
            var a = new DerechosDeLaPeticion((await servicio.DelTenantAsync(DatosBase.TenantA))!, await servicio.ModulosActivosDeLaEmpresaAsync(DatosBase.EmpresaA));
            Assert.True(a.TieneModulo("flota"));
            Assert.False(a.TieneModulo("catalogos"));

            // Tenant B: el plan concede catálogos, pero su empresa no lo activó.
            var b = new DerechosDeLaPeticion((await servicio.DelTenantAsync(DatosBase.TenantB))!, await servicio.ModulosActivosDeLaEmpresaAsync(DatosBase.EmpresaB));
            Assert.True(b.TieneModulo("flota"));
            Assert.False(b.TieneModulo("catalogos"));
            Assert.True(b.TieneModulo(" FLOTA "));   // sin importar mayúsculas ni espacios
        }

        [Fact]
        public async Task Lo_leido_se_guarda_en_memoria_hasta_que_se_invalida()
        {
            Sembrar();
            await using var db = _base.Crear();
            var servicio = Servicio(db);
            Assert.True((await servicio.DelTenantAsync(DatosBase.TenantA))!.Opera);

            // Otro contexto suspende al tenant; el servicio sigue viendo lo guardado hasta que se invalide.
            await using (var otro = _base.Crear())
            {
                var t = otro.Tenants.Single(x => x.IdTenant == DatosBase.TenantA);
                t.Estado = EstadoTenant.Suspendido;
                await otro.SaveChangesAsync();
            }

            Assert.True((await servicio.DelTenantAsync(DatosBase.TenantA))!.Opera);

            servicio.Invalidar(DatosBase.TenantA);
            Assert.False((await servicio.DelTenantAsync(DatosBase.TenantA))!.Opera);
        }

        [Fact]
        public async Task Los_modulos_de_la_empresa_tambien_se_guardan_en_memoria_y_se_invalidan_con_la_empresa()
        {
            Sembrar();
            await using var db = _base.Crear();
            var servicio = Servicio(db);
            Assert.Contains("catalogos", await servicio.ModulosActivosDeLaEmpresaAsync(DatosBase.EmpresaA));

            await using (var otro = _base.Crear())
            {
                var catalogos = otro.Modulos.Single(m => m.Codigo == "catalogos");
                otro.EmpresaModulos.Single(em => em.IdEmpresa == DatosBase.EmpresaA && em.IdModulo == catalogos.IdModulo).Activo = false;
                await otro.SaveChangesAsync();
            }

            Assert.Contains("catalogos", await servicio.ModulosActivosDeLaEmpresaAsync(DatosBase.EmpresaA));
            servicio.Invalidar(DatosBase.TenantA, DatosBase.EmpresaA);
            Assert.DoesNotContain("catalogos", await servicio.ModulosActivosDeLaEmpresaAsync(DatosBase.EmpresaA));
        }

        [Fact]
        public async Task El_limite_de_empresas_cuenta_las_empresas_del_tenant_y_no_deja_pasar_del_tope()
        {
            Sembrar();
            // Tenant A: dos empresas (A y A2) y un tope de 1.
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var r = await Servicio(db).PuedeAgregarAsync(DatosBase.TenantA, CodigosDeDerecho.LimiteEmpresas);

            Assert.False(r.Permitido);
            Assert.Equal(1, r.Tope);
            Assert.Equal(2, r.EnUso);
            Assert.Contains("hasta 1 empresas", r.Mensaje("empresas"));
        }

        [Fact]
        public async Task Sin_tope_siempre_se_puede_agregar()
        {
            Sembrar();
            await using var db = _base.CrearComo(DatosBase.TenantB);

            var r = await Servicio(db).PuedeAgregarAsync(DatosBase.TenantB, CodigosDeDerecho.LimiteUsuarios);

            Assert.True(r.Permitido);
            Assert.Null(r.Tope);
            Assert.Equal(string.Empty, r.Mensaje("usuarios"));
        }

        [Fact]
        public async Task El_limite_de_usuarios_cuenta_los_activos_de_las_empresas_del_tenant()
        {
            Sembrar();
            await using (var db = _base.Crear())
            {
                db.Users.AddRange(
                    NuevoUsuario("a1", DatosBase.EmpresaA, activo: true),
                    NuevoUsuario("a2", DatosBase.EmpresaA2, activo: true),      // otra empresa del mismo tenant
                    NuevoUsuario("a3", DatosBase.EmpresaA, activo: false),      // inactivo: no cuenta
                    NuevoUsuario("b1", DatosBase.EmpresaB, activo: true));      // de otro tenant: no cuenta
                await db.SaveChangesAsync();
            }

            await using var lectura = _base.CrearComo(DatosBase.TenantA);
            var r = await Servicio(lectura).PuedeAgregarAsync(DatosBase.TenantA, CodigosDeDerecho.LimiteUsuarios);

            Assert.Equal(2, r.EnUso);
            Assert.Equal(2, r.Tope);
            Assert.False(r.Permitido);
        }

        [Fact]
        public async Task Un_limite_que_no_se_aplica_hoy_se_rechaza()
        {
            Sembrar();
            await using var db = _base.Crear();

            await Assert.ThrowsAsync<ArgumentException>(() => Servicio(db).PuedeAgregarAsync(DatosBase.TenantA, "limite.vehiculos"));
        }

        private static User NuevoUsuario(string nombre, int idEmpresa, bool activo) => new()
        {
            Username = nombre, Email = nombre + "@example.test", Password = "$2a$11$ficticio", IsActive = activo, Role = "empresa_user",
            EmpresaId = idEmpresa, CreatedAt = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)
        };
    }
}
