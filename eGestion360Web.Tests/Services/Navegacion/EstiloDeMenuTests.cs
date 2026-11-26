using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Tests.Infra;
using eGestion360Web.Tests.Services.Identidad;

namespace eGestion360Web.Tests.Services.Navegacion
{
    /// <summary>
    /// La preferencia de estilo de menú (etapa 1 del plan secundario): vive en <c>saas.cuentas.estilo_menu</c>, viaja en la cookie y, si falta o no se
    /// reconoce, siempre es el hub.
    /// </summary>
    public class EstiloDeMenuTests
    {
        [Theory]
        [InlineData(null, "hub")]
        [InlineData("", "hub")]
        [InlineData("   ", "hub")]
        [InlineData("hub", "hub")]
        [InlineData("sidebar", "sidebar")]
        [InlineData("topbar", "topbar")]
        [InlineData(" SIDEBAR ", "sidebar")]
        [InlineData("doble_rail", "hub")]   // futuro: hasta que se habilite, cae al hub
        [InlineData("<script>", "hub")]
        public void Un_estilo_que_falta_o_no_se_reconoce_es_siempre_el_hub(string? valor, string esperado)
        {
            Assert.Equal(esperado, EstilosDeMenu.Normalizar(valor));
        }

        [Fact]
        public void La_lista_blanca_es_exacta_y_el_hub_es_el_predeterminado()
        {
            Assert.Equal(new[] { "hub", "sidebar", "topbar" }, EstilosDeMenu.Todos);
            Assert.True(EstilosDeMenu.EsValido("sidebar"));
            Assert.False(EstilosDeMenu.EsValido("Sidebar"));   // lo que se guarda va exacto; Normalizar es solo para leer
            Assert.False(EstilosDeMenu.EsValido(null));
            Assert.Equal(EstilosDeMenu.Hub, new Cuenta().EstiloMenu);
        }

        [Fact]
        public void El_estilo_viaja_en_la_cookie_y_se_lee_igual()
        {
            var datos = DatosDeIdentidadDePrueba.Empresa() with { EstiloMenu = EstilosDeMenu.Topbar };

            var leido = ReclamosDeIdentidad.Leer(ReclamosDeIdentidad.Crear(datos));

            Assert.Equal(datos, leido);
            Assert.Equal(EstilosDeMenu.Topbar, AuthHelper.GetEstiloMenu(SesionFalsa.ConIdentidad(datos)));
        }

        [Fact]
        public void Una_cookie_anterior_sin_el_estilo_o_con_un_valor_raro_da_el_hub()
        {
            var principal = ReclamosDeIdentidad.Crear(DatosDeIdentidadDePrueba.Empresa());
            var identidad = (ClaimsIdentity)principal.Identity!;
            identidad.RemoveClaim(identidad.FindFirst(ReclamosDeIdentidad.EstiloMenu));

            Assert.Equal(EstilosDeMenu.Hub, ReclamosDeIdentidad.Leer(principal)!.EstiloMenu);
            Assert.Equal(EstilosDeMenu.Hub, AuthHelper.GetEstiloMenu(new DefaultHttpContext { User = principal }));

            identidad.AddClaim(new Claim(ReclamosDeIdentidad.EstiloMenu, "morado"));
            Assert.Equal(EstilosDeMenu.Hub, AuthHelper.GetEstiloMenu(new DefaultHttpContext { User = principal }));
        }

        [Fact]
        public void Sin_sesion_el_estilo_es_el_hub()
        {
            Assert.Equal(EstilosDeMenu.Hub, AuthHelper.GetEstiloMenu(SesionFalsa.Contexto(autenticado: false)));
        }

        [Fact]
        public void El_script_048_permite_exactamente_los_estilos_de_la_aplicacion_y_su_valor_por_defecto_es_el_hub()
        {
            var script = File.ReadAllText(Path.Combine(RaizDelRepositorio(), "2 - Script SQL", "048_cuentas_estilo_menu.sql"));

            var check = Regex.Match(script, @"CHECK \(estilo_menu IN \((?<lista>[^)]*)\)\)");
            Assert.True(check.Success, "El script 048 no tiene el CHECK de estilo_menu.");
            var lista = check.Groups["lista"].Value.Replace("''", "'").Split(',').Select(v => v.Trim().Trim('\'')).ToArray();
            Assert.Equal(EstilosDeMenu.Todos, lista);
            Assert.Contains("estilo_menu VARCHAR(20) NOT NULL CONSTRAINT DF_cuentas_estilo_menu DEFAULT 'hub'", script, StringComparison.Ordinal);
            Assert.True(script.All(c => c < 128), "El script 048 debe ser ASCII puro.");
        }

        private static string RaizDelRepositorio()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;
            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }
    }
}
