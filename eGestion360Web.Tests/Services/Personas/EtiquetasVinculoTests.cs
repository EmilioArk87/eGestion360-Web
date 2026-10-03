using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Tests.Services.Personas
{
    public class EtiquetasVinculoTests
    {
        [Theory]
        [InlineData(TiposVinculo.Empleado, "Empleado")]
        [InlineData(TiposVinculo.Cliente, "Cliente")]
        [InlineData(TiposVinculo.Proveedor, "Proveedor")]
        [InlineData(TiposVinculo.Usuario, "Usuario")]
        [InlineData(TiposVinculo.Contacto, "Contacto")]
        [InlineData(TiposVinculo.Otro, "Otro")]
        [InlineData("inventado", "inventado")]
        public void Cada_rol_tiene_su_nombre_y_uno_desconocido_se_muestra_tal_cual(string tipo, string esperado) =>
            Assert.Equal(esperado, EtiquetasVinculo.Rol(tipo));

        [Fact]
        public void Todos_los_roles_de_la_base_tienen_nombre_propio()
        {
            foreach (var tipo in TiposVinculo.Todos)
                Assert.NotEqual(tipo, EtiquetasVinculo.Rol(tipo));
        }

        [Fact]
        public void Un_vinculo_vigente_va_de_color_y_uno_terminado_va_apagado()
        {
            Assert.Equal("bg-primary", EtiquetasVinculo.Clase(TiposVinculo.Empleado, vigente: true));
            Assert.Equal("bg-info text-dark", EtiquetasVinculo.Clase(TiposVinculo.Cliente, vigente: true));
            Assert.Equal("bg-secondary", EtiquetasVinculo.Clase(TiposVinculo.Contacto, vigente: true));
            foreach (var tipo in TiposVinculo.Todos)
                Assert.Equal("bg-light text-muted border", EtiquetasVinculo.Clase(tipo, vigente: false));
        }

        [Fact]
        public void El_estado_dice_vigente_inactivo_o_la_fecha_de_fin()
        {
            Assert.Equal("Vigente", EtiquetasVinculo.Estado(activo: true, fechaFin: null));
            Assert.Equal("Inactivo", EtiquetasVinculo.Estado(activo: false, fechaFin: null));
            Assert.Equal("Terminado el 31/01/2026", EtiquetasVinculo.Estado(activo: true, fechaFin: new DateOnly(2026, 1, 31)));
        }

        [Fact]
        public void El_resumen_de_una_foto_puede_omitir_columnas()
        {
            const string foto = "{\"cargo\":\"CONDUCTOR\",\"tarifa_diaria\":350,\"moneda_tarifa\":\"HNL\",\"activo\":true}";

            Assert.Equal("Cargo: CONDUCTOR, Tarifa diaria: 350, Moneda de la tarifa: HNL, Activo: Sí", EtiquetasBitacora.Resumir(foto));
            Assert.Equal("Cargo: CONDUCTOR, Activo: Sí",
                EtiquetasBitacora.Resumir(foto, new HashSet<string> { "tarifa_diaria", "moneda_tarifa" }));
            Assert.Null(EtiquetasBitacora.Resumir("{\"tarifa_diaria\":350}", new HashSet<string> { "tarifa_diaria" }));
        }
    }
}
