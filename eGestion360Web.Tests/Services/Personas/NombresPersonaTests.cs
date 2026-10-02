using eGestion360Web.Services.Personas;

namespace eGestion360Web.Tests.Services.Personas
{
    public class NombresPersonaTests
    {
        [Theory]
        [InlineData("  juan   carlos ", "juan carlos")]
        [InlineData("Ana", "Ana")]
        [InlineData("   ", "")]
        [InlineData(null, "")]
        public void Limpiar_quita_espacios_de_los_extremos_y_dobles(string? entrada, string esperado) =>
            Assert.Equal(esperado, NombresPersona.Limpiar(entrada));

        [Theory]
        [InlineData("José Ñandú Muñoz")]
        [InlineData("O'Brien-Pérez")]
        [InlineData("María del Carmen")]
        public void EsValido_acepta_letras_tildes_enie_apostrofo_y_guion(string valor) =>
            Assert.True(NombresPersona.EsValido(valor));

        [Theory]
        [InlineData("J0se")]
        [InlineData("Juan_")]
        [InlineData("Ana@")]
        [InlineData("-'")]
        [InlineData("")]
        public void EsValido_rechaza_digitos_simbolos_y_textos_sin_letras(string valor) =>
            Assert.False(NombresPersona.EsValido(valor));

        [Theory]
        [InlineData("JUAN", "Juan")]
        [InlineData("JOSÉ MARÍA", "José María")]
        [InlineData("JUAN Carlos", "Juan Carlos")]   // por palabra: la que ya tiene minúsculas no se toca
        [InlineData("McDonald", "McDonald")]
        [InlineData("O'BRIEN", "O'Brien")]
        [InlineData("JEAN-PIERRE", "Jean-Pierre")]
        [InlineData("A", "A")]
        public void AFormatoPropio_pasa_a_nombre_propio_solo_las_palabras_en_mayusculas(string entrada, string esperado) =>
            Assert.Equal(esperado, NombresPersona.AFormatoPropio(entrada));

        [Theory]
        [InlineData("José", "María", "Martínez", "López", "JOSE MARIA MARTINEZ LOPEZ")]
        [InlineData("Muñoz", null, null, null, "MUNOZ")]
        [InlineData("Güell", "", "", "", "GUELL")]
        [InlineData("Jean-Pierre", "O'Brien", null, null, "JEAN PIERRE OBRIEN")]
        [InlineData("Ana", null, "Paz", "", "ANA PAZ")]
        public void Normalizar_da_mayusculas_sin_tildes_con_enie_como_N(string? a, string? b, string? c, string? d, string esperado) =>
            Assert.Equal(esperado, NombresPersona.Normalizar(a, b, c, d));

        [Fact]
        public void Normalizar_no_depende_de_como_se_reparten_las_palabras_entre_nombres_y_apellidos()
        {
            // Es la propiedad que permite detectar parecidos en las 38 personas de reparto ambiguo.
            Assert.Equal(
                NombresPersona.Normalizar("Edwin", null, "Joel", "Martinez"),
                NombresPersona.Normalizar("Edwin", "Joel", "Martinez", null));
        }

        [Fact]
        public void Componer_arma_los_campos_legados_nombres_y_apellidos()
        {
            Assert.Equal("Juan", NombresPersona.ComponerNombres("Juan", null));
            Assert.Equal("Juan Carlos", NombresPersona.ComponerNombres("Juan", "Carlos"));
            Assert.Equal("Pérez López", NombresPersona.ComponerApellidos("Pérez", " López "));
            Assert.Equal("Pérez", NombresPersona.ComponerApellidos("Pérez", ""));
        }
    }
}
