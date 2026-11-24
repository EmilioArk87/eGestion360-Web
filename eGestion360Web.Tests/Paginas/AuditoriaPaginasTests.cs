using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;
using AuditoriaIndex = eGestion360Web.Pages.Admin.Auditoria.IndexModel;
using AuditoriaImprimir = eGestion360Web.Pages.Admin.Auditoria.ImprimirModel;
using PlataformaIndex = eGestion360Web.Pages.Admin.AuditoriaPlataforma.IndexModel;
using PlataformaImprimir = eGestion360Web.Pages.Admin.AuditoriaPlataforma.ImprimirModel;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// Paso F1.8c (ADR-030 D74): «Auditoría» de la empresa y «Auditoría de la plataforma». Solo leen; la del tenant muestra solo su tenant aunque
    /// el filtro global esté apagado (el operador), el correo sale enmascarado, las fechas son de Honduras y cada exportación (Excel o la hoja
    /// para imprimir, que el navegador guarda como PDF) deja su propia fila. SQLite en memoria; la RLS y la inmutabilidad las cubre el script 045.
    /// </summary>
    public class AuditoriaPaginasTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly AuditoriaEspia _auditoria = new();

        // 2026-11-20 08:00 en Honduras (UTC-6) = 14:00 UTC.
        private static readonly DateTime Jueves = new(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc);

        public void Dispose() => _bd.Dispose();

        private void Sembrar()
        {
            using var db = _bd.Crear();
            db.AuditoriaSeguridad.AddRange(
                Fila<AuditoriaSeguridad>(Jueves, TiposDeEvento.UsuarioEditado, "admin_a", detalle: "{\"correo\":\"maria.lopez@empresa.hn\",\"antes\":{\"rol\":3}}", aplicar: e => e.IdTenant = DatosBase.TenantA),
                Fila<AuditoriaSeguridad>(Jueves.AddHours(-1), TiposDeEvento.AccesoDenegado, "operador_a", resultado: ResultadosDeEvento.Denegado, aplicar: e => e.IdTenant = DatosBase.TenantA),
                // 2026-11-19 23:30 en Honduras, aunque en UTC ya es 20-nov: cuenta para el día 19.
                Fila<AuditoriaSeguridad>(new DateTime(2026, 11, 20, 5, 30, 0, DateTimeKind.Utc), TiposDeEvento.RolPermisos, "admin_a", aplicar: e => e.IdTenant = DatosBase.TenantA),
                Fila<AuditoriaSeguridad>(Jueves, TiposDeEvento.UsuarioCreado, "admin_b", aplicar: e => e.IdTenant = DatosBase.TenantB));
            db.AuditoriaPlataforma.AddRange(
                Fila<AuditoriaPlataforma>(Jueves, TiposDeEvento.SesionEntrada, "ju***@e***.hn", resultado: ResultadosDeEvento.Fallo),
                Fila<AuditoriaPlataforma>(Jueves.AddMinutes(5), TiposDeEvento.SoporteEntrada, "operador", aplicar: e => { e.IdTenant = DatosBase.TenantB; e.EsOperador = true; }),
                Fila<AuditoriaPlataforma>(Jueves.AddMinutes(6), TiposDeEvento.InvitacionAceptada, "pedro@empresa.hn"));
            db.SaveChanges();
        }

        private static T Fila<T>(DateTime utc, string tipo, string usuario, string resultado = ResultadosDeEvento.Ok, string? detalle = null, Action<T>? aplicar = null)
            where T : EventoDeAuditoria, new()
        {
            var e = new T { FechaHora = utc, Tipo = tipo, Resultado = resultado, Usuario = usuario, Detalle = detalle, Ip = "10.0.0.1", IdCorrelacion = Guid.NewGuid().ToString("N") };
            aplicar?.Invoke(e);
            return e;
        }

        private static void Preparar(PageModel pagina)
        {
            var http = SesionFalsa.Contexto("empresa_admin", DatosBase.EmpresaA.ToString());
            var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
            pagina.PageContext = new PageContext(actionContext)
            {
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            };
        }

        private AuditoriaIndex PaginaDelTenant(ApplicationDbContext db, int? idTenant)
        {
            var pagina = new AuditoriaIndex(new LectorDeAuditoria(db), new TenantContextFijo { IdTenant = idTenant }, _auditoria)
            {
                Desde = new DateOnly(2026, 11, 14), Hasta = new DateOnly(2026, 11, 21),
            };
            Preparar(pagina);
            return pagina;
        }

        private PlataformaIndex PaginaDeLaPlataforma(ApplicationDbContext db)
        {
            var pagina = new PlataformaIndex(new LectorDeAuditoria(db), _auditoria) { Desde = new DateOnly(2026, 11, 14), Hasta = new DateOnly(2026, 11, 21) };
            Preparar(pagina);
            return pagina;
        }

        // ── Permisos ────────────────────────────────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(typeof(AuditoriaIndex), "administracion.auditoria.ver")]
        [InlineData(typeof(AuditoriaImprimir), "administracion.auditoria.ver")]
        [InlineData(typeof(PlataformaIndex), "plataforma.auditoria.ver")]
        [InlineData(typeof(PlataformaImprimir), "plataforma.auditoria.ver")]
        public void Cada_pantalla_pide_su_permiso_de_solo_lectura(Type pagina, string permiso)
        {
            var requisito = RequisitoDePagina.De(pagina, null);

            Assert.Equal(TipoDeRequisito.Permiso, requisito.Tipo);
            Assert.Equal(permiso, requisito.Codigo);
            Assert.Contains(permiso, CatalogoDePermisos.SoloDelAdministrador.Append("plataforma.auditoria.ver"));
            // Ninguna escribe: no hay handlers OnPost, OnPut ni OnDelete (la cobertura de D76 no las alcanza porque no cambian nada).
            Assert.DoesNotContain(pagina.GetMethods(), m => m.Name.StartsWith("OnPost") || m.Name.StartsWith("OnPut") || m.Name.StartsWith("OnDelete"));
        }

        // ── Auditoría de la empresa ─────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task La_del_tenant_muestra_solo_su_tenant_aunque_el_contexto_vea_todos()
        {
            Sembrar();
            // Un contexto de plataforma (el filtro global apagado, como el del operador): la pantalla filtra por el tenant de la petición igual.
            await using var db = _bd.CrearComo(idTenant: null, plataforma: true);
            var pagina = PaginaDelTenant(db, DatosBase.TenantA);

            Assert.IsType<PageResult>(await pagina.OnGetAsync(CancellationToken.None));

            Assert.False(pagina.SinTenant);
            Assert.Equal(3, pagina.Resultados.Total);
            Assert.All(pagina.Resultados.Filas, f => Assert.Equal(DatosBase.TenantA, f.IdTenant));
            Assert.DoesNotContain(pagina.Resultados.Filas, f => f.Usuario == "admin_b");
            // Lo más reciente primero.
            Assert.Equal(pagina.Resultados.Filas.Select(f => f.IdEvento).OrderByDescending(i => i), pagina.Resultados.Filas.Select(f => f.IdEvento));
        }

        [Fact]
        public async Task Sin_tenant_no_muestra_nada_ni_exporta()
        {
            Sembrar();
            await using var db = _bd.CrearComo(idTenant: null, plataforma: true);
            var pagina = PaginaDelTenant(db, idTenant: null);

            Assert.IsType<PageResult>(await pagina.OnGetAsync(CancellationToken.None));
            Assert.True(pagina.SinTenant);
            Assert.Empty(pagina.Resultados.Filas);

            Assert.IsType<RedirectToPageResult>(await pagina.OnGetExcelAsync(CancellationToken.None));
            Assert.Empty(_auditoria.Eventos);
        }

        [Fact]
        public async Task El_correo_sale_enmascarado_en_el_detalle()
        {
            Sembrar();
            await using var db = _bd.CrearComo(DatosBase.TenantA);
            var pagina = PaginaDelTenant(db, DatosBase.TenantA);

            await pagina.OnGetAsync(CancellationToken.None);

            var editado = pagina.Resultados.Filas.Single(f => f.Tipo == TiposDeEvento.UsuarioEditado);
            Assert.DoesNotContain("maria.lopez@empresa.hn", editado.Detalle);
            Assert.Contains("ma***@e***.hn", editado.Detalle);
        }

        [Fact]
        public async Task Las_fechas_son_dias_de_Honduras()
        {
            Sembrar();
            await using var db = _bd.CrearComo(DatosBase.TenantA);
            var pagina = PaginaDelTenant(db, DatosBase.TenantA);
            pagina.Desde = pagina.Hasta = new DateOnly(2026, 11, 19);

            await pagina.OnGetAsync(CancellationToken.None);

            var fila = Assert.Single(pagina.Resultados.Filas);
            Assert.Equal(TiposDeEvento.RolPermisos, fila.Tipo);
            Assert.Equal(new DateTime(2026, 11, 19, 23, 30, 0), fila.FechaHoraLocal);
        }

        [Fact]
        public async Task La_fecha_maxima_no_rompe_el_filtro_y_no_pone_tope_superior()
        {
            Sembrar();
            await using var db = _bd.CrearComo(DatosBase.TenantA);
            var pagina = PaginaDelTenant(db, DatosBase.TenantA);
            pagina.Hasta = DateOnly.MaxValue;   // «?Hasta=9999-12-31»: antes desbordaba en AddDays(1) y daba error 500

            await pagina.OnGetAsync(CancellationToken.None);

            Assert.Equal(3, pagina.Resultados.Total);

            var plataforma = PaginaDeLaPlataforma(db);
            plataforma.Hasta = DateOnly.MaxValue;
            await plataforma.OnGetAsync(CancellationToken.None);
            Assert.Equal(3, plataforma.Resultados.Total);
        }

        [Fact]
        public async Task Filtra_por_evento_resultado_y_usuario()
        {
            Sembrar();
            await using var db = _bd.CrearComo(DatosBase.TenantA);

            var porTipo = PaginaDelTenant(db, DatosBase.TenantA);
            porTipo.Tipo = TiposDeEvento.RolPermisos;
            await porTipo.OnGetAsync(CancellationToken.None);
            Assert.Equal(new[] { TiposDeEvento.RolPermisos }, porTipo.Resultados.Filas.Select(f => f.Tipo));

            var porResultado = PaginaDelTenant(db, DatosBase.TenantA);
            porResultado.Resultado = ResultadosDeEvento.Denegado;
            await porResultado.OnGetAsync(CancellationToken.None);
            Assert.Equal(new[] { TiposDeEvento.AccesoDenegado }, porResultado.Resultados.Filas.Select(f => f.Tipo));

            var porUsuario = PaginaDelTenant(db, DatosBase.TenantA);
            porUsuario.Usuario = "operador";
            await porUsuario.OnGetAsync(CancellationToken.None);
            Assert.Equal(new[] { "operador_a" }, porUsuario.Resultados.Filas.Select(f => f.Usuario));

            // Un resultado que no existe se ignora en vez de dejar la pantalla vacía.
            var raro = PaginaDelTenant(db, DatosBase.TenantA);
            raro.Resultado = "cualquiera";
            await raro.OnGetAsync(CancellationToken.None);
            Assert.Null(raro.Resultado);
            Assert.Equal(3, raro.Resultados.Total);
        }

        [Fact]
        public async Task Exportar_a_Excel_da_el_libro_y_deja_su_fila_en_el_tenant()
        {
            Sembrar();
            await using var db = _bd.CrearComo(DatosBase.TenantA);
            var pagina = PaginaDelTenant(db, DatosBase.TenantA);

            var archivo = Assert.IsType<FileContentResult>(await pagina.OnGetExcelAsync(CancellationToken.None));

            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo.ContentType);
            Assert.Equal("auditoria_2026-11-14_a_2026-11-21.xlsx", archivo.FileDownloadName);
            using (var libro = new XLWorkbook(new MemoryStream(archivo.FileContents)))
            {
                var hoja = libro.Worksheet(1);
                Assert.Equal("Fecha y hora (Honduras)", hoja.Cell(3, 1).GetString());
                Assert.Equal(3, hoja.RowsUsed().Count() - 2);   // título y encabezado
                var todo = string.Join("|", hoja.CellsUsed().Select(c => c.GetString()));
                Assert.DoesNotContain("maria.lopez@empresa.hn", todo);
                Assert.DoesNotContain("admin_b", todo);
            }

            var evento = Assert.Single(_auditoria.De(TiposDeEvento.AuditoriaExportada));
            Assert.Equal(DatosBase.TenantA, evento.IdTenant);
            var detalle = JsonSerializer.Serialize(evento.Detalle);
            Assert.Contains("\"formato\":\"excel\"", detalle);
            Assert.Contains("\"filas\":3", detalle);
        }

        [Fact]
        public async Task La_hoja_para_imprimir_trae_las_mismas_filas_y_deja_su_fila_como_pdf()
        {
            Sembrar();
            await using var db = _bd.CrearComo(DatosBase.TenantA);
            var pagina = new AuditoriaImprimir(new LectorDeAuditoria(db), new TenantContextFijo { IdTenant = DatosBase.TenantA }, _auditoria)
            {
                Desde = new DateOnly(2026, 11, 14), Hasta = new DateOnly(2026, 11, 21),
            };
            Preparar(pagina);

            Assert.IsType<PageResult>(await pagina.OnGetAsync(CancellationToken.None));

            Assert.Equal(3, pagina.Exportacion.Filas.Count);
            Assert.False(pagina.Exportacion.Recortada);
            Assert.Contains("\"formato\":\"pdf\"", JsonSerializer.Serialize(Assert.Single(_auditoria.De(TiposDeEvento.AuditoriaExportada)).Detalle));
        }

        // ── Auditoría de la plataforma ──────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task La_de_la_plataforma_muestra_todo_con_el_codigo_del_tenant_y_filtra_por_tenant()
        {
            Sembrar();
            await using var db = _bd.CrearComo(idTenant: null, plataforma: true);

            var todas = PaginaDeLaPlataforma(db);
            await todas.OnGetAsync(CancellationToken.None);
            Assert.Equal(3, todas.Resultados.Total);
            Assert.Equal("TENANT_B", todas.Resultados.Filas.Single(f => f.Tipo == TiposDeEvento.SoporteEntrada).Tenant);
            Assert.NotEmpty(todas.Tenants);

            var deB = PaginaDeLaPlataforma(db);
            deB.IdTenant = DatosBase.TenantB;
            await deB.OnGetAsync(CancellationToken.None);
            Assert.Equal(new[] { TiposDeEvento.SoporteEntrada }, deB.Resultados.Filas.Select(f => f.Tipo));
        }

        [Fact]
        public async Task En_la_de_la_plataforma_el_usuario_con_correo_sale_enmascarado()
        {
            Sembrar();
            await using var db = _bd.CrearComo(idTenant: null, plataforma: true);
            var pagina = PaginaDeLaPlataforma(db);

            await pagina.OnGetAsync(CancellationToken.None);

            var usuarios = pagina.Resultados.Filas.Select(f => f.Usuario).ToList();
            Assert.Contains("pe***@e***.hn", usuarios);
            Assert.Contains("ju***@e***.hn", usuarios);   // ya venía enmascarado: no cambia
            Assert.DoesNotContain("pedro@empresa.hn", usuarios);
        }

        [Fact]
        public async Task Exportar_la_de_la_plataforma_deja_su_fila_en_la_plataforma()
        {
            Sembrar();
            await using var db = _bd.CrearComo(idTenant: null, plataforma: true);
            var pagina = PaginaDeLaPlataforma(db);
            pagina.Usuario = "pedro@empresa.hn";

            var archivo = Assert.IsType<FileContentResult>(await pagina.OnGetExcelAsync(CancellationToken.None));

            Assert.StartsWith("auditoria_plataforma_", archivo.FileDownloadName);
            var evento = Assert.Single(_auditoria.De(TiposDeEvento.AuditoriaPlataformaExportada));
            Assert.Equal(DestinoDeEvento.Plataforma, TiposDeEvento.DestinoDe(evento.Tipo));
            var detalle = JsonSerializer.Serialize(evento.Detalle);
            Assert.Contains("\"filas\":1", detalle);
            Assert.DoesNotContain("pedro@empresa.hn", detalle);   // el filtro por usuario también va enmascarado
        }

        // ── Lo compartido ──────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Sin_fechas_se_muestran_los_ultimos_siete_dias_y_unas_fechas_al_reves_se_ordenan()
        {
            var vacio = new FiltroDeAuditoria();
            ConsultaDeAuditoria.Normalizar(vacio, new DateOnly(2026, 11, 21));
            Assert.Equal(new DateOnly(2026, 11, 15), vacio.Desde);
            Assert.Null(vacio.Hasta);

            var alReves = new FiltroDeAuditoria { Desde = new DateOnly(2026, 11, 21), Hasta = new DateOnly(2026, 11, 1) };
            ConsultaDeAuditoria.Normalizar(alReves, new DateOnly(2026, 11, 21));
            Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 21)), (alReves.Desde!.Value, alReves.Hasta!.Value));
        }

        [Theory]
        [InlineData("{\"a\":\"maria.lopez@empresa.hn\"}", "{\"a\":\"ma***@e***.hn\"}")]
        [InlineData("dos: a@b.com y juan.perez@sub.dominio.org", "dos: a***@b***.com y ju***@s***.org")]
        [InlineData("{\"correo\":\"ma***@e***.hn\"}", "{\"correo\":\"ma***@e***.hn\"}")]
        [InlineData("sin correo", "sin correo")]
        [InlineData(null, null)]
        public void Enmascara_cada_correo_completo_y_deja_lo_demas(string? texto, string? esperado)
            => Assert.Equal(esperado, ConsultaDeAuditoria.EnmascararCorreos(texto));

        [Fact]
        public void Cada_tipo_del_catalogo_tiene_su_nombre_en_espanol_y_cada_tabla_ofrece_sus_tipos()
        {
            Assert.All(TiposDeEvento.Todos, t => Assert.NotEqual(t, ConsultaDeAuditoria.NombreDeTipo(t)));

            var deLaPlataforma = LectorDeAuditoria.TiposDe(plataforma: true);
            var delTenant = LectorDeAuditoria.TiposDe(plataforma: false);
            Assert.Contains(TiposDeEvento.SesionEntrada, deLaPlataforma);
            Assert.DoesNotContain(TiposDeEvento.SesionEntrada, delTenant);
            Assert.Contains(TiposDeEvento.UsuarioEditado, delTenant);
            Assert.Contains(TiposDeEvento.SoporteEntrada, delTenant);
            Assert.Contains(TiposDeEvento.ContextoElegido, delTenant);
        }
    }
}
