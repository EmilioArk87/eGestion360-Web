using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using eGestion360Web.Data;
using eGestion360Web.Tests.Infra;
using static eGestion360Web.Tests.Infra.FilasDePrueba;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Paso F1.3c (ADR-022): el filtro global de borrado lógico «borrado» está en las entidades con <c>Eliminado</c> que ninguna
    /// otra referencia (hojas: cargas, peajes, documentos…). Las que otras referencian (vehículo, persona, cliente…) no lo llevan:
    /// con él, el historial que apunta a una fila borrada desaparecería de las consultas que la incluyen. Esta prueba fija las dos
    /// listas para que un cambio en el modelo (una relación nueva hacia una hoja, una entidad nueva con Eliminado) sea una
    /// decisión visible y no un efecto escondido.
    /// </summary>
    public class FiltroDeBorradoTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private static readonly string[] HojasConFiltro =
        {
            "cargas_combustible", "cargos", "control_salidas", "empleados", "gastos_repuestos", "notas", "odometro_diario",
            "ordenes_mantenimiento", "peajes", "persona_documentos", "polizas_seguros", "proveedores", "salarios_diarios",
        };

        private static readonly string[] PrincipalesSinFiltro =
        {
            "categorias_repuesto", "clientes", "condiciones_pago", "ct_asientos", "ct_centros_costo", "ct_cuentas", "empresas",
            "facturas", "formas_pago", "impuestos", "pagos", "persona_empresa", "personas", "productos_servicios", "rutas",
            "talleres", "tasas_cambio", "tipos_vehiculo", "vehiculos",
        };

        private static IEnumerable<IEntityType> ConBorrado(ApplicationDbContext db) =>
            db.Model.GetEntityTypes().Where(e => !e.IsOwned() && e.BaseType == null && e.FindProperty("Eliminado")?.ClrType == typeof(bool));

        private static bool TieneFiltro(IEntityType e) => e.GetDeclaredQueryFilters().Any(f => f.Key == ApplicationDbContext.FiltroBorrado);

        [Fact]
        public void El_filtro_de_borrado_esta_en_las_hojas_y_no_en_las_entidades_que_otras_referencian()
        {
            using var db = _bd.CrearComo(DatosBase.TenantA);
            var entidades = ConBorrado(db).ToList();

            var conFiltro = entidades.Where(TieneFiltro).Select(e => e.GetTableName()!).Order().ToList();
            var sinFiltro = entidades.Where(e => !TieneFiltro(e)).Select(e => e.GetTableName()!).Order().ToList();

            Assert.Equal(HojasConFiltro.Order().ToList(), conFiltro);
            Assert.Equal(PrincipalesSinFiltro.Order().ToList(), sinFiltro);
        }

        [Fact]
        public void Una_entidad_con_el_filtro_de_borrado_no_puede_ser_referenciada_por_otra()
        {
            using var db = _bd.CrearComo(DatosBase.TenantA);

            var referenciadas = ConBorrado(db).Where(TieneFiltro)
                .Where(e => e.GetReferencingForeignKeys().Any())
                .Select(e => e.GetTableName()!).ToList();

            Assert.True(referenciadas.Count == 0,
                "Estas entidades llevan el filtro de borrado y ahora las referencia otra: el historial que apunta a una fila borrada " +
                "desaparecería de las consultas con Include. Quítales el filtro (ADR-022): " + string.Join(", ", referenciadas));
        }

        [Fact]
        public void El_filtro_oculta_lo_borrado_se_puede_ignorar_solo_el_de_borrado_y_el_de_tenant_sigue()
        {
            var fallos = new List<string>();
            var probadas = new List<Type>();

            using (var datos = _bd.Crear())
            {
                datos.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");
                foreach (var entidad in ConBorrado(datos).Where(TieneFiltro).ToList())
                {
                    var tipo = entidad.ClrType;
                    try
                    {
                        var viva = Fabricar(entidad, DatosBase.TenantA, DatosBase.EmpresaA, "V", 11);
                        var borrada = Fabricar(entidad, DatosBase.TenantA, DatosBase.EmpresaA, "B", 12);
                        var deOtro = Fabricar(entidad, DatosBase.TenantB, DatosBase.EmpresaB, "O", 13);
                        tipo.GetProperty("Eliminado")!.SetValue(borrada, true);
                        FijarClavesSinGenerar(entidad, viva, 11);
                        FijarClavesSinGenerar(entidad, borrada, 12);
                        FijarClavesSinGenerar(entidad, deOtro, 13);
                        datos.AddRange(viva, borrada, deOtro);
                        datos.SaveChanges();
                        probadas.Add(tipo);
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
            var problemas = new List<string>();
            foreach (var tipo in probadas)
            {
                bool Borrada(object f) => (bool)tipo.GetProperty("Eliminado")!.GetValue(f)!;
                var normales = Filas(comoA, tipo);
                var sinElFiltroDeBorrado = Filas(comoA, tipo, new[] { ApplicationDbContext.FiltroBorrado });
                var sinNingunFiltro = Filas(comoA, tipo, new[] { ApplicationDbContext.FiltroBorrado, ApplicationDbContext.FiltroTenant });

                if (normales.Any(Borrada)) problemas.Add($"{tipo.Name}: una consulta normal devuelve filas borradas");
                if (!normales.Any()) problemas.Add($"{tipo.Name}: una consulta normal no devuelve la fila viva");
                if (!sinElFiltroDeBorrado.Any(Borrada)) problemas.Add($"{tipo.Name}: ignorar el filtro de borrado no devuelve la fila borrada");
                if (sinElFiltroDeBorrado.Any(f => TenantDe(f) != DatosBase.TenantA)) problemas.Add($"{tipo.Name}: ignorar el de borrado dejó ver otro tenant");
                if (!sinNingunFiltro.Any(f => TenantDe(f) == DatosBase.TenantB)) problemas.Add($"{tipo.Name}: ignorar los dos filtros no devuelve el otro tenant");
            }

            Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));
            Assert.Equal(HojasConFiltro.Length, probadas.Count);
        }

        [Fact]
        public void Las_entidades_que_otras_referencian_siguen_viendo_sus_filas_borradas_para_no_perder_historial()
        {
            using var datos = _bd.Crear();
            datos.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");
            var entidad = datos.Model.FindEntityType(typeof(eGestion360Web.Models.Flota.Vehiculo))!;
            var borrado = Fabricar(entidad, DatosBase.TenantA, DatosBase.EmpresaA, "X");
            typeof(eGestion360Web.Models.Flota.Vehiculo).GetProperty("Eliminado")!.SetValue(borrado, true);
            datos.Add(borrado);
            datos.SaveChanges();

            using var comoA = _bd.CrearComo(DatosBase.TenantA);
            Assert.Contains(comoA.Set<eGestion360Web.Models.Flota.Vehiculo>().ToList(), v => v.Eliminado);
        }
    }
}
