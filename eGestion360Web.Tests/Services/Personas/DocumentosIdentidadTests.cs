using eGestion360Web.Services.Personas;

namespace eGestion360Web.Tests.Services.Personas
{
    public class DocumentosIdentidadTests
    {
        [Theory]
        [InlineData(" 0801-1990 12345 ", "0801199012345")]
        [InlineData("ab-12.3 4", "AB1234")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void NormalizarNumero_quita_guiones_espacios_y_puntos_y_pasa_a_mayusculas(string? entrada, string esperado) =>
            Assert.Equal(esperado, DocumentosIdentidad.NormalizarNumero(entrada));

        [Fact]
        public void TryAnalizarDni_separa_municipio_anio_y_correlativo()
        {
            Assert.True(DocumentosIdentidad.TryAnalizarDni("0801199012345", out var p));
            Assert.Equal("08", p.Departamento);
            Assert.Equal("0801", p.Municipio);
            Assert.Equal(1990, p.Anio);
            Assert.Equal("12345", p.Correlativo);
        }

        [Theory]
        [InlineData("080119901234")]     // 12 dígitos
        [InlineData("08011990123456")]   // 14 dígitos
        [InlineData("08011990123A5")]    // con una letra
        [InlineData("")]
        public void TryAnalizarDni_rechaza_lo_que_no_son_13_digitos(string entrada) =>
            Assert.False(DocumentosIdentidad.TryAnalizarDni(entrada, out _));

        [Theory]
        [InlineData("01", 1990, true)]
        [InlineData("18", 1990, true)]
        [InlineData("00", 1990, false)]
        [InlineData("19", 1990, false)]
        [InlineData("08", 1899, false)]
        [InlineData("08", 1900, true)]
        [InlineData("08", 2026, true)]
        [InlineData("08", 2027, false)]
        public void ValidarPartesDni_exige_departamento_01_a_18_y_un_anio_posible(string departamento, int anio, bool valido)
        {
            var partes = new DocumentosIdentidad.PartesDni(departamento, departamento + "01", anio, "00001");
            Assert.Equal(valido, DocumentosIdentidad.ValidarPartesDni(partes, 2026) == null);
        }

        [Theory]
        [InlineData("0801199012345", "****-****-*2345")]       // DNI de 13 dígitos
        [InlineData("0801-1990-12345", "****-****-*2345")]     // con guiones
        [InlineData("AB1234567", "*****4567")]                  // pasaporte
        [InlineData("31234", "31234")]                          // código corto de empleado: no es un documento
        [InlineData("", "")]
        [InlineData(null, "")]
        public void Enmascarar_deja_ver_solo_los_ultimos_4_caracteres(string? entrada, string esperado) =>
            Assert.Equal(esperado, DocumentosIdentidad.Enmascarar(entrada));

        [Theory]
        [InlineData("0801199012345", "0801-1990-12345")]
        [InlineData("AB123", "AB123")]
        public void FormatearDni_muestra_4_4_5_y_no_toca_otros_textos(string entrada, string esperado) =>
            Assert.Equal(esperado, DocumentosIdentidad.FormatearDni(entrada));
    }
}
