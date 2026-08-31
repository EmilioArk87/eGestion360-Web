using System.ComponentModel.DataAnnotations;

namespace eGestion360Web.Services
{
    /// <summary>
    /// Aplica <see cref="PasswordPolicy"/> a una propiedad de contraseña.
    /// </summary>
    /// <remarks>
    /// Los nombres que se le pasan son propiedades del mismo PageModel cuyo valor no
    /// debe aparecer dentro de la contraseña, por ejemplo
    /// <c>[PasswordSeguro(nameof(Username), nameof(Email))]</c>. Si alguna no existe
    /// se ignora, así que el atributo no rompe cuando el modelo no tiene esos datos.
    ///
    /// La validación es solo de servidor: no hay adaptador para jquery-validation, de
    /// modo que el mensaje aparece al enviar el formulario y no mientras se escribe.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class PasswordSeguroAttribute : ValidationAttribute
    {
        private readonly string[] _propiedadesDeContexto;

        public PasswordSeguroAttribute(params string[] propiedadesDeContexto)
        {
            _propiedadesDeContexto = propiedadesDeContexto ?? Array.Empty<string>();
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            var password = value as string;

            var contexto = new List<string?>(_propiedadesDeContexto.Length);
            foreach (var nombre in _propiedadesDeContexto)
            {
                var propiedad = validationContext.ObjectType.GetProperty(nombre);
                if (propiedad != null)
                {
                    contexto.Add(propiedad.GetValue(validationContext.ObjectInstance) as string);
                }
            }

            var error = PasswordPolicy.Validar(password, contexto.ToArray());
            if (error == null)
            {
                return ValidationResult.Success;
            }

            return new ValidationResult(
                error,
                validationContext.MemberName == null
                    ? Array.Empty<string>()
                    : new[] { validationContext.MemberName });
        }
    }
}
