using System.Collections;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Tests.Infra
{
    /// <summary>
    /// Arma y lee filas mínimas de CUALQUIER entidad del modelo, por reflexión, para las pruebas que recorren todas las entidades
    /// (filtro de tenant, filtro de borrado). Se insertan con un contexto sin filtros y sin claves foráneas.
    /// </summary>
    public static class FilasDePrueba
    {
        /// <summary>Una fila mínima de la entidad para el tenant dado: ids de tenant y de empresa, y texto no vacío.</summary>
        public static object Fabricar(IEntityType entidad, int idTenant, int idEmpresa, string marca, int? valorForaneas = null)
        {
            var tipo = entidad.ClrType;
            var fila = Activator.CreateInstance(tipo)!;
            var foraneas = entidad.GetForeignKeys().SelectMany(k => k.Properties).Select(p => p.Name).ToHashSet();

            foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
            {
                if (p.Name == nameof(ITenantEntity.IdTenant)) p.SetValue(fila, p.PropertyType == typeof(int?) ? (int?)idTenant : idTenant);
                else if (p.Name == "IdEmpresa" && tipo == typeof(eGestion360Web.Models.Empresa)) p.SetValue(fila, idEmpresa + 100);   // la clave de la propia empresa
                else if (p.Name == "IdEmpresa") p.SetValue(fila, p.PropertyType == typeof(int?) ? (int?)idEmpresa : idEmpresa);
                else if (foraneas.Contains(p.Name) && p.PropertyType == typeof(int)) p.SetValue(fila, valorForaneas ?? idTenant);   // que dos filas no choquen en claves únicas
                else if (p.PropertyType == typeof(string) && string.IsNullOrEmpty((string?)p.GetValue(fila))) p.SetValue(fila, marca);
            }

            return fila;
        }

        /// <summary>Las claves que la base no genera (por ejemplo la compuesta de los permisos) se fijan por valor para no chocar.</summary>
        public static void FijarClavesSinGenerar(IEntityType entidad, object fila, int valor)
        {
            foreach (var clave in entidad.FindPrimaryKey()!.Properties.Where(p => p.ValueGenerated == ValueGenerated.Never))
            {
                if (clave.ClrType == typeof(int)) entidad.ClrType.GetProperty(clave.Name)!.SetValue(fila, valor);
            }
        }

        /// <summary>Todas las filas que el contexto devuelve de la entidad (con los filtros que lleve).</summary>
        public static IReadOnlyList<object> Filas(DbContext db, Type tipo) => Filas(db, tipo, ignorar: null);

        /// <summary>Lo mismo, ignorando los filtros con esos nombres (por ejemplo, solo el de borrado).</summary>
        public static IReadOnlyList<object> Filas(DbContext db, Type tipo, IEnumerable<string>? ignorar)
        {
            var set = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(tipo).Invoke(db, null)!;
            if (ignorar != null)
            {
                var metodo = typeof(EntityFrameworkQueryableExtensions).GetMethods()
                    .First(m => m.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters) && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType.IsAssignableFrom(typeof(string[])))
                    .MakeGenericMethod(tipo);
                set = metodo.Invoke(null, new object[] { set, ignorar })!;
            }

            return ((IEnumerable)set).Cast<object>().ToList();
        }

        public static int? TenantDe(object fila) => fila switch
        {
            ITenantEntity e => e.IdTenant,
            ITenantOpcionalEntity o => o.IdTenant,
            _ => null
        };
    }
}
