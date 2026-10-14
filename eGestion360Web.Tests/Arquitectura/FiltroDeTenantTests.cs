using System.Collections;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Tests.Infra;
using static eGestion360Web.Tests.Infra.FilasDePrueba;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.3: el filtro global por tenant de EF cubre TODAS las entidades de tenant. La parte estructural lee el modelo;
    /// la de datos inserta una fila de cada entidad para dos tenants y comprueba, con contextos que llevan el filtro, que
    /// cada tenant solo ve lo suyo, que una petición sin tenant no ve nada de tenant y que la plataforma lo ve todo.
    /// </summary>
    public class FiltroDeTenantTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private static IEnumerable<IEntityType> EntidadesDeTenant(ApplicationDbContext db) =>
            db.Model.GetEntityTypes().Where(e => !e.IsOwned() && e.BaseType == null
                && (typeof(ITenantEntity).IsAssignableFrom(e.ClrType) || typeof(ITenantOpcionalEntity).IsAssignableFrom(e.ClrType)));

        private static bool EsMixta(Type tipo) => typeof(ITenantOpcionalEntity).IsAssignableFrom(tipo);

        [Fact]
        public void Toda_entidad_de_tenant_tiene_el_filtro_tenant_y_ninguna_otra_lo_tiene()
        {
            using var db = _bd.CrearComo(DatosBase.TenantA);

            var conFiltro = db.Model.GetEntityTypes()
                .Where(e => e.GetDeclaredQueryFilters().Any(f => f.Key == ApplicationDbContext.FiltroTenant))
                .Select(e => e.ClrType).ToHashSet();
            var deTenant = EntidadesDeTenant(db).Select(e => e.ClrType).ToHashSet();

            Assert.Empty(deTenant.Except(conFiltro).Select(t => t.Name));
            Assert.Empty(conFiltro.Except(deTenant).Select(t => t.Name));
            Assert.True(deTenant.Count >= 44, $"Se esperaban al menos 44 entidades de tenant y hay {deTenant.Count}.");
        }

        [Fact]
        public void Las_tablas_que_el_arnes_clasifica_como_de_tenant_son_justo_las_que_llevan_el_filtro()
        {
            using var db = _bd.CrearComo(DatosBase.TenantA);
            var tablasConFiltro = EntidadesDeTenant(db).Select(e => e.GetTableName()!).ToHashSet(StringComparer.Ordinal);
            var tablasDelArnes = ClasificacionDeTablas.Todas
                .Where(t => t.EnModeloEf && ClasificacionDeTablas.LlevaIdTenant(t.Categoria))
                .Select(t => t.Tabla).ToHashSet(StringComparer.Ordinal);

            Assert.Empty(tablasDelArnes.Except(tablasConFiltro));
            Assert.Empty(tablasConFiltro.Except(tablasDelArnes));
        }

        // ── Datos: dos tenants, todas las entidades ─────────────────────────

        [Fact]
        public void Con_dos_tenants_cada_contexto_ve_solo_lo_suyo_en_cada_entidad_de_tenant()
        {
            var fallos = new List<string>();
            var insertadas = new List<Type>();

            // Se arma con un contexto sin filtro y sin claves foráneas: lo que se prueba es el filtro, no el modelo relacional.
            using (var datos = _bd.Crear())
            {
                datos.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");

                foreach (var entidad in EntidadesDeTenant(datos).ToList())
                {
                    var tipo = entidad.ClrType;
                    try
                    {
                        var a = Fabricar(entidad, DatosBase.TenantA, DatosBase.EmpresaA, "A");
                        var b = Fabricar(entidad, DatosBase.TenantB, DatosBase.EmpresaB, "B");
                        FijarClavesSinGenerar(entidad, a, DatosBase.TenantA);
                        FijarClavesSinGenerar(entidad, b, DatosBase.TenantB);
                        datos.AddRange(a, b);

                        if (EsMixta(tipo))
                        {
                            var global = Fabricar(entidad, DatosBase.TenantA, DatosBase.EmpresaA, "G");
                            ((ITenantOpcionalEntity)global).IdTenant = null;
                            tipo.GetProperty("IdEmpresa")?.SetValue(global, null);
                            FijarClavesSinGenerar(entidad, global, 99);
                            datos.Add(global);
                        }

                        datos.SaveChanges();
                        insertadas.Add(tipo);
                    }
                    catch (Exception ex)
                    {
                        fallos.Add($"{tipo.Name}: {ex.GetBaseException().Message}");
                        datos.ChangeTracker.Clear();
                    }
                }
            }

            Assert.True(fallos.Count == 0, "No se pudo armar una fila de: " + string.Join(" | ", fallos));

            using var comoA = _bd.CrearComo(DatosBase.TenantA);
            using var comoB = _bd.CrearComo(DatosBase.TenantB);
            using var sinTenant = _bd.CrearComo(null);
            using var plataforma = _bd.CrearComo(null, plataforma: true);

            var problemas = new List<string>();
            foreach (var tipo in insertadas)
            {
                var mixta = EsMixta(tipo);
                var deA = Filas(comoA, tipo).Select(TenantDe).ToList();
                var deB = Filas(comoB, tipo).Select(TenantDe).ToList();
                var deNadie = Filas(sinTenant, tipo).Select(TenantDe).ToList();
                var deTodos = Filas(plataforma, tipo).Select(TenantDe).ToList();

                if (!deA.Contains(DatosBase.TenantA) || deA.Any(t => t != DatosBase.TenantA && !(mixta && t == null)))
                    problemas.Add($"{tipo.Name}: el tenant A no ve solo lo suyo ({string.Join(",", deA.Distinct())})");
                if (!deB.Contains(DatosBase.TenantB) || deB.Any(t => t != DatosBase.TenantB && !(mixta && t == null)))
                    problemas.Add($"{tipo.Name}: el tenant B no ve solo lo suyo ({string.Join(",", deB.Distinct())})");
                if (mixta ? deNadie.Count == 0 || deNadie.Any(t => t != null) : deNadie.Count != 0)
                    problemas.Add($"{tipo.Name}: sin tenant ve {deNadie.Count} filas ({string.Join(",", deNadie.Distinct())})");
                if (!deTodos.Contains(DatosBase.TenantA) || !deTodos.Contains(DatosBase.TenantB))
                    problemas.Add($"{tipo.Name}: la plataforma no ve los dos tenants");
                if (mixta && !deA.Contains(null) || mixta && !deB.Contains(null))
                    problemas.Add($"{tipo.Name}: una tabla mixta debe dejar ver la fila global a todos los tenants");
            }

            Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));
            Assert.True(insertadas.Count >= 44, $"Solo se probaron {insertadas.Count} entidades.");
        }
    }
}
