using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Pages;
using eGestion360Web.Services;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services
{
    /// <summary>
    /// La página que ve quien no puede entrar (sin permiso, sin módulo, cuenta suspendida o bloqueada): una sola página con el logo y el CSS
    /// adentro, que codifica todo lo que recibe y no pide nada de afuera.
    /// </summary>
    public class PaginaDeAccesoTests
    {
        private static DatosDePaginaDeAcceso Muestra(Action<List<LineaDePagina>>? ajustar = null)
        {
            var lineas = new List<LineaDePagina> { new("Tu rol no incluye: ", "Crear usuarios de la empresa", "."), new("Pide a quien administra tu empresa que lo agregue.") };
            ajustar?.Invoke(lineas);
            return new DatosDePaginaDeAcceso("No tienes permiso para esto", lineas, "administracion.usuarios.crear", "0HNPUS7K3OLG7:00000002");
        }

        [Fact]
        public void La_pagina_lleva_el_logo_el_titulo_el_mensaje_los_botones_y_las_referencias()
        {
            var html = PaginaDeAcceso.Html(Muestra());

            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.Contains("<html lang=\"es\">", html);
            Assert.Contains("aria-label=\"eGestión360\"", html);                         // el logo en línea
            Assert.Contains("<h1>No tienes permiso para esto</h1>", html);
            Assert.Contains("<title>No tienes permiso para esto — eGestión360</title>", html);
            Assert.Contains("Tu rol no incluye: <strong>Crear usuarios de la empresa</strong>.", html);
            Assert.Contains("<a class=\"btn p\" href=\"/MainMenu\">Volver al menú principal</a>", html);
            Assert.Contains("history.back()", html);
            Assert.Contains("Permiso <code>administracion.usuarios.crear</code>", html);
            Assert.Contains("Referencia <code>0HNPUS7K3OLG7:00000002</code>", html);
        }

        [Fact]
        public void Todo_lo_que_viene_de_afuera_se_codifica()
        {
            var datos = new DatosDePaginaDeAcceso(
                "<script>alert('t')</script>",
                new[] { new LineaDePagina("Motivo: ", "<img src=x onerror=alert(1)> & \"comillas\"", "<b>") },
                "a.b.c\"><script>", "ref<1>");

            var html = PaginaDeAcceso.Html(datos);

            Assert.DoesNotContain("<script>alert", html);
            Assert.DoesNotContain("<img src=x", html);
            Assert.DoesNotContain("<b>", html);
            Assert.DoesNotContain("\"><script>", html);
            Assert.DoesNotContain("ref<1>", html);
            Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
            Assert.Contains("&amp;", html);
        }

        [Fact]
        public void Los_acentos_salen_tal_cual_y_no_como_entidades()
        {
            var html = PaginaDeAcceso.Html(Muestra());

            Assert.Contains("Volver atrás", html);
            Assert.Contains("menú principal", html);
            Assert.DoesNotContain("&#", html);
        }

        [Fact]
        public void No_pide_nada_de_afuera_para_verse_igual_aunque_falle_todo_lo_demas()
        {
            var html = PaginaDeAcceso.Html(Muestra());

            Assert.DoesNotContain("<link", html);
            Assert.DoesNotContain("<script", html);
            Assert.DoesNotContain("<img", html);
            Assert.DoesNotContain("src=\"http", html);
            Assert.DoesNotContain("href=\"http", html);
            Assert.DoesNotMatch(@"url\((?!#)", html);                                      // solo se permite el degradado del propio logo (url(#…))
            Assert.DoesNotContain("@import", html);
        }

        [Fact]
        public void Sin_codigo_ni_referencia_no_hay_pie_y_sin_volver_atras_no_hay_el_segundo_boton()
        {
            var html = PaginaDeAcceso.Html(new DatosDePaginaDeAcceso("Titulo", new[] { new LineaDePagina("Texto") },
                TextoDelBotonPrincipal: "Cerrar sesión", EnlaceDelBotonPrincipal: "/Logout", OfrecerVolverAtras: false));

            Assert.DoesNotContain("class=\"pie\"", html);
            Assert.DoesNotContain("history.back()", html);
            Assert.Contains("<a class=\"btn p\" href=\"/Logout\">Cerrar sesión</a>", html);
        }

        [Fact]
        public void La_respuesta_trae_el_estado_y_el_tipo_de_contenido()
        {
            var r = PaginaDeAcceso.Respuesta(StatusCodes.Status403Forbidden, Muestra());

            Assert.Equal(StatusCodes.Status403Forbidden, r.StatusCode);
            Assert.Equal("text/html; charset=utf-8", r.ContentType);
            Assert.Contains("No tienes permiso para esto", r.Content);
        }

        // ── La página /SinAcceso (cuenta suspendida, bloqueada o cancelada) usa la misma página ─────────────────────────────

        private static SinAccesoModel SinAcceso(string estado, string modo, string? motivo)
        {
            var http = SesionFalsa.Contexto("empresa_user", "1");
            http.Items[EstadoDelTenantMiddleware.ClaveDerechos] = new DerechosDeLaPeticion(
                new DerechosDelTenant(2, "TRANSGAR", TipoTenant.Cliente, estado, modo, motivo, "flota", new HashSet<string>(), new Dictionary<string, int?>()),
                new HashSet<string>());
            return new SinAccesoModel { PageContext = new PageContext { HttpContext = http } };
        }

        [Fact]
        public void Suspendido_en_solo_lectura_explica_que_no_se_guardo_y_deja_volver_al_menu()
        {
            var r = Assert.IsType<ContentResult>(SinAcceso(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "Falta de pago").OnGet());

            Assert.Equal(StatusCodes.Status403Forbidden, r.StatusCode);
            Assert.Contains("Tu cuenta está en solo lectura", r.Content);
            Assert.Contains("TRANSGAR está suspendido", r.Content);
            Assert.Contains("El cambio que intentaste no se guardó.", r.Content);
            Assert.Contains("Motivo: <strong>Falta de pago</strong>", r.Content);
            Assert.Contains("href=\"/MainMenu\"", r.Content);
        }

        [Theory]
        [InlineData(EstadoTenant.Cancelado, ModoDeSuspension.Lectura, "cancelado")]
        [InlineData(EstadoTenant.Suspendido, ModoDeSuspension.Bloqueo, "suspendido")]
        public void Bloqueado_o_cancelado_solo_ofrece_cerrar_la_sesion(string estado, string modo, string enPalabras)
        {
            var r = Assert.IsType<ContentResult>(SinAcceso(estado, modo, null).OnGet());

            Assert.Equal(StatusCodes.Status403Forbidden, r.StatusCode);
            Assert.Contains("Tu cuenta no puede usar el sistema por ahora", r.Content);
            Assert.Contains($"TRANSGAR está {enPalabras}", r.Content);
            Assert.DoesNotContain("Motivo:", r.Content);
            Assert.Contains("href=\"/Logout\">Cerrar sesión</a>", r.Content);
            Assert.DoesNotContain("history.back()", r.Content);
        }

        [Fact]
        public void Con_todo_en_orden_responde_200_y_lo_dice()
        {
            var r = Assert.IsType<ContentResult>(SinAcceso(EstadoTenant.Activo, ModoDeSuspension.Lectura, null).OnGet());

            Assert.Equal(StatusCodes.Status200OK, r.StatusCode);
            Assert.Contains("Tu cuenta funciona con normalidad.", r.Content);
        }

        [Fact]
        public void El_motivo_que_escribe_un_operador_no_puede_inyectar_html()
        {
            var r = Assert.IsType<ContentResult>(SinAcceso(EstadoTenant.Suspendido, ModoDeSuspension.Lectura, "<script>alert(1)</script>").OnGet());

            Assert.DoesNotContain("<script>alert", r.Content);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", r.Content);
        }
    }
}
