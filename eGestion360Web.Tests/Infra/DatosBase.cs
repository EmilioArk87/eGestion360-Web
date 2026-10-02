using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Flota;

namespace eGestion360Web.Tests.Infra
{
    /// <summary>
    /// Catálogos y empresas de prueba, iguales en forma a los reales (scripts 015 a 017):
    /// los mismos tipos de documento con sus patrones, las 9 categorías de licencia y los 5 cargos base.
    /// </summary>
    public static class DatosBase
    {
        public const int EmpresaA = 1;   // equivale a una empresa cualquiera
        public const int EmpresaB = 4;   // otra empresa, para probar la separación entre empresas

        public static void Sembrar(ApplicationDbContext db)
        {
            var ahora = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

            db.Paises.AddRange(
                new Pais { CodigoIso = "HN", CodigoIso3 = "HND", CodigoNumerico = "340", Nombre = "Honduras", NombreIngles = "Honduras", FechaCreacion = ahora },
                new Pais { CodigoIso = "US", CodigoIso3 = "USA", CodigoNumerico = "840", Nombre = "Estados Unidos de América", NombreIngles = "United States", FechaCreacion = ahora },
                new Pais { CodigoIso = "GT", CodigoIso3 = "GTM", CodigoNumerico = "320", Nombre = "Guatemala", NombreIngles = "Guatemala", FechaCreacion = ahora });

            // Las monedas ya vienen del modelo (HasData); aquí se ajustan a como están en la base real.
            foreach (var m in db.Monedas.Where(m => m.CodigoIso == "ANG").ToList()) m.Activo = false;

            db.CatalogoTiposDocumento.AddRange(
                new CatalogoTipoDocumento { Codigo = "DNI", Nombre = "Documento Nacional de Identificación", PaisIso = "HN", Patron = "^[0-9]{13}$", LargoMin = 13, LargoMax = 13, EsIdentidad = true, FechaCreacion = ahora },
                new CatalogoTipoDocumento { Codigo = "RTN", Nombre = "Registro Tributario Nacional", PaisIso = "HN", Patron = "^[0-9]{14}$", LargoMin = 14, LargoMax = 14, EsIdentidad = true, FechaCreacion = ahora },
                new CatalogoTipoDocumento { Codigo = "PASAPORTE", Nombre = "Pasaporte", PaisIso = null, Patron = "^[A-Z0-9]{5,20}$", LargoMin = 5, LargoMax = 20, EsIdentidad = true, FechaCreacion = ahora },
                new CatalogoTipoDocumento { Codigo = "CARNE_RESIDENTE", Nombre = "Carné de residente", PaisIso = "HN", Patron = null, LargoMin = null, LargoMax = null, EsIdentidad = true, FechaCreacion = ahora });

            foreach (var codigo in new[] { "A", "B", "B1", "BE", "C1", "C", "CE", "D1", "D" })
                db.CatalogoTiposLicencia.Add(new CatalogoTipoLicencia { Codigo = codigo, Nombre = "Categoría " + codigo, FechaCreacion = ahora });

            var departamento = new CatalogoDepartamento { PaisIso = "HN", Codigo = "08", Nombre = "Francisco Morazán", FechaCreacion = ahora };
            departamento.Municipios.Add(new CatalogoMunicipio { Codigo = "0801", Nombre = "Distrito Central", FechaCreacion = ahora });
            departamento.Municipios.Add(new CatalogoMunicipio { Codigo = "0802", Nombre = "Alubarén", FechaCreacion = ahora });
            db.CatalogoDepartamentos.Add(departamento);

            foreach (var id in new[] { EmpresaA, EmpresaB })
            {
                db.Empresas.Add(new Empresa
                {
                    IdEmpresa = id, Codigo = "E" + id, RazonSocial = "Empresa de prueba " + id, PaisIso = "HN", MonedaIso = "HNL",
                    ZonaHoraria = "America/Tegucigalpa", FechaActivacion = ahora, CreadoPor = "pruebas", FechaCreacion = ahora
                });

                foreach (var codigo in new[] { "CONDUCTOR", "COBRADOR", "MECANICO", "SUPERVISOR", "OTRO" })
                    db.Cargos.Add(new Cargo { IdEmpresa = id, Codigo = codigo, Nombre = codigo, CreadoPor = "pruebas", FechaCreacion = ahora });
            }

            db.SaveChanges();
        }
    }
}
