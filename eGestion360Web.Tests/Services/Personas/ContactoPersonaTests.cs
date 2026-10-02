using eGestion360Web.Services.Personas;

namespace eGestion360Web.Tests.Services.Personas
{
    public class ContactoPersonaTests
    {
        [Theory]
        [InlineData("98765432")]
        [InlineData("9876-5432")]
        [InlineData("+504 9876-5432")]
        [InlineData("50498765432")]
        [InlineData("(504) 9876 5432")]
        [InlineData("  9876.5432 ")]
        public void Telefono_valido_se_normaliza_a_8_digitos(string entrada)
        {
            Assert.True(ContactoPersona.TryNormalizarTelefono(entrada, out var digitos));
            Assert.Equal("98765432", digitos);
        }

        [Theory]
        [InlineData("9876543")]       // 7 dígitos
        [InlineData("987654321")]     // 9 dígitos
        [InlineData("9876-ABCD")]     // letras
        [InlineData("9876+5432")]     // + en medio
        [InlineData("   ")]
        [InlineData(null)]
        public void Telefono_invalido_se_rechaza(string? entrada) =>
            Assert.False(ContactoPersona.TryNormalizarTelefono(entrada, out _));

        [Fact]
        public void FormatearTelefono_muestra_9999_9999() =>
            Assert.Equal("9876-5432", ContactoPersona.FormatearTelefono("98765432"));

        [Fact]
        public void Correo_valido_se_guarda_en_minusculas()
        {
            Assert.True(ContactoPersona.TryNormalizarCorreo("  Juan.Perez@Empresa.COM ", out var correo));
            Assert.Equal("juan.perez@empresa.com", correo);
        }

        [Theory]
        [InlineData("juan.empresa.com")]    // sin arroba
        [InlineData("juan@empresa")]        // sin punto en el dominio
        [InlineData("ju an@empresa.com")]   // con espacio
        [InlineData("")]
        public void Correo_invalido_se_rechaza(string entrada) =>
            Assert.False(ContactoPersona.TryNormalizarCorreo(entrada, out _));

        [Fact]
        public void Correo_de_mas_de_150_caracteres_se_rechaza() =>
            Assert.False(ContactoPersona.TryNormalizarCorreo(new string('a', 150) + "@x.com", out _));
    }
}
