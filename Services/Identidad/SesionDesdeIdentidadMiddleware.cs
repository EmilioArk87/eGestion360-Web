namespace eGestion360Web.Services.Identidad
{
    /// <summary>
    /// Deja en la sesión una copia de la identidad de la cookie para el código anterior a F1.5 (D50): unas 50 páginas leen
    /// <c>Session["Username"]</c> para el «creado por», y otras leen <c>UserId</c>, <c>Role</c> o <c>Email</c>. La cookie manda; la sesión
    /// es solo un espejo que se rehace sola si falta (por ejemplo, tras reiniciar el sitio) y se vacía si la cookie ya no sirve.
    ///
    /// Ya no guarda los módulos ni los permisos de la empresa (F1.6e-3, ADR-029): los módulos los decide en cada petición el plan del
    /// tenant (<see cref="Derechos.EstadoDelTenantMiddleware"/>) y los permisos los evalúa el evaluador de permisos (<c>Services/Permisos</c>).
    /// </summary>
    public sealed class SesionDesdeIdentidadMiddleware
    {
        /// <summary>Claves de sesión que son espejo de la identidad (no lo son el tenant de soporte ni los mensajes de pantalla).</summary>
        public static readonly string[] ClavesDeIdentidad =
            { "UserId", "Username", "Email", "Role", "EmpresaId", "EmpresaRolId", "MustChangePassword", AuthHelper.ClaveNombrePersona };

        private readonly RequestDelegate _siguiente;

        public SesionDesdeIdentidadMiddleware(RequestDelegate siguiente) => _siguiente = siguiente;

        public async Task InvokeAsync(HttpContext context)
        {
            ISession sesion;
            try
            {
                sesion = context.Session;
                await sesion.LoadAsync(context.RequestAborted);
            }
            catch (InvalidOperationException)
            {
                // La petición no tiene sesión configurada: no hay nada que reflejar.
                await _siguiente(context);
                return;
            }

            var datos = ReclamosDeIdentidad.Leer(context.User);
            if (datos == null)
            {
                Vaciar(sesion);
                await _siguiente(context);
                return;
            }

            Reflejar(sesion, datos);

            await _siguiente(context);
        }

        /// <summary>Copia la identidad de la cookie a la sesión, escribiendo solo lo que cambió.</summary>
        public static void Reflejar(ISession sesion, DatosDeIdentidad datos)
        {
            Poner(sesion, "UserId", (datos.IdUserLegado ?? datos.IdCuenta).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Poner(sesion, "Username", datos.NombreUsuario);
            Poner(sesion, "Email", datos.Correo);
            Poner(sesion, "Role", datos.Rol);
            Poner(sesion, AuthHelper.ClaveNombrePersona, datos.NombrePersona);
            Poner(sesion, "EmpresaId", datos.IdEmpresa?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Poner(sesion, "EmpresaRolId", datos.IdRolEmpresa?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Poner(sesion, "MustChangePassword", datos.DebeCambiarClave ? "1" : null);
        }

        /// <summary>Quita de la sesión todo lo que es espejo de una identidad (la cookie ya no sirve o no hay sesión iniciada).</summary>
        public static void Vaciar(ISession sesion)
        {
            foreach (var clave in ClavesDeIdentidad) sesion.Remove(clave);
        }

        private static void Poner(ISession sesion, string clave, string? valor)
        {
            var actual = sesion.GetString(clave);
            if (string.IsNullOrEmpty(valor))
            {
                if (actual != null) sesion.Remove(clave);
            }
            else if (actual != valor)
            {
                sesion.SetString(clave, valor);
            }
        }
    }
}
