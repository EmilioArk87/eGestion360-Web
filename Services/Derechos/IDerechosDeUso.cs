using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Services.Derechos
{
    /// <summary>
    /// Lo que un tenant tiene derecho a usar y en qué estado está (paso F1.7, ADR-010 y ADR-013). El código pregunta por
    /// derechos (<c>modulo.flota</c>, <c>limite.usuarios</c>), nunca por el nombre de un plan.
    /// </summary>
    /// <param name="IdTenant">El tenant.</param>
    /// <param name="Codigo">Su código corto (por ejemplo <c>TRANSGAR</c>).</param>
    /// <param name="Tipo">Uno de <see cref="TipoTenant"/>.</param>
    /// <param name="Estado">Uno de <see cref="EstadoTenant"/>.</param>
    /// <param name="ModoSuspension">Uno de <see cref="ModoDeSuspension"/>; solo importa si el estado es suspendido.</param>
    /// <param name="MotivoEstado">Por qué está en ese estado, si se anotó.</param>
    /// <param name="PlanCodigo">Código del plan vigente; nulo si el tenant no tiene plan.</param>
    /// <param name="Derechos">Códigos de los derechos que concede su plan vigente (vacío si no tiene plan).</param>
    /// <param name="Limites">Tope de cada derecho de tipo límite que concede el plan; <c>null</c> = sin tope.</param>
    public sealed record DerechosDelTenant(
        int IdTenant,
        string Codigo,
        string Tipo,
        string Estado,
        string ModoSuspension,
        string? MotivoEstado,
        string? PlanCodigo,
        IReadOnlySet<string> Derechos,
        IReadOnlyDictionary<string, int?> Limites)
    {
        /// <summary>El tenant opera con normalidad.</summary>
        public bool Opera => Estado == EstadoTenant.Activo;

        /// <summary>Suspendido en modo lectura: ve sus datos pero no puede guardar cambios.</summary>
        public bool EnSoloLectura => Estado == EstadoTenant.Suspendido && ModoSuspension != ModoDeSuspension.Bloqueo;

        /// <summary>No entra a nada: cancelado, o suspendido en modo bloqueo.</summary>
        public bool Bloqueado => Estado == EstadoTenant.Cancelado || (Estado == EstadoTenant.Suspendido && ModoSuspension == ModoDeSuspension.Bloqueo);

        /// <summary>True si el plan concede el derecho.</summary>
        public bool Concede(string codigoDeDerecho) => Derechos.Contains(codigoDeDerecho);

        /// <summary>Tope del límite, o <c>null</c> si no tiene tope (o el plan no lo concede).</summary>
        public int? Limite(string codigoDeLimite) => Limites.TryGetValue(codigoDeLimite, out var tope) ? tope : null;
    }

    /// <summary>
    /// Lo que se sabe de los derechos en una petición: los del tenant y los módulos que su empresa tiene activos y vigentes.
    /// El middleware de estado lo deja en <c>HttpContext.Items</c> una vez por petición para que las pantallas lo lean sin
    /// consultar la base (la mayoría de los accesos son síncronos).
    /// </summary>
    public sealed class DerechosDeLaPeticion
    {
        public DerechosDeLaPeticion(DerechosDelTenant tenant, IReadOnlySet<string> modulosActivosDeLaEmpresa)
        {
            Tenant = tenant;
            ModulosActivosDeLaEmpresa = modulosActivosDeLaEmpresa;
        }

        public DerechosDelTenant Tenant { get; }

        /// <summary>Códigos de módulo que la empresa de la sesión tiene activos y no vencidos hoy.</summary>
        public IReadOnlySet<string> ModulosActivosDeLaEmpresa { get; }

        /// <summary>
        /// Un módulo se puede usar si el plan del tenant lo concede Y la empresa lo tiene activado y vigente. La activación
        /// (<c>empresa_modulos</c>) queda dentro de lo que el plan permite (ADR-010).
        /// </summary>
        public bool TieneModulo(string moduloCodigo)
        {
            var codigo = moduloCodigo.Trim().ToLowerInvariant();
            return Tenant.Concede(CodigosDeDerecho.DeModulo(codigo)) && ModulosActivosDeLaEmpresa.Contains(codigo);
        }
    }

    /// <summary>Resultado de comprobar un límite del plan antes de agregar algo (usuarios, empresas).</summary>
    /// <param name="Permitido">True si se puede agregar uno más.</param>
    /// <param name="Tope">El tope del plan; <c>null</c> si no tiene.</param>
    /// <param name="EnUso">Cuántos hay hoy.</param>
    public sealed record ResultadoDeLimite(bool Permitido, int? Tope, int EnUso)
    {
        public string Mensaje(string quePermite)
            => Tope is { } tope
                ? $"El plan de este tenant permite hasta {tope} {quePermite} y ya hay {EnUso}."
                : string.Empty;
    }

    public interface IDerechosDeUso
    {
        /// <summary>
        /// Los derechos y el estado de un tenant; <c>null</c> si el tenant no existe. Se guardan unos segundos en memoria
        /// (D55) y se invalidan con <see cref="Invalidar"/>.
        /// </summary>
        Task<DerechosDelTenant?> DelTenantAsync(int idTenant, CancellationToken ct = default);

        /// <summary>Códigos de módulo que una empresa tiene activados y no vencidos hoy. Se guardan unos segundos en memoria.</summary>
        Task<IReadOnlySet<string>> ModulosActivosDeLaEmpresaAsync(int idEmpresa, CancellationToken ct = default);

        /// <summary>
        /// Comprueba si el tenant puede agregar un usuario o una empresa más según su plan (D54: solo se aplican
        /// <see cref="CodigosDeDerecho.LimiteUsuarios"/> y <see cref="CodigosDeDerecho.LimiteEmpresas"/>). Sin tope, siempre se
        /// puede. No guarda nada en memoria: cuenta cada vez.
        /// </summary>
        Task<ResultadoDeLimite> PuedeAgregarAsync(int idTenant, string codigoDeLimite, CancellationToken ct = default);

        /// <summary>Olvida lo guardado de un tenant (y de una empresa) para que el siguiente acceso lea la base.</summary>
        void Invalidar(int idTenant, int? idEmpresa = null);
    }
}
