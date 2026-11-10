using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    /// <summary>
    /// Explica por qué el tenant no puede guardar cambios o entrar (suspendido en solo lectura, suspendido en bloqueo o cancelado).
    /// A ella manda <see cref="EstadoDelTenantMiddleware"/> y responde 403 mientras el motivo siga vigente. Lee el estado de lo que el
    /// middleware dejó en la petición: nada del estado viene de la URL ni de un formulario.
    /// </summary>
    [PaginaSinPermiso("Explica por qué no se puede entrar")]
    public class SinAccesoModel : PageModel
    {
        public bool SoloLectura { get; private set; }

        public bool Bloqueado { get; private set; }

        public string NombreDelTenant { get; private set; } = "Tu cuenta";

        public string EstadoEnPalabras { get; private set; } = string.Empty;

        public string? Motivo { get; private set; }

        public IActionResult OnGet()
        {
            if (HttpContext.Items.TryGetValue(EstadoDelTenantMiddleware.ClaveDerechos, out var valor) && valor is DerechosDeLaPeticion derechos)
            {
                var tenant = derechos.Tenant;
                NombreDelTenant = tenant.Codigo;
                Motivo = tenant.MotivoEstado;
                SoloLectura = tenant.EnSoloLectura;
                Bloqueado = tenant.Bloqueado;
                EstadoEnPalabras = tenant.Estado == EstadoTenant.Cancelado ? "cancelado" : "suspendido";
            }

            // Mientras haya un motivo, la respuesta es 403; sin él (alguien llegó a la página con todo en orden) es una página normal.
            if (SoloLectura || Bloqueado) Response.StatusCode = StatusCodes.Status403Forbidden;
            return Page();
        }
    }
}
