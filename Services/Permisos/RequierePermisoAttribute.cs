using System.Reflection;

namespace eGestion360Web.Services.Permisos
{
    /// <summary>
    /// Declara el permiso que pide una pantalla (puesto en la clase de la página) o uno de sus handlers (puesto en el método, y vale
    /// más que el de la clase). El valor es el código completo del catálogo, por ejemplo <c>flota.vehiculos.editar</c>
    /// (paso F1.6b, ADR-029, D59). Lo lee <see cref="PermisosPageFilter"/>; una prueba de arquitectura exige que cada página tenga este
    /// atributo o <see cref="PaginaSinPermisoAttribute"/> y que el código exista en <see cref="CatalogoDePermisos"/>.
    ///
    /// No se hereda (<c>Inherited = false</c>): cada página declara el suyo. Si una clase base lo diera por todas sus hijas, una
    /// pantalla nueva quedaría cubierta sin que nadie decidiera su permiso.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class RequierePermisoAttribute : Attribute
    {
        public RequierePermisoAttribute(string codigo) => Codigo = codigo;

        /// <summary>Código del permiso (<c>modulo.recurso.accion</c>).</summary>
        public string Codigo { get; }
    }

    /// <summary>
    /// Marca una pantalla que no pide ningún permiso y dice por qué: las que se abren antes de tener sesión (inicio de sesión,
    /// recuperar la clave), las de la propia cuenta (cambiar la clave, cerrar la sesión) y los centros de navegación, cuyas opciones
    /// piden cada una el suyo. Es una decisión visible en la propia página, y una prueba fija la lista completa para que agregar una
    /// excepción obligue a cambiar también la prueba.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PaginaSinPermisoAttribute : Attribute
    {
        public PaginaSinPermisoAttribute(string motivo) => Motivo = motivo;

        /// <summary>Por qué la pantalla no pide permiso.</summary>
        public string Motivo { get; }
    }

    /// <summary>Qué se sabe de lo que exige una pantalla.</summary>
    public enum TipoDeRequisito
    {
        /// <summary>La clase no declaró nada: es un descuido, no una decisión.</summary>
        SinDeclarar,

        /// <summary>La pantalla declaró que no pide permiso.</summary>
        SinPermiso,

        /// <summary>La pantalla (o el handler) pide un permiso del catálogo.</summary>
        Permiso,
    }

    /// <summary>Lo que exige una pantalla para un handler dado.</summary>
    /// <param name="Tipo">Si pide permiso, no pide o no declaró.</param>
    /// <param name="Codigo">El permiso que pide; solo si <paramref name="Tipo"/> es <see cref="TipoDeRequisito.Permiso"/>.</param>
    /// <param name="Motivo">Por qué no pide permiso; solo si <paramref name="Tipo"/> es <see cref="TipoDeRequisito.SinPermiso"/>.</param>
    /// <param name="DelHandler">True si el permiso lo declaró el handler y no la clase.</param>
    public sealed record RequisitoDePagina(TipoDeRequisito Tipo, string? Codigo, string? Motivo, bool DelHandler)
    {
        /// <summary>
        /// Lo que exige el handler de una página: el atributo del método si lo tiene y, si no, el de la clase. Un handler que declara su
        /// permiso lo pide aunque la clase diga <see cref="PaginaSinPermisoAttribute"/>; sin atributos, no declaró nada.
        /// </summary>
        public static RequisitoDePagina De(Type tipoDePagina, MethodInfo? handler)
        {
            var delHandler = handler?.GetCustomAttribute<RequierePermisoAttribute>(inherit: false);
            if (delHandler != null) return new RequisitoDePagina(TipoDeRequisito.Permiso, delHandler.Codigo, null, DelHandler: true);

            var delaClase = tipoDePagina.GetCustomAttribute<RequierePermisoAttribute>(inherit: false);
            if (delaClase != null) return new RequisitoDePagina(TipoDeRequisito.Permiso, delaClase.Codigo, null, DelHandler: false);

            var sinPermiso = tipoDePagina.GetCustomAttribute<PaginaSinPermisoAttribute>(inherit: false);
            if (sinPermiso != null) return new RequisitoDePagina(TipoDeRequisito.SinPermiso, null, sinPermiso.Motivo, DelHandler: false);

            return new RequisitoDePagina(TipoDeRequisito.SinDeclarar, null, null, DelHandler: false);
        }
    }
}
