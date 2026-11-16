using ClosedXML.Excel;
using eGestion360Web.Services.Psicologia;

namespace eGestion360Web.Tests.Services.Psicologia
{
    /// <summary>Reparto de nombres y lectura del Excel de la brigada: sin base de datos, con nombres y DNI inventados.</summary>
    public class RepartoYLectorTests
    {
        // ── Reparto de nombres ──────────────────────────────────────────────

        [Fact]
        public void Cuatro_palabras_son_dos_nombres_y_dos_apellidos_sin_revisar()
        {
            var r = RepartoDeNombres.Repartir("  Carlos   Andrés Mejía Rivera ");
            Assert.True(r.Valido);
            Assert.False(r.RequiereRevision);
            Assert.Equal(("Carlos", "Andrés", "Mejía", "Rivera"), (r.PrimerNombre, r.SegundoNombre, r.PrimerApellido, r.SegundoApellido));
        }

        [Theory]
        [InlineData("Marvin Reyes Castillo", "Marvin", null, "Reyes", "Castillo", 3)]
        [InlineData("Ana Torres", "Ana", null, "Torres", null, 2)]
        [InlineData("Juan Carlos José Paz Ortega", "Juan", "Carlos José", "Paz", "Ortega", 5)]
        public void Tres_dos_o_cinco_palabras_se_proponen_y_se_marcan_para_revisar(
            string nombre, string p1, string? p2, string a1, string? a2, int piezas)
        {
            var r = RepartoDeNombres.Repartir(nombre);
            Assert.True(r.Valido);
            Assert.True(r.RequiereRevision);
            Assert.NotNull(r.Motivo);
            Assert.Equal(piezas, r.Piezas);
            Assert.Equal((p1, p2, a1, a2), (r.PrimerNombre, r.SegundoNombre, r.PrimerApellido, r.SegundoApellido));
        }

        [Fact]
        public void Las_particulas_se_pegan_a_la_palabra_siguiente_y_el_reparto_se_confirma_a_mano()
        {
            var r = RepartoDeNombres.Repartir("María de los Ángeles Pineda Cruz");
            Assert.True(r.RequiereRevision);   // probable, no seguro
            Assert.Contains("«de»", r.Motivo);
            Assert.Equal(("María", "de los Ángeles", "Pineda", "Cruz"), (r.PrimerNombre, r.SegundoNombre, r.PrimerApellido, r.SegundoApellido));

            var s = RepartoDeNombres.Repartir("José de la Cruz Pérez");
            Assert.Equal(3, s.Piezas);
            Assert.Equal(("José", "de la Cruz", "Pérez"), (s.PrimerNombre, s.PrimerApellido, s.SegundoApellido));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("Carlos")]
        public void Sin_nombre_o_con_una_sola_palabra_no_se_puede_repartir(string? nombre)
        {
            var r = RepartoDeNombres.Repartir(nombre);
            Assert.False(r.Valido);
            Assert.NotNull(r.Motivo);
        }

        // ── Lector del Excel ────────────────────────────────────────────────

        private static MemoryStream Libro(Action<XLWorkbook> armar)
        {
            using var libro = new XLWorkbook();
            armar(libro);
            var ms = new MemoryStream();
            libro.SaveAs(ms);
            ms.Position = 0;
            return ms;
        }

        private static MemoryStream ListadoDePrueba() => Libro(l =>
        {
            var resumen = l.AddWorksheet("Resumen");                 // hoja sin el listado: se ignora
            resumen.Cell(1, 1).Value = "Resumen demográfico";
            resumen.Cell(3, 1).Value = "Sexo";

            var h = l.AddWorksheet("Listado Brigada");
            h.Cell(1, 1).Value = "Brigada de prueba";               // un título arriba de los encabezados
            h.Cell(2, 1).Value = "N°"; h.Cell(2, 2).Value = "Nombre completo"; h.Cell(2, 3).Value = "DNI";
            h.Cell(2, 4).Value = "Edad"; h.Cell(2, 5).Value = "Género";
            h.Cell(3, 1).Value = 1; h.Cell(3, 2).Value = "Carlos Andrés Mejía Rivera"; h.Cell(3, 3).Value = "0501-1998-00412";
            h.Cell(3, 4).Value = 28; h.Cell(3, 5).Value = "M";
            h.Cell(5, 1).Value = 2; h.Cell(5, 2).Value = "Ana Lucía Torres Núñez"; h.Cell(5, 3).Value = "0501-1992-02290";
            h.Cell(5, 4).Value = 34; h.Cell(5, 5).Value = "F";      // la fila 4 queda vacía y se salta
        });

