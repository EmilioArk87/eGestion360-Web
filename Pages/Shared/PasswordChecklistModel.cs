namespace eGestion360Web.Pages.Shared
{
    /// <summary>
    /// Datos que necesita el partial <c>_PasswordChecklist</c>: los id de los inputs
    /// a los que se engancha el checklist en vivo.
    /// </summary>
    /// <param name="IdPassword">Id del input de la contraseña.</param>
    /// <param name="IdConfirmacion">
    /// Id del input de confirmación. Si se omite, no se muestra la regla de coincidencia.
    /// </param>
    /// <param name="IdsContexto">
    /// Ids de los campos cuyo valor no debe aparecer dentro de la contraseña
    /// (usuario, email). Si se omiten, esa regla no se muestra.
    /// </param>
    public record PasswordChecklistModel(
        string IdPassword,
        string? IdConfirmacion = null,
        params string[] IdsContexto);
}
