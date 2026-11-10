using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Pages
{
    /// <summary>
    /// La persona invitada abre el enlace del correo y elige su contraseña (ADR-028). Es una pantalla sin sesión (ruta de
    /// <c>AmbitoDePlataformaPageFilter.RutasSinSesion</c>): quien llega solo tiene el token, y de él salen el usuario que la
    /// invitación dejó sin activar y su tenant. El enlace sirve una sola vez y vence.
    /// </summary>
    [PaginaSinPermiso("Se entra con el enlace de la invitación, antes de tener sesión")]
    public class AceptarInvitacionModel : PageModel
    {
        private readonly IServicioDeInvitaciones _invitaciones;

        public AceptarInvitacionModel(IServicioDeInvitaciones invitaciones) => _invitaciones = invitaciones;

        /// <summary>El token viaja en la URL del correo y, al enviar el formulario, en un campo oculto; nunca se guarda.</summary>
        [BindProperty]
        public string? Token { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "La contraseña es requerida")]
        [PasswordSeguro]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Debes confirmar la contraseña")]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public ConsultaDeInvitacion Consulta { get; private set; } = new(EstadoDeConsulta.NoEncontrada);

        public string MensajeDeEstado => ServicioDeInvitaciones.MensajeDeEstado(Consulta.Estado);

        public async Task<IActionResult> OnGetAsync(string? token)
        {
            ProtegerElToken();
            Token = token;
            Consulta = await _invitaciones.ConsultarAsync(token, HttpContext.RequestAborted);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            ProtegerElToken();
            Consulta = await _invitaciones.ConsultarAsync(Token, HttpContext.RequestAborted);
            if (Consulta.Estado != EstadoDeConsulta.Valida) return Page();
            if (!ModelState.IsValid) return Page();

            var resultado = await _invitaciones.AceptarAsync(Token, Password, HttpContext.RequestAborted);
            if (!resultado.Ok)
            {
                foreach (var error in resultado.Errores) ModelState.AddModelError(string.Empty, error);
                Consulta = await _invitaciones.ConsultarAsync(Token, HttpContext.RequestAborted);
                return Page();
            }

            TempData["SuccessMessage"] = "Tu cuenta quedó activada. Inicia sesión con tu correo y la contraseña que elegiste.";
            return RedirectToPage("/Login");
        }

        /// <summary>El token está en la URL: que no salga en el encabezado Referer hacia otros sitios ni se guarde en cachés.</summary>
        private void ProtegerElToken()
        {
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["Cache-Control"] = "no-store";
        }
    }
}
