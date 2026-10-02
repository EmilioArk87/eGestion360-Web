using eGestion360Web.Services.Auditoria;

namespace eGestion360Web.Tests.Services.Auditoria
{
    public class EtiquetasBitacoraTests
    {
        [Theory]
        [InlineData("personas", "Persona")]
        [InlineData("persona_documentos", "Documento")]
        [InlineData("persona_empresa", "Vínculo con la empresa")]
        [InlineData("empleados", "Ficha de empleado")]
        [InlineData("otra_cosa", "otra_cosa")]
        public void Entidad_se_muestra_en_espanol(string entidad, string esperado) =>
            Assert.Equal(esperado, EtiquetasBitacora.Entidad(entidad));

        [Theory]
        [InlineData("INSERT", "Alta")]
        [InlineData("UPDATE", "Cambio")]
        [InlineData("DELETE", "Baja")]
        public void Operacion_se_muestra_en_espanol(string operacion, string esperado) =>
            Assert.Equal(esperado, EtiquetasBitacora.Operacion(operacion));

        [Theory]
        [InlineData("primer_nombre", "Primer nombre")]
        [InlineData("codigo_interno", "Código de empleado")]
        [InlineData("fusion", "Fusión de fichas")]
        [InlineData("campo_nuevo_sin_etiqueta", "Campo nuevo sin etiqueta")]
        [InlineData(null, "")]
        public void Campo_se_muestra_en_espanol_y_lo_desconocido_se_hace_legible(string? campo, string esperado) =>
            Assert.Equal(esperado, EtiquetasBitacora.Campo(campo));

        [Theory]
        [InlineData("true", "Sí")]
        [InlineData("false", "No")]
        [InlineData("MECANICO", "MECANICO")]
        [InlineData(null, null)]
        public void Valor_pasa_los_booleanos_a_Si_No(string? valor, string? esperado) =>
            Assert.Equal(esperado, EtiquetasBitacora.Valor(valor));

        [Fact]
        public void Resumir_arma_campo_valor_con_etiquetas_y_booleanos_en_espanol()
        {
            var resumen = EtiquetasBitacora.Resumir("{\"codigo_interno\":\"10001\",\"cargo\":\"MECANICO\",\"tarifa_diaria\":350.00,\"activo\":true}");
            Assert.Equal("Código de empleado: 10001, Cargo: MECANICO, Tarifa diaria: 350.00, Activo: Sí", resumen);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("no es json")]
        [InlineData("[1,2]")]
        [InlineData("{}")]
        public void Resumir_devuelve_nulo_si_no_hay_un_objeto_con_datos(string? json) =>
            Assert.Null(EtiquetasBitacora.Resumir(json));
    }
}
