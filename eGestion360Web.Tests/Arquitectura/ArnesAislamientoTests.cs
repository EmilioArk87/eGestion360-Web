using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using eGestion360Web.Data;
using Xunit.Abstractions;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// Arnés de aislamiento (paso F0.6). Recorre el modelo de EF que arma la aplicación y lo compara con
    /// <see cref="ClasificacionDeTablas"/>:
    ///   * Desde ya, toda tabla nueva tiene que nacer clasificada (tenant, mixta, global…): la decisión de aislamiento se
    ///     toma al crearla, no después.
    ///   * La lista de tablas que aún no tienen <c>id_tenant</c> solo puede bajar. En F0 son todas; en F1 se van quitando a
    ///     medida que cada grupo de tablas recibe la columna, hasta que la lista queda vacía y la regla pasa a ser total.
    ///   * Cuando una tabla ya tiene <c>id_tenant</c>, su nulabilidad tiene que ser la de su categoría.
    /// No necesita base de datos: lee el modelo, no la base.
    /// </summary>
    public class ArnesAislamientoTests
    {
        private const string ColumnaTenant = "id_tenant";

        private readonly ITestOutputHelper _salida;

        public ArnesAislamientoTests(ITestOutputHelper salida) => _salida = salida;

        /// <summary>Tabla → entidad del modelo de EF, tal como lo arma la aplicación (proveedor SQL Server, sin conectarse).</summary>
        private static readonly Lazy<IReadOnlyDictionary<string, IEntityType>> _modelo = new(() =>
        {
            var opciones = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=.;Database=modelo").Options;
            using var db = new ApplicationDbContext(opciones);
            return db.Model.GetEntityTypes()
                .Where(e => e.GetTableName() != null && !e.IsOwned())
                .GroupBy(e => e.GetTableName()!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        });

        private static IReadOnlyDictionary<string, IEntityType> Modelo => _modelo.Value;

        private static Dictionary<string, TablaClasificada> Clasificacion()
            => ClasificacionDeTablas.Todas.ToDictionary(t => t.Tabla, StringComparer.Ordinal);

        private static IProperty? PropiedadTenant(IEntityType entidad)
            => entidad.GetProperties().FirstOrDefault(p => p.GetColumnName() == ColumnaTenant);

        private static bool TieneColumna(IEntityType entidad, params string[] columnas)
            => entidad.GetProperties().Any(p => columnas.Contains(p.GetColumnName()));

        [Fact]
        public void Cada_tabla_aparece_una_sola_vez_en_la_clasificacion()
        {
            var repetidas = ClasificacionDeTablas.Todas.GroupBy(t => t.Tabla, StringComparer.Ordinal)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();

            Assert.True(repetidas.Count == 0, $"Tablas clasificadas más de una vez: {string.Join(", ", repetidas)}");
        }

        [Fact]
        public void Toda_tabla_del_modelo_de_EF_esta_clasificada()
        {
            var clasificacion = Clasificacion();
            var sinClasificar = Modelo.Keys.Where(t => !clasificacion.ContainsKey(t)).OrderBy(t => t).ToList();

            Assert.True(sinClasificar.Count == 0,
                "Estas tablas del modelo de EF no tienen regla de aislamiento: " + string.Join(", ", sinClasificar) +
                ". Agrégalas a eGestion360Web.Tests/Arquitectura/ClasificacionDeTablas.cs con su categoría (Tenant, Mixta, " +
                "Infraestructura, Global, Plataforma o Legado). Una tabla de tenant nueva debe nacer con id_tenant.");
        }

        [Fact]
        public void La_clasificacion_coincide_con_el_modelo_de_EF()
        {
            var faltan = ClasificacionDeTablas.Todas.Where(t => t.EnModeloEf && !Modelo.ContainsKey(t.Tabla)).Select(t => t.Tabla).ToList();
            var sobran = ClasificacionDeTablas.Todas.Where(t => !t.EnModeloEf && Modelo.ContainsKey(t.Tabla)).Select(t => t.Tabla).ToList();

            Assert.True(faltan.Count == 0,
                $"Marcadas como mapeadas por EF, pero el modelo ya no las tiene: {string.Join(", ", faltan)}. Corrige EnModeloEf o quítalas.");
            Assert.True(sobran.Count == 0,
                $"Marcadas como «solo en la base», pero EF ya las mapea: {string.Join(", ", sobran)}. Corrige EnModeloEf y revisa su categoría.");
        }

        [Fact]
        public void Las_tablas_sin_id_tenant_solo_pueden_disminuir()
        {
            var clasificacion = Clasificacion();
            var sinIdTenant = Modelo
                .Where(m => clasificacion.TryGetValue(m.Key, out var c) && ClasificacionDeTablas.LlevaIdTenant(c.Categoria))
                .Where(m => PropiedadTenant(m.Value) == null)
                .Select(m => m.Key)
                .ToHashSet(StringComparer.Ordinal);

            var nuevas = sinIdTenant.Except(ClasificacionDeTablas.PendientesDeIdTenant).OrderBy(t => t).ToList();
            var resueltas = ClasificacionDeTablas.PendientesDeIdTenant.Except(sinIdTenant).OrderBy(t => t).ToList();

            Assert.True(nuevas.Count == 0,
                $"Tablas de tenant nuevas sin id_tenant: {string.Join(", ", nuevas)}. Toda tabla de tenant nueva debe nacer con la columna.");
            Assert.True(resueltas.Count == 0,
                $"Estas tablas ya tienen id_tenant o dejaron de necesitarlo: {string.Join(", ", resueltas)}. " +
                "Quítalas de ClasificacionDeTablas.PendientesDeIdTenant para que no vuelvan atrás.");
        }

        [Fact]
        public void Donde_ya_existe_id_tenant_tiene_la_nulabilidad_de_su_categoria()
        {
            var problemas = new List<string>();
            foreach (var (tabla, c) in Clasificacion())
            {
                if (!Modelo.TryGetValue(tabla, out var entidad)) continue;
                var propiedad = PropiedadTenant(entidad);
                if (propiedad == null) continue;

                if (!ClasificacionDeTablas.LlevaIdTenant(c.Categoria))
                    problemas.Add($"{tabla} es {c.Categoria} y no debería tener id_tenant");
                else if (propiedad.IsNullable != ClasificacionDeTablas.IdTenantNulable(c.Categoria))
                    problemas.Add($"{tabla} es {c.Categoria}: id_tenant debe ser {(ClasificacionDeTablas.IdTenantNulable(c.Categoria) ? "nulable" : "obligatorio")}");
            }

            Assert.True(problemas.Count == 0, string.Join("; ", problemas));
        }

        /// <summary>No falla nunca: deja en la salida de la prueba el estado del aislamiento, para seguir el avance de F1.</summary>
        [Fact]
        public void Informe_de_aislamiento()
        {
            var tablas = ClasificacionDeTablas.Todas;
            var texto = new StringBuilder();
            texto.AppendLine($"Tablas clasificadas: {tablas.Count} ({Modelo.Count} en el modelo de EF, " +
                             $"{tablas.Count(t => t.EnBaseReal)} en eBD_SPD al 2026-10-10).");

            foreach (var grupo in tablas.GroupBy(t => t.Categoria).OrderBy(g => g.Key))
                texto.AppendLine($"  {grupo.Key,-16} {grupo.Count(),3}");

            texto.AppendLine($"Pendientes de id_tenant: {ClasificacionDeTablas.PendientesDeIdTenant.Count}.");

            var sinEmpresa = tablas
                .Where(t => t.Categoria == CategoriaTabla.Tenant && Modelo.TryGetValue(t.Tabla, out var e) && !TieneColumna(e, "id_empresa", "EmpresaId"))
                .Select(t => t.Tabla).OrderBy(t => t).ToList();
            texto.AppendLine($"Tablas de tenant que hoy no llevan ni id_empresa (el filtro por empresa no las alcanza directo): {string.Join(", ", sinEmpresa)}.");

            var fueraDeEf = tablas.Where(t => !t.EnModeloEf && ClasificacionDeTablas.LlevaIdTenant(t.Categoria))
                .Select(t => t.Tabla).OrderBy(t => t).ToList();
            texto.AppendLine($"Tablas de tenant o mixtas fuera del modelo de EF (solo las protege RLS, no los filtros de EF): {string.Join(", ", fueraDeEf)}.");

            var legado = tablas.Where(t => t.Categoria == CategoriaTabla.Legado).Select(t => t.Tabla).OrderBy(t => t).ToList();
            texto.AppendLine($"Legado a decidir en F0.7: {string.Join(", ", legado)}.");

            _salida.WriteLine(texto.ToString());
        }
    }
}
