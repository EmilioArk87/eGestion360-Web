using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using eGestion360Web.Models;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Data.Configuracion
{
    // Catálogos globales, iguales para todas las empresas: monedas, países y división territorial.

    public sealed class MonedaConfiguracion : IEntityTypeConfiguration<Moneda>
    {
        public void Configure(EntityTypeBuilder<Moneda> entity)
        {
            entity.HasKey(e => e.CodigoIso);
            entity.Property(e => e.CodigoIso).IsRequired().HasMaxLength(3);
            entity.Property(e => e.Nombre).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Simbolo).IsRequired().HasMaxLength(10);

            entity.HasData(
                new Moneda { CodigoIso = "AED", Nombre = "Dírham de los EAU", Simbolo = "د.إ" },
                new Moneda { CodigoIso = "AFN", Nombre = "Afgani afgano", Simbolo = "؋" },
                new Moneda { CodigoIso = "ALL", Nombre = "Lek albanés", Simbolo = "L" },
                new Moneda { CodigoIso = "AMD", Nombre = "Dram armenio", Simbolo = "֏" },
                new Moneda { CodigoIso = "ANG", Nombre = "Florín antillano neerlandés", Simbolo = "ƒ" },
                new Moneda { CodigoIso = "AOA", Nombre = "Kwanza angoleño", Simbolo = "Kz" },
                new Moneda { CodigoIso = "ARS", Nombre = "Peso argentino", Simbolo = "$" },
                new Moneda { CodigoIso = "AUD", Nombre = "Dólar australiano", Simbolo = "A$" },
                new Moneda { CodigoIso = "AWG", Nombre = "Florín arubeño", Simbolo = "ƒ" },
                new Moneda { CodigoIso = "AZN", Nombre = "Manat azerbaiyano", Simbolo = "₼" },
                new Moneda { CodigoIso = "BAM", Nombre = "Marco bosnio convertible", Simbolo = "KM" },
                new Moneda { CodigoIso = "BBD", Nombre = "Dólar de Barbados", Simbolo = "Bds$" },
                new Moneda { CodigoIso = "BDT", Nombre = "Taka bangladesí", Simbolo = "৳" },
                new Moneda { CodigoIso = "BGN", Nombre = "Lev búlgaro", Simbolo = "лв" },
                new Moneda { CodigoIso = "BHD", Nombre = "Dinar bareiní", Simbolo = ".د.ب" },
                new Moneda { CodigoIso = "BIF", Nombre = "Franco burundés", Simbolo = "Fr" },
                new Moneda { CodigoIso = "BMD", Nombre = "Dólar de Bermudas", Simbolo = "$" },
                new Moneda { CodigoIso = "BND", Nombre = "Dólar de Brunéi", Simbolo = "B$" },
                new Moneda { CodigoIso = "BOB", Nombre = "Boliviano", Simbolo = "Bs" },
                new Moneda { CodigoIso = "BRL", Nombre = "Real brasileño", Simbolo = "R$" },
                new Moneda { CodigoIso = "BSD", Nombre = "Dólar bahameño", Simbolo = "B$" },
                new Moneda { CodigoIso = "BTN", Nombre = "Ngultrum butanés", Simbolo = "Nu" },
                new Moneda { CodigoIso = "BWP", Nombre = "Pula botsuanesa", Simbolo = "P" },
                new Moneda { CodigoIso = "BYN", Nombre = "Rublo bielorruso", Simbolo = "Br" },
                new Moneda { CodigoIso = "BZD", Nombre = "Dólar de Belice", Simbolo = "BZ$" },
                new Moneda { CodigoIso = "CAD", Nombre = "Dólar canadiense", Simbolo = "CA$" },
                new Moneda { CodigoIso = "CDF", Nombre = "Franco congoleño", Simbolo = "Fr" },
                new Moneda { CodigoIso = "CHF", Nombre = "Franco suizo", Simbolo = "Fr" },
                new Moneda { CodigoIso = "CLP", Nombre = "Peso chileno", Simbolo = "$" },
                new Moneda { CodigoIso = "CNY", Nombre = "Yuan chino", Simbolo = "¥" },
                new Moneda { CodigoIso = "COP", Nombre = "Peso colombiano", Simbolo = "$" },
                new Moneda { CodigoIso = "CRC", Nombre = "Colón costarricense", Simbolo = "₡" },
                new Moneda { CodigoIso = "CUP", Nombre = "Peso cubano", Simbolo = "$" },
                new Moneda { CodigoIso = "CVE", Nombre = "Escudo caboverdiano", Simbolo = "$" },
                new Moneda { CodigoIso = "CZK", Nombre = "Corona checa", Simbolo = "Kč" },
                new Moneda { CodigoIso = "DJF", Nombre = "Franco yibutiano", Simbolo = "Fr" },
                new Moneda { CodigoIso = "DKK", Nombre = "Corona danesa", Simbolo = "kr" },
                new Moneda { CodigoIso = "DOP", Nombre = "Peso dominicano", Simbolo = "RD$" },
                new Moneda { CodigoIso = "DZD", Nombre = "Dinar argelino", Simbolo = "دج" },
                new Moneda { CodigoIso = "EGP", Nombre = "Libra egipcia", Simbolo = "E£" },
                new Moneda { CodigoIso = "ERN", Nombre = "Nakfa eritreo", Simbolo = "Nfk" },
                new Moneda { CodigoIso = "ETB", Nombre = "Birr etíope", Simbolo = "Br" },
                new Moneda { CodigoIso = "EUR", Nombre = "Euro", Simbolo = "€" },
                new Moneda { CodigoIso = "FJD", Nombre = "Dólar fiyiano", Simbolo = "FJ$" },
                new Moneda { CodigoIso = "FKP", Nombre = "Libra malvinense", Simbolo = "£" },
                new Moneda { CodigoIso = "GBP", Nombre = "Libra esterlina", Simbolo = "£" },
                new Moneda { CodigoIso = "GEL", Nombre = "Lari georgiano", Simbolo = "₾" },
                new Moneda { CodigoIso = "GHS", Nombre = "Cedi ghanés", Simbolo = "₵" },
                new Moneda { CodigoIso = "GIP", Nombre = "Libra gibraltareña", Simbolo = "£" },
                new Moneda { CodigoIso = "GMD", Nombre = "Dalasi gambiano", Simbolo = "D" },
                new Moneda { CodigoIso = "GNF", Nombre = "Franco guineano", Simbolo = "Fr" },
                new Moneda { CodigoIso = "GTQ", Nombre = "Quetzal guatemalteco", Simbolo = "Q" },
                new Moneda { CodigoIso = "GYD", Nombre = "Dólar de Guyana", Simbolo = "GY$" },
                new Moneda { CodigoIso = "HKD", Nombre = "Dólar de Hong Kong", Simbolo = "HK$" },
                new Moneda { CodigoIso = "HNL", Nombre = "Lempira hondureño", Simbolo = "L" },
                new Moneda { CodigoIso = "HTG", Nombre = "Gourde haitiano", Simbolo = "G" },
                new Moneda { CodigoIso = "HUF", Nombre = "Forinto húngaro", Simbolo = "Ft" },
                new Moneda { CodigoIso = "IDR", Nombre = "Rupia indonesia", Simbolo = "Rp" },
                new Moneda { CodigoIso = "ILS", Nombre = "Nuevo séquel israelí", Simbolo = "₪" },
                new Moneda { CodigoIso = "INR", Nombre = "Rupia india", Simbolo = "₹" },
                new Moneda { CodigoIso = "IQD", Nombre = "Dinar iraquí", Simbolo = "ع.د" },
                new Moneda { CodigoIso = "IRR", Nombre = "Rial iraní", Simbolo = "﷼" },
                new Moneda { CodigoIso = "ISK", Nombre = "Corona islandesa", Simbolo = "kr" },
                new Moneda { CodigoIso = "JMD", Nombre = "Dólar jamaicano", Simbolo = "J$" },
                new Moneda { CodigoIso = "JOD", Nombre = "Dinar jordano", Simbolo = "JD" },
                new Moneda { CodigoIso = "JPY", Nombre = "Yen japonés", Simbolo = "¥" },
                new Moneda { CodigoIso = "KES", Nombre = "Chelín keniata", Simbolo = "KSh" },
                new Moneda { CodigoIso = "KGS", Nombre = "Som kirguís", Simbolo = "с" },
                new Moneda { CodigoIso = "KHR", Nombre = "Riel camboyano", Simbolo = "៛" },
                new Moneda { CodigoIso = "KMF", Nombre = "Franco comorense", Simbolo = "Fr" },
                new Moneda { CodigoIso = "KPW", Nombre = "Won norcoreano", Simbolo = "₩" },
                new Moneda { CodigoIso = "KRW", Nombre = "Won surcoreano", Simbolo = "₩" },
                new Moneda { CodigoIso = "KWD", Nombre = "Dinar kuwaití", Simbolo = "KD" },
                new Moneda { CodigoIso = "KZT", Nombre = "Tenge kazajo", Simbolo = "₸" },
                new Moneda { CodigoIso = "LAK", Nombre = "Kip laosiano", Simbolo = "₭" },
                new Moneda { CodigoIso = "LBP", Nombre = "Libra libanesa", Simbolo = "L£" },
                new Moneda { CodigoIso = "LKR", Nombre = "Rupia de Sri Lanka", Simbolo = "Rs" },
                new Moneda { CodigoIso = "LRD", Nombre = "Dólar liberiano", Simbolo = "L$" },
                new Moneda { CodigoIso = "LSL", Nombre = "Loti lesotense", Simbolo = "L" },
                new Moneda { CodigoIso = "LYD", Nombre = "Dinar libio", Simbolo = "LD" },
                new Moneda { CodigoIso = "MAD", Nombre = "Dírham marroquí", Simbolo = "MAD" },
                new Moneda { CodigoIso = "MDL", Nombre = "Leu moldavo", Simbolo = "L" },
                new Moneda { CodigoIso = "MGA", Nombre = "Ariary malgache", Simbolo = "Ar" },
                new Moneda { CodigoIso = "MKD", Nombre = "Denar macedonio", Simbolo = "ден" },
                new Moneda { CodigoIso = "MMK", Nombre = "Kyat birmano", Simbolo = "K" },
                new Moneda { CodigoIso = "MNT", Nombre = "Tugrik mongol", Simbolo = "₮" },
                new Moneda { CodigoIso = "MOP", Nombre = "Pataca macaense", Simbolo = "P" },
                new Moneda { CodigoIso = "MRU", Nombre = "Uguiya mauritana", Simbolo = "UM" },
                new Moneda { CodigoIso = "MUR", Nombre = "Rupia mauriciana", Simbolo = "Rs" },
                new Moneda { CodigoIso = "MVR", Nombre = "Rufiyaa maldiva", Simbolo = "Rf" },
                new Moneda { CodigoIso = "MWK", Nombre = "Kwacha malauí", Simbolo = "MK" },
                new Moneda { CodigoIso = "MXN", Nombre = "Peso mexicano", Simbolo = "$" },
                new Moneda { CodigoIso = "MYR", Nombre = "Ringgit malayo", Simbolo = "RM" },
                new Moneda { CodigoIso = "MZN", Nombre = "Metical mozambiqueño", Simbolo = "MT" },
                new Moneda { CodigoIso = "NAD", Nombre = "Dólar namibio", Simbolo = "N$" },
                new Moneda { CodigoIso = "NGN", Nombre = "Naira nigeriana", Simbolo = "₦" },
                new Moneda { CodigoIso = "NIO", Nombre = "Córdoba nicaragüense", Simbolo = "C$" },
                new Moneda { CodigoIso = "NOK", Nombre = "Corona noruega", Simbolo = "kr" },
                new Moneda { CodigoIso = "NPR", Nombre = "Rupia nepalesa", Simbolo = "Rs" },
                new Moneda { CodigoIso = "NZD", Nombre = "Dólar neozelandés", Simbolo = "NZ$" },
                new Moneda { CodigoIso = "OMR", Nombre = "Rial omaní", Simbolo = "ر.ع." },
                new Moneda { CodigoIso = "PAB", Nombre = "Balboa panameño", Simbolo = "B/." },
                new Moneda { CodigoIso = "PEN", Nombre = "Sol peruano", Simbolo = "S/" },
                new Moneda { CodigoIso = "PGK", Nombre = "Kina de Papúa Nueva Guinea", Simbolo = "K" },
                new Moneda { CodigoIso = "PHP", Nombre = "Peso filipino", Simbolo = "₱" },
                new Moneda { CodigoIso = "PKR", Nombre = "Rupia pakistaní", Simbolo = "Rs" },
                new Moneda { CodigoIso = "PLN", Nombre = "Esloti polaco", Simbolo = "zł" },
                new Moneda { CodigoIso = "PYG", Nombre = "Guaraní paraguayo", Simbolo = "₲" },
                new Moneda { CodigoIso = "QAR", Nombre = "Riyal catarí", Simbolo = "QR" },
                new Moneda { CodigoIso = "RON", Nombre = "Leu rumano", Simbolo = "lei" },
                new Moneda { CodigoIso = "RSD", Nombre = "Dinar serbio", Simbolo = "din" },
                new Moneda { CodigoIso = "RUB", Nombre = "Rublo ruso", Simbolo = "₽" },
                new Moneda { CodigoIso = "RWF", Nombre = "Franco ruandés", Simbolo = "Fr" },
                new Moneda { CodigoIso = "SAR", Nombre = "Riyal saudí", Simbolo = "SR" },
                new Moneda { CodigoIso = "SBD", Nombre = "Dólar de las Islas Salomón", Simbolo = "SI$" },
                new Moneda { CodigoIso = "SCR", Nombre = "Rupia de Seychelles", Simbolo = "Rs" },
                new Moneda { CodigoIso = "SDG", Nombre = "Libra sudanesa", Simbolo = "£" },
                new Moneda { CodigoIso = "SEK", Nombre = "Corona sueca", Simbolo = "kr" },
                new Moneda { CodigoIso = "SGD", Nombre = "Dólar de Singapur", Simbolo = "S$" },
                new Moneda { CodigoIso = "SHP", Nombre = "Libra de Santa Elena", Simbolo = "£" },
                new Moneda { CodigoIso = "SLE", Nombre = "Leone de Sierra Leona", Simbolo = "Le" },
                new Moneda { CodigoIso = "SOS", Nombre = "Chelín somalí", Simbolo = "Sh" },
                new Moneda { CodigoIso = "SRD", Nombre = "Dólar surinamés", Simbolo = "$" },
                new Moneda { CodigoIso = "STN", Nombre = "Dobra de Santo Tomé", Simbolo = "Db" },
                new Moneda { CodigoIso = "SVC", Nombre = "Colón salvadoreño", Simbolo = "₡" },
                new Moneda { CodigoIso = "SYP", Nombre = "Libra siria", Simbolo = "£" },
                new Moneda { CodigoIso = "SZL", Nombre = "Lilangeni suazi", Simbolo = "L" },
                new Moneda { CodigoIso = "THB", Nombre = "Baht tailandés", Simbolo = "฿" },
                new Moneda { CodigoIso = "TJS", Nombre = "Somoni tayiko", Simbolo = "SM" },
                new Moneda { CodigoIso = "TMT", Nombre = "Manat turcomano", Simbolo = "T" },
                new Moneda { CodigoIso = "TND", Nombre = "Dinar tunecino", Simbolo = "DT" },
                new Moneda { CodigoIso = "TOP", Nombre = "Pa'anga tongano", Simbolo = "T$" },
                new Moneda { CodigoIso = "TRY", Nombre = "Lira turca", Simbolo = "₺" },
                new Moneda { CodigoIso = "TTD", Nombre = "Dólar de Trinidad y Tobago", Simbolo = "TT$" },
                new Moneda { CodigoIso = "TWD", Nombre = "Nuevo dólar taiwanés", Simbolo = "NT$" },
                new Moneda { CodigoIso = "TZS", Nombre = "Chelín tanzano", Simbolo = "Sh" },
                new Moneda { CodigoIso = "UAH", Nombre = "Grivna ucraniana", Simbolo = "₴" },
                new Moneda { CodigoIso = "UGX", Nombre = "Chelín ugandés", Simbolo = "Sh" },
                new Moneda { CodigoIso = "USD", Nombre = "Dólar estadounidense", Simbolo = "$" },
                new Moneda { CodigoIso = "UYU", Nombre = "Peso uruguayo", Simbolo = "$U" },
                new Moneda { CodigoIso = "UZS", Nombre = "Som uzbeko", Simbolo = "лв" },
                new Moneda { CodigoIso = "VES", Nombre = "Bolívar venezolano", Simbolo = "Bs" },
                new Moneda { CodigoIso = "VND", Nombre = "Dong vietnamita", Simbolo = "₫" },
                new Moneda { CodigoIso = "VUV", Nombre = "Vatu de Vanuatu", Simbolo = "Vt" },
                new Moneda { CodigoIso = "WST", Nombre = "Tālā samoano", Simbolo = "T" },
                new Moneda { CodigoIso = "XAF", Nombre = "Franco CFA de África Central", Simbolo = "Fr" },
                new Moneda { CodigoIso = "XCD", Nombre = "Dólar del Caribe Oriental", Simbolo = "EC$" },
                new Moneda { CodigoIso = "XOF", Nombre = "Franco CFA de África Occidental", Simbolo = "Fr" },
                new Moneda { CodigoIso = "XPF", Nombre = "Franco CFP", Simbolo = "Fr" },
                new Moneda { CodigoIso = "YER", Nombre = "Rial yemení", Simbolo = "﷼" },
                new Moneda { CodigoIso = "ZAR", Nombre = "Rand sudafricano", Simbolo = "R" },
                new Moneda { CodigoIso = "ZMW", Nombre = "Kwacha zambiano", Simbolo = "ZK" },
                new Moneda { CodigoIso = "ZWL", Nombre = "Dólar zimbabuense", Simbolo = "$" }
            );
        }
    }

    // Sin semilla: la tabla catalogo_paises (249 filas, ISO 3166-1) la carga el script 016 (decisión D12).
    public sealed class PaisConfiguracion : IEntityTypeConfiguration<Pais>
    {
        public void Configure(EntityTypeBuilder<Pais> entity)
        {
            entity.HasKey(e => e.CodigoIso);
            entity.Property(e => e.CodigoIso).IsRequired().HasMaxLength(2);
            entity.Property(e => e.Nombre).IsRequired().HasMaxLength(100);
        }
    }

    public sealed class CatalogoMunicipioConfiguracion : IEntityTypeConfiguration<CatalogoMunicipio>
    {
        public void Configure(EntityTypeBuilder<CatalogoMunicipio> entity)
        {
            entity.HasOne(m => m.Departamento)
                  .WithMany(d => d.Municipios)
                  .HasForeignKey(m => m.IdDepartamento)
                  .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
