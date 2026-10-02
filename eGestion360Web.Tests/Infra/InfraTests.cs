using Microsoft.EntityFrameworkCore;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests
{
    /// <summary>Comprueba que la base de prueba (SQLite) arranca con el modelo completo de la aplicación.</summary>
    public class InfraTests
    {
        [Fact]
        public async Task La_base_de_prueba_se_crea_con_los_catalogos_y_las_empresas()
        {
            using var bd = new BaseDeDatosDePrueba();
            using var db = bd.Crear();

            Assert.Equal(3, await db.Paises.CountAsync());
            Assert.Equal(4, await db.CatalogoTiposDocumento.CountAsync());
            Assert.Equal(9, await db.CatalogoTiposLicencia.CountAsync());
            Assert.Equal(2, await db.CatalogoMunicipios.CountAsync());
            Assert.Equal(2, await db.Empresas.CountAsync());
            Assert.Equal(10, await db.Cargos.CountAsync());
            Assert.False((await db.Monedas.FirstAsync(m => m.CodigoIso == "ANG")).Activo);
        }
    }
}
