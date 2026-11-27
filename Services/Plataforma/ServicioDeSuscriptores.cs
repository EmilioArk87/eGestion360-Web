using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Derechos;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Plataforma
{
    /// <summary>Una fila de la lista de suscriptores. Los topes nulos significan «sin tope».</summary>
    public sealed record ResumenDeSuscriptor(int IdTenant, string Codigo, string Nombre, string Tipo, string Estado, string ModoSuspension,
        DateTime FechaEstado, string? Plan, int Empresas, int? TopeDeEmpresas, int Usuarios, int? TopeDeUsuarios);

    /// <param name="Nombre">Cómo se muestra: «Módulo Facturación», «Usuarios activos».</param>
    public sealed record DerechoDePlan(string Codigo, string Tipo, string Nombre, int? Tope);

    /// <summary>Una versión de un plan, con lo que concede. <see cref="Etiqueta"/> es «flota v1».</summary>
    public sealed record VersionDePlan(int IdPlanVersion, string Codigo, string Nombre, int Numero, IReadOnlyList<DerechoDePlan> Derechos)
    {
        public string Etiqueta => Codigo + " v" + Numero;
    }

    public sealed record PlanEnElTiempo(string Plan, DateTime Desde, DateTime? Hasta, string? Motivo, string CreadoPor);

    /// <param name="Token">Marca de concurrencia del suscriptor: viaja en el formulario y delata que otro lo cambió mientras tanto.</param>
    public sealed record DetalleDeSuscriptor(ResumenDeSuscriptor Resumen, string? MotivoEstado, string Token, VersionDePlan? PlanActual,
        IReadOnlyList<VersionDePlan> PlanesDisponibles, IReadOnlyList<PlanEnElTiempo> Historial);

    /// <summary>Lo que cambiaría al pasar a otra versión de plan, antes de confirmarlo.</summary>
    public sealed record CambioPrevisto(VersionDePlan Nuevo, IReadOnlyList<string> Gana, IReadOnlyList<string> Pierde, IReadOnlyList<string> Avisos);

    public sealed record CambioDeEstado(int IdTenant, AccionDeEstado Accion, string? Modo, string? Motivo, string? CodigoDeConfirmacion, string? Token);

    /// <param name="IdPlanVersionActual">La versión que la persona veía como vigente (0 si no había): si ya es otra, el cambio no se hace.</param>
    public sealed record CambioDePlan(int IdTenant, int IdPlanVersion, int IdPlanVersionActual, string? Motivo, string? CodigoDeConfirmacion);

    public sealed record ResultadoDeSuscriptor(bool Ok, string? Error)
    {
        public static ResultadoDeSuscriptor Exito() => new(true, null);
        public static ResultadoDeSuscriptor Fallo(string error) => new(false, error);
    }

    public interface IServicioDeSuscriptores
    {
        Task<IReadOnlyList<ResumenDeSuscriptor>> ListarAsync(CancellationToken ct = default);

        /// <summary>El detalle de un suscriptor, o nulo si no existe.</summary>
        Task<DetalleDeSuscriptor?> ObtenerAsync(int idTenant, CancellationToken ct = default);

        /// <summary>Lo que cambiaría con esa versión de plan; nulo si el suscriptor no existe o la versión no está disponible.</summary>
        Task<CambioPrevisto?> PrevisualizarPlanAsync(int idTenant, int idPlanVersion, CancellationToken ct = default);

        Task<ResultadoDeSuscriptor> CambiarEstadoAsync(CambioDeEstado cambio, string usuario, CancellationToken ct = default);

        Task<ResultadoDeSuscriptor> CambiarPlanAsync(CambioDePlan cambio, string usuario, CancellationToken ct = default);
    }

    /// <summary>
    /// La consola del operador sobre los suscriptores (F1.9a, ADR-032): su estado y su plan. Los suscriptores y los planes son del plano de
    /// control (esquema <c>saas</c>, sin RLS); las lecturas de empresas y usuarios necesitan el ámbito de plataforma, que la ruta de la consola
    /// activa. Cada cambio deja su auditoría en la misma transacción (la pone <see cref="Auditoria.AuditoriaCambiosInterceptor"/>), por eso
    /// antes de guardar se fija el tenant del suscriptor: con la política encendida la base solo admite la fila del tenant en su contexto.
    /// </summary>
    public sealed class ServicioDeSuscriptores : IServicioDeSuscriptores
    {
        private const string CambiadoPorOtro = "Alguien cambió este suscriptor mientras lo veías. Vuelve a cargar la página y revisa antes de repetir.";

        private readonly ApplicationDbContext _db;
        private readonly TenantContext? _tenant;
        private readonly IDerechosDeUso? _derechos;
        private readonly TimeProvider _tiempo;

        public ServicioDeSuscriptores(ApplicationDbContext db, TenantContext? tenant = null, IDerechosDeUso? derechos = null, TimeProvider? tiempo = null)
        {
            _db = db;
            _tenant = tenant;
            _derechos = derechos;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        private DateTime Ahora => _tiempo.GetUtcNow().UtcDateTime;

        private DateOnly HoyEnHonduras => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Ahora, ZonaHorariaHonduras.Obtener()));

        // ── Lecturas ────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<ResumenDeSuscriptor>> ListarAsync(CancellationToken ct = default)
        {
            var tenants = await _db.Tenants.AsNoTracking().OrderBy(t => t.Codigo).ToListAsync(ct);
            return await ResumirAsync(tenants, ct);
        }

        public async Task<DetalleDeSuscriptor?> ObtenerAsync(int idTenant, CancellationToken ct = default)
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.IdTenant == idTenant, ct);
            if (tenant == null) return null;

            var resumen = (await ResumirAsync(new[] { tenant }, ct))[0];
            var versiones = await VersionesAsync(ct);

            var historial = await _db.TenantPlanes.AsNoTracking()
                .Where(p => p.IdTenant == idTenant)
                .OrderByDescending(p => p.VigenteDesde).ThenByDescending(p => p.IdTenantPlan)
                .ToListAsync(ct);
            var vigente = historial.FirstOrDefault(p => p.VigenteHasta == null);

            return new DetalleDeSuscriptor(resumen, tenant.MotivoEstado, Convert.ToBase64String(tenant.TokenConcurrencia),
                vigente == null ? null : versiones.Todas.FirstOrDefault(v => v.IdPlanVersion == vigente.IdPlanVersion),
                versiones.Disponibles,
                historial.Select(p => new PlanEnElTiempo(
                    versiones.Todas.FirstOrDefault(v => v.IdPlanVersion == p.IdPlanVersion)?.Etiqueta ?? "versión " + p.IdPlanVersion,
                    p.VigenteDesde, p.VigenteHasta, p.Motivo, p.CreadoPor)).ToList());
        }

        public async Task<CambioPrevisto?> PrevisualizarPlanAsync(int idTenant, int idPlanVersion, CancellationToken ct = default)
        {
            var detalle = await ObtenerAsync(idTenant, ct);
            var nuevo = detalle?.PlanesDisponibles.FirstOrDefault(v => v.IdPlanVersion == idPlanVersion);
            if (detalle == null || nuevo == null) return null;

            var antes = detalle.PlanActual?.Derechos ?? (IReadOnlyList<DerechoDePlan>)Array.Empty<DerechoDePlan>();
            var gana = new List<string>();
            var pierde = new List<string>();

            // Módulos y características: se ganan o se pierden enteros.
            gana.AddRange(nuevo.Derechos.Where(d => d.Tipo != TipoDerecho.Limite && antes.All(a => a.Codigo != d.Codigo)).Select(d => d.Nombre));
            pierde.AddRange(antes.Where(d => d.Tipo != TipoDerecho.Limite && nuevo.Derechos.All(n => n.Codigo != d.Codigo)).Select(d => d.Nombre));

            // Topes: sin la fila del derecho no hay tope, igual que lo aplica DerechosDeUso.
            foreach (var codigo in antes.Concat(nuevo.Derechos).Where(d => d.Tipo == TipoDerecho.Limite).Select(d => d.Codigo).Distinct().Order(StringComparer.Ordinal))
            {
                var topeAntes = antes.FirstOrDefault(d => d.Codigo == codigo)?.Tope;
                var topeDespues = nuevo.Derechos.FirstOrDefault(d => d.Codigo == codigo)?.Tope;
                if (topeAntes == topeDespues) continue;

                var nombre = (nuevo.Derechos.FirstOrDefault(d => d.Codigo == codigo) ?? antes.First(d => d.Codigo == codigo)).Nombre;
                var texto = $"{nombre}: de {Tope(topeAntes)} a {Tope(topeDespues)}";
                var sube = topeDespues == null || (topeAntes != null && topeDespues > topeAntes);
                (sube ? gana : pierde).Add(texto);
            }

            var avisos = new List<string>();
            AvisarTope(avisos, nuevo, CodigosDeDerecho.LimiteEmpresas, detalle.Resumen.Empresas, "empresas");
            AvisarTope(avisos, nuevo, CodigosDeDerecho.LimiteUsuarios, detalle.Resumen.Usuarios, "usuarios activos");
            if (antes.Any(d => d.Tipo == TipoDerecho.Modulo && nuevo.Derechos.All(n => n.Codigo != d.Codigo)))
                avisos.Add("Las pantallas de los módulos que se pierden dejan de abrirse en menos de un minuto. Sus datos se conservan.");

            return new CambioPrevisto(nuevo, gana, pierde, avisos);
        }

        private static string Tope(int? tope) => tope?.ToString() ?? "sin tope";

        private static void AvisarTope(List<string> avisos, VersionDePlan nuevo, string codigo, int enUso, string que)
        {
            var tope = nuevo.Derechos.FirstOrDefault(d => d.Codigo == codigo)?.Tope;
            if (tope is { } t && enUso > t)
                avisos.Add($"El plan nuevo permite hasta {t} {que} y hoy hay {enUso}. No se borra nada, pero no podrá agregar más.");
        }

        /// <summary>Los conteos usan las mismas reglas con las que se aplican los topes (D54).</summary>
        private async Task<IReadOnlyList<ResumenDeSuscriptor>> ResumirAsync(IReadOnlyList<Tenant> tenants, CancellationToken ct)
        {
            var ids = tenants.Select(t => t.IdTenant).ToList();

            var empresas = await _db.Empresas.AsNoTracking()
                .Where(e => !e.Eliminado && ids.Contains(e.IdTenant))
                .GroupBy(e => e.IdTenant)
                .Select(g => new { IdTenant = g.Key, Cantidad = g.Count() })
                .ToDictionaryAsync(g => g.IdTenant, g => g.Cantidad, ct);

            var usuarios = await (from u in _db.Users.AsNoTracking()
                                  where u.IsActive && u.EmpresaId != null
                                  join e in _db.Empresas.AsNoTracking() on u.EmpresaId equals e.IdEmpresa
                                  where !e.Eliminado && ids.Contains(e.IdTenant)
                                  group u by e.IdTenant into g
                                  select new { IdTenant = g.Key, Cantidad = g.Count() })
                .ToDictionaryAsync(g => g.IdTenant, g => g.Cantidad, ct);

            var versiones = await VersionesAsync(ct);
            var vigentes = await _db.TenantPlanes.AsNoTracking()
                .Where(p => p.VigenteHasta == null && ids.Contains(p.IdTenant))
                .Select(p => new { p.IdTenant, p.IdPlanVersion })
                .ToListAsync(ct);

            return tenants.Select(t =>
            {
                var vigente = vigentes.FirstOrDefault(v => v.IdTenant == t.IdTenant);
                var plan = vigente == null ? null : versiones.Todas.FirstOrDefault(v => v.IdPlanVersion == vigente.IdPlanVersion);
                return new ResumenDeSuscriptor(t.IdTenant, t.Codigo, t.Nombre, t.Tipo, t.Estado, t.ModoSuspension, t.FechaEstado, plan?.Etiqueta,
                    empresas.GetValueOrDefault(t.IdTenant), plan?.Derechos.FirstOrDefault(d => d.Codigo == CodigosDeDerecho.LimiteEmpresas)?.Tope,
                    usuarios.GetValueOrDefault(t.IdTenant), plan?.Derechos.FirstOrDefault(d => d.Codigo == CodigosDeDerecho.LimiteUsuarios)?.Tope);
            }).ToList();
        }

        /// <summary>Todas las versiones de plan con sus derechos, y las que hoy se pueden asignar: de un plan activo y vigentes en Honduras.</summary>
        private async Task<(IReadOnlyList<VersionDePlan> Todas, IReadOnlyList<VersionDePlan> Disponibles)> VersionesAsync(CancellationToken ct)
        {
            var filas = await (from v in _db.PlanVersiones.AsNoTracking()
                               join p in _db.Planes.AsNoTracking() on v.IdPlan equals p.IdPlan
                               select new { v.IdPlanVersion, p.Codigo, p.Nombre, p.Activo, v.Numero, v.VigenteDesde, v.VigenteHasta }).ToListAsync(ct);
            var derechos = await (from pvd in _db.PlanVersionDerechos.AsNoTracking()
                                  join d in _db.Derechos.AsNoTracking() on pvd.IdDerecho equals d.IdDerecho
                                  select new { pvd.IdPlanVersion, d.Codigo, d.Tipo, d.Descripcion, pvd.ValorLimite }).ToListAsync(ct);
            var modulos = (await _db.Modulos.AsNoTracking().Select(m => new { m.Codigo, m.Nombre }).ToListAsync(ct))
                .ToDictionary(m => CodigosDeDerecho.DeModulo(m.Codigo), m => m.Nombre, StringComparer.Ordinal);

            // En pantalla, un nombre corto: la descripción guardada con cada derecho está pensada para quien edita la base.
            string NombreDe(string codigo, string descripcion) => codigo switch
            {
                CodigosDeDerecho.LimiteEmpresas => "Empresas",
                CodigosDeDerecho.LimiteUsuarios => "Usuarios activos",
                _ => modulos.TryGetValue(codigo, out var modulo) ? "Módulo " + modulo : descripcion,
            };

            var hoy = HoyEnHonduras;
            var todas = filas
                .OrderBy(f => f.Codigo, StringComparer.Ordinal).ThenBy(f => f.Numero)
                .Select(f => new
                {
                    Disponible = f.Activo && f.VigenteDesde <= hoy && (f.VigenteHasta == null || f.VigenteHasta >= hoy),
                    Version = new VersionDePlan(f.IdPlanVersion, f.Codigo, f.Nombre, f.Numero,
                        derechos.Where(d => d.IdPlanVersion == f.IdPlanVersion)
                            .OrderBy(d => d.Codigo, StringComparer.Ordinal)
                            .Select(d => new DerechoDePlan(d.Codigo, d.Tipo, NombreDe(d.Codigo, d.Descripcion), d.ValorLimite)).ToList()),
                })
                .ToList();

            return (todas.Select(t => t.Version).ToList(), todas.Where(t => t.Disponible).Select(t => t.Version).ToList());
        }

        // ── Cambios ─────────────────────────────────────────────────────────

        public async Task<ResultadoDeSuscriptor> CambiarEstadoAsync(CambioDeEstado cambio, string usuario, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(cambio);

            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.IdTenant == cambio.IdTenant, ct);
            if (tenant == null) return ResultadoDeSuscriptor.Fallo("El suscriptor no existe.");
            if (!ReglasDeSuscriptor.SePuedeAdministrar(tenant.Tipo))
                return ResultadoDeSuscriptor.Fallo("El suscriptor interno es la propia plataforma: no se cambia desde la consola.");
            if (cambio.Token != Convert.ToBase64String(tenant.TokenConcurrencia)) return ResultadoDeSuscriptor.Fallo(CambiadoPorOtro);
            if (!ReglasDeSuscriptor.AccionesDesde(tenant.Estado).Contains(cambio.Accion))
                return ResultadoDeSuscriptor.Fallo($"Esa acción no cabe: el suscriptor está {tenant.Estado}.");

            var error = Revisar(cambio.Motivo, cambio.CodigoDeConfirmacion, tenant.Codigo, out var motivo);
            if (error != null) return ResultadoDeSuscriptor.Fallo(error);

            if (ReglasDeSuscriptor.PideModo(cambio.Accion))
            {
                if (cambio.Modo == null || !ModoDeSuspension.Todos.Contains(cambio.Modo))
                    return ResultadoDeSuscriptor.Fallo("Elige cómo queda suspendido: solo lectura o bloqueo total.");
                if (cambio.Accion == AccionDeEstado.CambiarModo && cambio.Modo == tenant.ModoSuspension)
                    return ResultadoDeSuscriptor.Fallo("Ya está en ese modo de suspensión.");
                tenant.ModoSuspension = cambio.Modo;
            }

            var ahora = Ahora;
            tenant.Estado = ReglasDeSuscriptor.EstadoDespues(cambio.Accion);
            tenant.FechaEstado = ahora;
            tenant.MotivoEstado = motivo;
            tenant.ModificadoPor = Cortar(usuario);
            tenant.FechaModificacion = ahora;

            _tenant?.Fijar(tenant.IdTenant, null);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ResultadoDeSuscriptor.Fallo(CambiadoPorOtro);
            }

            _derechos?.Invalidar(tenant.IdTenant);
            return ResultadoDeSuscriptor.Exito();
        }

        public async Task<ResultadoDeSuscriptor> CambiarPlanAsync(CambioDePlan cambio, string usuario, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(cambio);

            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.IdTenant == cambio.IdTenant, ct);
            if (tenant == null) return ResultadoDeSuscriptor.Fallo("El suscriptor no existe.");
            if (!ReglasDeSuscriptor.SePuedeAdministrar(tenant.Tipo))
                return ResultadoDeSuscriptor.Fallo("El suscriptor interno es la propia plataforma: no se cambia desde la consola.");
            if (tenant.Estado == EstadoTenant.Cancelado)
                return ResultadoDeSuscriptor.Fallo("El suscriptor está cancelado: recupéralo antes de cambiar su plan.");

            var versiones = await VersionesAsync(ct);
            if (versiones.Disponibles.All(v => v.IdPlanVersion != cambio.IdPlanVersion))
                return ResultadoDeSuscriptor.Fallo("Ese plan no está disponible para asignar.");

            var error = Revisar(cambio.Motivo, cambio.CodigoDeConfirmacion, tenant.Codigo, out var motivo);
            if (error != null) return ResultadoDeSuscriptor.Fallo(error);

            // El contexto del suscriptor se fija ANTES de abrir la transacción: la conexión toma de él su tenant al abrirse.
            _tenant?.Fijar(tenant.IdTenant, null);
            await using var transaccion = await _db.Database.BeginTransactionAsync(ct);

            var vigente = await _db.TenantPlanes.FirstOrDefaultAsync(p => p.IdTenant == tenant.IdTenant && p.VigenteHasta == null, ct);
            if ((vigente?.IdPlanVersion ?? 0) != cambio.IdPlanVersionActual) return ResultadoDeSuscriptor.Fallo(CambiadoPorOtro);
            if (vigente?.IdPlanVersion == cambio.IdPlanVersion) return ResultadoDeSuscriptor.Fallo("Ya tiene ese plan.");

            var ahora = Ahora;
            var quien = Cortar(usuario)!;
            try
            {
                // Primero se cierra el vigente y después entra el nuevo: la base solo admite un plan abierto por suscriptor.
                if (vigente != null)
                {
                    vigente.VigenteHasta = ahora;
                    vigente.ModificadoPor = quien;
                    vigente.FechaModificacion = ahora;
                    await _db.SaveChangesAsync(ct);
                }

                _db.TenantPlanes.Add(new TenantPlan
                {
                    IdTenant = tenant.IdTenant, IdPlanVersion = cambio.IdPlanVersion, VigenteDesde = ahora, Motivo = motivo,
                    CreadoPor = quien, FechaCreacion = ahora,
                });
                await _db.SaveChangesAsync(ct);
                await transaccion.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Otro cambio de plan entró a la vez (índice único del plan vigente) o no se pudo dejar el rastro: no queda nada.
                await transaccion.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return ResultadoDeSuscriptor.Fallo("No se pudo cambiar el plan y no se guardó nada. Vuelve a cargar la página e intenta de nuevo.");
            }

            _derechos?.Invalidar(tenant.IdTenant);
            return ResultadoDeSuscriptor.Exito();
        }

        /// <summary>Motivo obligatorio y confirmación escribiendo el código del suscriptor (D80).</summary>
        private static string? Revisar(string? motivo, string? confirmacion, string codigo, out string limpio)
        {
            limpio = (motivo ?? string.Empty).Trim();
            if (limpio.Length == 0) return "Escribe el motivo del cambio.";
            if (limpio.Length > ReglasDeSuscriptor.LargoMaximoDelMotivo)
                return $"El motivo admite hasta {ReglasDeSuscriptor.LargoMaximoDelMotivo} caracteres.";
            return string.Equals((confirmacion ?? string.Empty).Trim(), codigo, StringComparison.OrdinalIgnoreCase)
                ? null
                : $"Para confirmar, escribe el código del suscriptor: {codigo}.";
        }

        private static string? Cortar(string? texto) => string.IsNullOrEmpty(texto) ? null : texto.Length <= 100 ? texto : texto[..100];
    }
}
