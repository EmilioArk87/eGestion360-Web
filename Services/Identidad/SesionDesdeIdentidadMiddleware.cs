namespace eGestion360Web.Services.Identidad
{
    /// <summary>
    /// Deja en la sesión una copia de la identidad de la cookie para el código anterior a F1.5 (D50): unas 50 páginas leen
    /// <c>Session["Username"]</c> para el «creado por», y otras leen <c>UserId</c>, <c>Role</c> o <c>Email</c>. La cookie manda; la sesión
    /// es solo un espejo que se rehace sola si falta (por ejemplo, tras reiniciar el sitio) y se vacía si la cookie ya no sirve. También
    /// guarda en la sesión los módulos y permisos de la empresa (la caché que antes llenaba el Login), y los vuelve a leer cuando la
    /// persona cambia de empresa o de rol.
    ///
    /// Va después de <see cref="Tenancy.TenantMiddleware"/> y del middleware de estado del tenant: para leer los permisos necesita el
    /// tenant de la petición ya fijado.
    /// </summary>
    public sealed class SesionDesdeIdentidadMiddleware
    {
        /// <summary>Claves de sesión que son espejo de la identidad (no lo son el tenant de soporte ni los mensajes de pantalla).</summary>
        public static readonly string[] ClavesDeIdentidad =
            { "UserId", "Username", "Email", "Role", "EmpresaId", "EmpresaRolId", "MustChangePassword", AuthHelper.ClaveNombrePersona };

        private const string ClaveContexto = "ContextoDePermisos";

        private readonly RequestDelegate _siguiente;

        public SesionDesdeIdentidadMiddleware(RequestDelegate siguiente) => _siguiente = siguiente;

        public async Task InvokeAsync(HttpContext context, ICargadorDePermisos cargador, ILogger<SesionDesdeIdentidadMiddleware> log)
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

            // Módulos y permisos de la empresa: caché de la sesión. Se rehace si falta, si la identidad se renovó o si cambió la empresa o el rol.
            if (datos.IdEmpresa is { } idEmpresa && !datos.DebeCambiarClave)
            {
                var contexto = $"{idEmpresa}:{datos.IdRolEmpresa}:{datos.Rol}";
                var renovada = context.Items.ContainsKey(EventosDeCookieDeIdentidad.ClaveIdentidadRenovada);
                if (renovada || sesion.GetString(ClaveContexto) != contexto
                    || string.IsNullOrEmpty(sesion.GetString("Modulos")) || string.IsNullOrEmpty(sesion.GetString("Permisos")))
                {
                    try
                    {
                        var permisos = await cargador.CargarAsync(idEmpresa, datos.IdRolEmpresa, datos.Rol, context.RequestAborted);
                        sesion.SetString("Modulos", System.Text.Json.JsonSerializer.Serialize(permisos.Modulos));
                        sesion.SetString("Permisos", System.Text.Json.JsonSerializer.Serialize(permisos.Permisos));
                        sesion.SetString(ClaveContexto, contexto);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Sin permisos la persona ve menos, nunca más: se deja la caché vacía y se registra para revisarlo.
                        log.LogError(ex, "No se pudieron leer los módulos y permisos de la empresa {IdEmpresa} para la cuenta {IdCuenta}.", idEmpresa, datos.IdCuenta);
                        sesion.Remove("Modulos");
                        sesion.Remove("Permisos");
                        sesion.Remove(ClaveContexto);
                    }
                }
            }
            else
            {
                sesion.Remove("Modulos");
                sesion.Remove("Permisos");
                sesion.Remove(ClaveContexto);
            }

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
            sesion.Remove("Modulos");
            sesion.Remove("Permisos");
            sesion.Remove(ClaveContexto);
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