        [Fact]
        public void Lee_el_listado_aunque_haya_otra_hoja_un_titulo_y_filas_vacias()
        {
            using var archivo = ListadoDePrueba();
            var r = LectorExcelBrigada.Leer(archivo, @"C:\descargas\Brigada de prueba.xlsx");

            Assert.True(r.Ok, r.Error);
            Assert.Equal("Brigada de prueba.xlsx", r.Archivo!.NombreArchivo);   // solo el nombre, nunca la ruta del equipo
            Assert.Equal("Listado Brigada", r.Archivo.Hoja);
            Assert.Matches("^[0-9a-f]{64}$", r.Archivo.HashSha256);
            Assert.Equal(new[] { 3, 5 }, r.Archivo.Filas.Select(f => f.NumeroFila));
            Assert.Equal(new FilaBrigada(3, "Carlos Andrés Mejía Rivera", "0501-1998-00412", "28", "M"), r.Archivo.Filas[0]);
        }

        [Fact]
        public void El_mismo_archivo_da_la_misma_huella()
        {
            using var a = ListadoDePrueba();
            var bytes = a.ToArray();
            var r1 = LectorExcelBrigada.Leer(new MemoryStream(bytes), "a.xlsx");
            var r2 = LectorExcelBrigada.Leer(new MemoryStream(bytes), "b.xlsx");
            Assert.Equal(r1.Archivo!.HashSha256, r2.Archivo!.HashSha256);
        }

        [Fact]
        public void Rechaza_otra_extension_un_archivo_vacio_lo_que_no_es_Excel_y_un_listado_sin_encabezados()
        {
            Assert.Contains(".xlsx", LectorExcelBrigada.Leer(new MemoryStream(new byte[] { 1 }), "listado.csv").Error);
            Assert.Contains("vacío", LectorExcelBrigada.Leer(new MemoryStream(), "listado.xlsx").Error);
            Assert.Contains("No se pudo abrir", LectorExcelBrigada.Leer(new MemoryStream(new byte[] { 1, 2, 3 }), "listado.xlsx").Error);

            using var sinEncabezados = Libro(l => l.AddWorksheet("Hoja1").Cell(1, 1).Value = "Otra cosa");
            Assert.Contains("encabezados", LectorExcelBrigada.Leer(sinEncabezados, "listado.xlsx").Error);
        }

        [Fact]
        public void Rechaza_un_listado_con_datos_desde_la_primera_fila_sin_fila_de_encabezados()
        {
            // Decisión de Emilio (2026-11-15): el listado debe traer su fila de títulos; no se adivinan las columnas por el contenido.
            using var archivo = Libro(l =>
            {
                var h = l.AddWorksheet("Listado Brigada");
                h.Cell(1, 1).Value = 1; h.Cell(1, 2).Value = "Prueba Ficticia Uno Inventado"; h.Cell(1, 3).Value = "0000-1990-99991";
                h.Cell(1, 4).Value = 30; h.Cell(1, 5).Value = "M";
                h.Cell(2, 1).Value = 2; h.Cell(2, 2).Value = "Prueba Ficticia Dos Inventada"; h.Cell(2, 3).Value = "0000-1985-99992";
                h.Cell(2, 4).Value = 41; h.Cell(2, 5).Value = "F";

                var resumen = l.AddWorksheet("Resumen Estadístico");   // trae "Sexo" como título, pero no es el listado
                resumen.Cell(4, 1).Value = "Sexo"; resumen.Cell(4, 2).Value = "Código"; resumen.Cell(4, 3).Value = "Cantidad";
            });

            var r = LectorExcelBrigada.Leer(archivo, "Brigada sin encabezados.xlsx");

            Assert.False(r.Ok);
            Assert.Null(r.Archivo);
            Assert.Contains("encabezados", r.Error);
            Assert.Contains("Nombre completo", r.Error);
        }

        [Fact]
        public void Acepta_encabezados_sin_tildes_en_mayusculas_y_con_sinonimos()
        {
            using var archivo = Libro(l =>
            {
                var h = l.AddWorksheet("Datos");
                h.Cell(1, 1).Value = "NOMBRE"; h.Cell(1, 2).Value = "Identidad"; h.Cell(1, 3).Value = "SEXO";
                h.Cell(2, 1).Value = "Ana Torres"; h.Cell(2, 2).Value = "0501-1992-02290"; h.Cell(2, 3).Value = "F";
            });
            var r = LectorExcelBrigada.Leer(archivo, "listado.xlsx");
            Assert.True(r.Ok, r.Error);
            Assert.Equal(new FilaBrigada(2, "Ana Torres", "0501-1992-02290", null, "F"), r.Archivo!.Filas.Single());
        }
    }
}
