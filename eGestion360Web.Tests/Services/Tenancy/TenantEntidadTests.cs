using Microsoft.EntityFrameworkCore;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Tenancy
{
    /// <summary>
    /// La entidad <see cref="Tenant"/> y sus restricciones (script 025). SQLite aplica los CHECK, los índices únicos (también
    /// el filtrado) y las claves foráneas; el resto lo comprueban <see cref="SqlServer.Tenancy.TenantsSqlServerTests"/>
    /// y el POSTCHECK del script.
    /// </summary>
    public class TenantEntidadTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private static Tenant Nuevo(string codigo, string tipo = TipoTenant.Cliente, string estado = EstadoTenant.Activo) => new()
        {
            UidPublico = Guid.NewGuid(), Codigo = codigo, Nombre = "Tenant " + codigo, Tipo = tipo, Estado = estado,
            FechaEstado = DateTime.UtcNow, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
        };

        private void AssertRechaza(Action<Microsoft.EntityFrameworkCore.DbContext> cambio)
        {
            using var db = _bd.Crear();
            Assert.Throws<DbUpdateException>(() => cambio(db));
        }

        [Fact]
        public void Un_tenant_valido_se_guarda_y_recibe_su_id()
        {
            using var db = _bd.Crear();
            var tenant = Nuevo("NUEVO");

            db.Tenants.Add(tenant);
            db.SaveChanges();

            Assert.True(tenant.IdTenant > 0);
            Assert.NotEqual(Guid.Empty, tenant.UidPublico);
        }

        [Fact]
        public void El_tipo_tiene_que_ser_uno_de_los_permitidos() =>
            AssertRechaza(db => { db.Set<Tenant>().Add(Nuevo("X1", tipo: "otro")); db.SaveChanges(); });

        [Fact]
        public void El_estado_tiene_que_ser_uno_de_los_permitidos() =>
            AssertRechaza(db => { db.Set<Tenant>().Add(Nuevo("X2", estado: "raro")); db.SaveChanges(); });

        [Fact]
        public void El_codigo_no_se_repite() =>
            AssertRechaza(db => { db.Set<Tenant>().Add(Nuevo("TENANT_A")); db.SaveChanges(); });

        [Fact]
        public void El_uid_publico_no_se_repite()
        {
            using var db = _bd.Crear();
            var existente = db.Tenants.First().UidPublico;
            var repetido = Nuevo("OTRO");
            repetido.UidPublico = existente;
            db.Tenants.Add(repetido);

            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        [Fact]
        public void Solo_puede_haber_un_tenant_interno()
        {
            // DatosBase ya tiene uno (TENANT_A, interno): un segundo interno se rechaza, pero muchos de clientes o demo se aceptan.
            AssertRechaza(db => { db.Set<Tenant>().Add(Nuevo("SEGUNDO_INTERNO", tipo: TipoTenant.Interno)); db.SaveChanges(); });

            using var db = _bd.Crear();
            db.Tenants.AddRange(Nuevo("C1"), Nuevo("C2"), Nuevo("D1", tipo: TipoTenant.Demo), Nuevo("D2", tipo: TipoTenant.Demo));
            db.SaveChanges();
            Assert.Equal(6, db.Tenants.Count());
        }

        [Fact]
        public void Una_empresa_no_puede_apuntar_a_un_tenant_que_no_existe() =>
            AssertRechaza(db =>
            {
                db.Set<eGestion360Web.Models.Empresa>().Add(new eGestion360Web.Models.Empresa
                {
                    IdEmpresa = 77, IdTenant = 999, Codigo = "E77", RazonSocial = "Sin tenant", PaisIso = "HN", MonedaIso = "HNL",
                    ZonaHoraria = "America/Tegucigalpa", FechaActivacion = DateTime.UtcNow, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                });
                db.SaveChanges();
            });

        [Fact]
        public void Un_tenant_con_empresas_no_se_puede_borrar()
        {
            using var db = _bd.Crear();
            var tenant = db.Tenants.Single(t => t.IdTenant == DatosBase.TenantA);
            db.Tenants.Remove(tenant);

            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        [Fact]
        public void Las_constantes_de_tipo_y_estado_son_las_que_acepta_la_base()
        {
            Assert.Equal(new[] { "interno", "cliente", "demo" }, TipoTenant.Todos);
            Assert.Equal(new[] { "activo", "suspendido", "cancelado" }, EstadoTenant.Todos);
        }
    }
}
