using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using eGestion360Web.Services;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services
{
    public class EmpresaRequeridaTests
    {
        // ── AuthHelper.GetEmpresaIdRequerida ──────────────────────────────────

        [Theory]
        [InlineData("2", 2)]
        [InlineData("1", 1)]
        [InlineData("14", 14)]
        public void La_empresa_de_la_sesion_se_devuelve_tal_cual(string enSesion, int esperada) =>
            Assert.Equal(esperada, AuthHelper.GetEmpresaIdRequerida(SesionFalsa.Contexto(empresaId: enSesion)));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("-3")]
        [InlineData("abc")]
        public void Sin_empresa_valida_en_la_sesion_falla_en_lugar_de_usar_la_empresa_1(string? enSesion)
        {
            var http = SesionFalsa.Contexto(rol: "admin", empresaId: enSesion);

            Assert.Throws<InvalidOperationException>(() => AuthHelper.GetEmpresaIdRequerida(http));
        }

        // ── Qué rutas alcanza el filtro ───────────────────────────────────────

        [Theory]
        [InlineData("/Flota/Index")]
        [InlineData("/Flota/Catalogos/Vehiculos/Index")]
        [InlineData("/Flota/Catalogos/Vehiculos/Delete")]
        [InlineData("/Flota/Operacion/ControlSalidas/Historial")]
        [InlineData("/Flota/KPI/Dashboard")]
        [InlineData("/flota/gastos/seguros/create")]
        [InlineData("/Flota/UnaPantallaNuevaQueAunNoExiste")]
        public void Toda_pantalla_de_flota_exige_empresa(string ruta) =>
            Assert.True(EmpresaRequeridaPageFilter.Aplica(ruta));

        [Theory]
        [InlineData("/Flota/SinEmpresa")]
        [InlineData("/Flota/Catalogos/Personas/Index")]
        [InlineData("/Flota/Catalogos/Personas/Edit")]
        [InlineData("/MainMenu")]
        [InlineData("/Login")]
        [InlineData("/Admin/Personas/Index")]
        [InlineData("/Catalogos/Clientes/Index")]
        [InlineData("/FlotaOtraCosa")]
        [InlineData("")]
        [InlineData(null)]
        public void El_aviso_las_personas_y_el_resto_del_sistema_quedan_fuera(string? ruta) =>
            Assert.False(EmpresaRequeridaPageFilter.Aplica(ruta));

        // ── Qué hace el filtro con cada sesión ────────────────────────────────

        [Theory]
        [InlineData("admin")]
        [InlineData("empresa_admin")]
        [InlineData("empresa_user")]
        public void Quien_no_trae_empresa_va_al_aviso_y_el_handler_no_corre(string rol)
        {
            var contexto = Ejecutar("/Flota/Catalogos/Vehiculos/Index", SesionFalsa.Contexto(rol));

            var redireccion = Assert.IsType<RedirectToPageResult>(contexto.Result);
            Assert.Equal("/Flota/SinEmpresa", redireccion.PageName);
        }

        [Fact]
        public void Quien_no_inicio_sesion_va_al_login()
        {
            var contexto = Ejecutar("/Flota/KPI/Dashboard", SesionFalsa.Contexto(rol: null, autenticado: false));

            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(contexto.Result).PageName);
        }

        [Fact]
        public void Quien_debe_cambiar_la_clave_va_al_login_aunque_tenga_empresa()
        {
            var http = SesionFalsa.Contexto(empresaId: "2");
            http.Session.SetString("MustChangePassword", "1");

            var contexto = Ejecutar("/Flota/Operacion/Peajes/Create", http);

            Assert.Equal("/Login", Assert.IsType<RedirectToPageResult>(contexto.Result).PageName);
        }

        [Theory]
        [InlineData("admin", "2")]
        [InlineData("empresa_admin", "1")]
        [InlineData("empresa_user", "2")]
        public void Quien_trae_empresa_pasa_sin_desvio(string rol, string empresaId)
        {
            var contexto = Ejecutar("/Flota/Operacion/Peajes/Create", SesionFalsa.Contexto(rol, empresaId));

            Assert.Null(contexto.Result);
        }

        [Fact]
        public void Una_pagina_fuera_de_flota_no_se_toca_aunque_la_sesion_no_traiga_empresa()
        {
            var contexto = Ejecutar("/MainMenu", SesionFalsa.Contexto("admin"));

            Assert.Null(contexto.Result);
        }

        [Fact]
        public void Las_personas_se_las_arreglan_solas_y_el_filtro_no_interviene()
        {
            var contexto = Ejecutar("/Flota/Catalogos/Personas/Index", SesionFalsa.Contexto("admin"));

            Assert.Null(contexto.Result);
        }

        // ── Que no vuelva a aparecer el respaldo ──────────────────────────────

        [Fact]
        public void Ninguna_pagina_cae_en_la_empresa_1_cuando_la_sesion_no_trae_empresa()
        {
            var raiz = RaizDelRepositorio();
            // Lee EmpresaId de la sesión y, unas líneas después, devuelve el literal 1: el respaldo que se quitó.
            var respaldo = new Regex(@"GetString\(""EmpresaId""\)[\s\S]{0,250}?return\s+1\s*;", RegexOptions.Compiled);

            var culpables = Directory.EnumerateFiles(Path.Combine(raiz, "Pages"), "*.cs", SearchOption.AllDirectories)
                .Where(f => respaldo.IsMatch(File.ReadAllText(f)))
                .Select(f => Path.GetRelativePath(raiz, f))
                .ToList();

            Assert.True(culpables.Count == 0,
                "Estas páginas todavía caen en la empresa 1 si la sesión no trae empresa: " + string.Join(", ", culpables));
        }

        // ── Ayudas ────────────────────────────────────────────────────────────

        private static PageHandlerExecutingContext Ejecutar(string ruta, HttpContext http)
        {
            var descriptor = new CompiledPageActionDescriptor { ViewEnginePath = ruta, RelativePath = ruta + ".cshtml" };
            var pageContext = new PageContext(new ActionContext(http, new RouteData(), descriptor));
            var contexto = new PageHandlerExecutingContext(
                pageContext, Array.Empty<IFilterMetadata>(), new HandlerMethodDescriptor(),
                new Dictionary<string, object?>(), new object());

            new EmpresaRequeridaPageFilter().OnPageHandlerExecuting(contexto);
            return contexto;
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
