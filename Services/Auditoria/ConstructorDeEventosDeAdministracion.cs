using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using eGestion360Web.Models;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Auditoria
{
    /// <summary>
    /// Arma las filas de auditoría de seguridad (F1.8b, ADR-030) de los cambios de administración que se guardan con EF: usuarios, roles, los
    /// permisos de un rol, los módulos de una empresa, los accesos y las empresas. Las filas se agregan al mismo contexto que guarda el cambio
    /// (lo hace <see cref="AuditoriaCambiosInterceptor"/>), así que quedan en la misma transacción: si no se puede dejar la fila, el cambio no se
    /// hace (D73).
    ///
    /// Cada cambio deja una fila en <c>dbo.auditoria_seguridad</c> cuando se sabe de qué tenant es, y una en <c>saas.auditoria_plataforma</c> cuando
    /// quien lo hace es del personal de la plataforma (D70: lo que el operador hace dentro de un tenant también lo ve la plataforma) o cuando el
    /// cambio no tiene tenant (por ejemplo, un usuario del personal de la plataforma, que no tiene empresa).
    ///
    /// Nunca guarda claves, correos ni textos libres: de una contraseña solo dice que cambió (<c>claveCambiada</c>), de un correo igual
    /// (<c>correoCambiado</c>) y de una descripción también. Esos nombres no son los prohibidos de D72, que son los de los valores.
    /// </summary>
    public sealed class ConstructorDeEventosDeAdministracion
    {
        private readonly IContextoAuditoria _contexto;
        private readonly IResolvedorDeTenant? _resolvedor;
        private readonly TimeProvider _tiempo;

        public ConstructorDeEventosDeAdministracion(IContextoAuditoria contexto, IResolvedorDeTenant? resolvedor = null, TimeProvider? tiempo = null)
        {
            _contexto = contexto;
            _resolvedor = resolvedor;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        /// <summary>Qué le pasó a la entidad en este guardado.</summary>
        private enum Operacion { Alta, Cambio, Baja }

        /// <summary>Si la entidad es de las que se auditan como administración.</summary>
        public static bool Interesa(EntityEntry entrada) =>
            entrada.Entity is User or EmpresaRol or RolPermiso or EmpresaModulo or AccesoEmpresa or Empresa or Modulo or Tenant or TenantPlan;

        /// <summary>Los permisos de un rol no generan clave en la base: se arman antes de guardar, con el resto de las modificaciones y las bajas.</summary>
        public static bool SeArmaAntesDeGuardar(EntityEntry entrada) => entrada.State != EntityState.Added || entrada.Entity is RolPermiso;

        // ── Antes de guardar: modificaciones, bajas y permisos de rol ───────────

        public async Task<List<object>> FilasAntesAsync(DbContext contexto, IReadOnlyList<EntityEntry> entradas, CancellationToken ct)
        {
            var filas = new List<object>();
            foreach (var entrada in entradas.Where(e => e.Entity is not RolPermiso))
                filas.AddRange(await FilasDeAsync(contexto, entrada, entrada.State == EntityState.Deleted ? Operacion.Baja : Operacion.Cambio, ct));

            filas.AddRange(await FilasDePermisosAsync(contexto, entradas.Where(e => e.Entity is RolPermiso).ToList(), ct));
            return filas;
        }

        // ── Después del primer guardado: altas, que ya tienen su id ─────────────

        public async Task<List<object>> FilasDeAltasAsync(DbContext contexto, IReadOnlyList<object> entidades, CancellationToken ct)
        {
            var filas = new List<object>();
            foreach (var entidad in entidades)
                filas.AddRange(await FilasDeAsync(contexto, contexto.Entry(entidad), Operacion.Alta, ct));
            return filas;
        }

        private Task<List<object>> FilasDeAsync(DbContext contexto, EntityEntry entrada, Operacion operacion, CancellationToken ct) => entrada.Entity switch
        {
            User u => UsuarioAsync(entrada, u, operacion, ct),
            EmpresaRol r => RolAsync(entrada, r, operacion, ct),
            EmpresaModulo m => ModuloAsync(contexto, entrada, m, operacion, ct),
            AccesoEmpresa a => AccesoAsync(entrada, a, operacion, ct),
            Empresa e => EmpresaAsync(entrada, e, operacion, ct),
            Modulo catalogo => Task.FromResult(ModuloDelCatalogo(entrada, catalogo, operacion)),
            Tenant t => Task.FromResult(SuscriptorEstado(entrada, t, operacion)),
            TenantPlan p => PlanDelSuscriptorAsync(contexto, p, operacion, ct),
            _ => Task.FromResult(new List<object>())
        };

        // ── Suscriptores: estado y plan (F1.9a, ADR-032) ────────────────────

        private static readonly Dictionary<string, string> CamposDeEstado = new()
        {
            [nameof(Tenant.Estado)] = "estado",
            [nameof(Tenant.ModoSuspension)] = "modo",
        };

        /// <summary>
        /// El cambio de estado de un suscriptor, con quién y por qué. El motivo lo escribe el operador: se guarda porque es parte del hecho
        /// (D80), con los correos enmascarados. El alta de un suscriptor se audita desde F1.9b.
        /// </summary>
        private List<object> SuscriptorEstado(EntityEntry entrada, Tenant t, Operacion operacion)
        {
            if (operacion != Operacion.Cambio) return new List<object>();
            var detalle = Cambios(entrada, CamposDeEstado);
            if (detalle.Count == 0) return new List<object>();

            if (t.Estado == EstadoTenant.Suspendido) detalle["modoDeSuspension"] = t.ModoSuspension;
            detalle["suscriptor"] = t.Codigo;
            detalle["motivo"] = ConsultaDeAuditoria.EnmascararCorreos(t.MotivoEstado);
            return Crear(TiposDeEvento.TenantEstado, "tenant", t.IdTenant.ToString(), t.IdTenant, null, detalle);
        }

        /// <summary>
        /// El cambio de plan: la fila nueva de <c>tenant_planes</c> dice a cuál pasa; la anterior, ya cerrada en la misma transacción, de cuál
        /// venía. Cerrar una fila no deja evento por sí solo.
        /// </summary>
        private async Task<List<object>> PlanDelSuscriptorAsync(DbContext contexto, TenantPlan nuevo, Operacion operacion, CancellationToken ct)
        {
            if (operacion != Operacion.Alta) return new List<object>();

            var anterior = await contexto.Set<TenantPlan>().AsNoTracking()
                .Where(x => x.IdTenant == nuevo.IdTenant && x.VigenteHasta != null && x.IdTenantPlan != nuevo.IdTenantPlan)
                .OrderByDescending(x => x.VigenteHasta).ThenByDescending(x => x.IdTenantPlan)
                .Select(x => (int?)x.IdPlanVersion)
                .FirstOrDefaultAsync(ct);
            var codigo = await contexto.Set<Tenant>().AsNoTracking()
                .Where(x => x.IdTenant == nuevo.IdTenant).Select(x => x.Codigo).FirstOrDefaultAsync(ct);

            return Crear(TiposDeEvento.TenantPlanCambiado, "tenant", nuevo.IdTenant.ToString(), nuevo.IdTenant, null, new Dictionary<string, object?>
            {
                ["suscriptor"] = codigo,
                ["planAntes"] = await NombreDeVersionAsync(contexto, anterior, ct),
                ["planDespues"] = await NombreDeVersionAsync(contexto, nuevo.IdPlanVersion, ct),
                ["motivo"] = ConsultaDeAuditoria.EnmascararCorreos(nuevo.Motivo),
            });
        }

        /// <summary>Una versión de plan como «flota v1».</summary>
        private static async Task<string?> NombreDeVersionAsync(DbContext contexto, int? idPlanVersion, CancellationToken ct)
        {
            if (idPlanVersion == null) return null;
            var fila = await (from v in contexto.Set<PlanVersion>().AsNoTracking()
                              join p in contexto.Set<Plan>().AsNoTracking() on v.IdPlan equals p.IdPlan
                              where v.IdPlanVersion == idPlanVersion
                              select new { p.Codigo, v.Numero }).FirstOrDefaultAsync(ct);
            return fila == null ? "version-" + idPlanVersion : fila.Codigo + " v" + fila.Numero;
        }

        // ── Usuarios ────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> CamposDeUsuario = new()
        {
            [nameof(User.Username)] = "nombreDeUsuario",
            [nameof(User.Role)] = "rol",
            [nameof(User.EmpresaId)] = "empresa",
            [nameof(User.EmpresaRolId)] = "rolDeEmpresa",
            [nameof(User.IsActive)] = "activo",
            [nameof(User.RequirePasswordChange)] = "debeCambiarClave",
            [nameof(User.PersonaId)] = "persona",
        };

        private async Task<List<object>> UsuarioAsync(EntityEntry entrada, User u, Operacion operacion, CancellationToken ct)
        {
            // La empresa del usuario antes del cambio, para que la baja o el traslado queden en el tenant donde estaba.
            var idEmpresa = u.EmpresaId ?? entrada.Property(nameof(User.EmpresaId)).OriginalValue as int?;
            var idTenant = await TenantDeEmpresaAsync(idEmpresa, ct);

            switch (operacion)
            {
                case Operacion.Alta:
                    return Crear(TiposDeEvento.UsuarioCreado, "usuario", u.Id.ToString(), idTenant, idEmpresa, new Dictionary<string, object?>
                    {
                        ["usuario"] = u.Username, ["rol"] = u.Role, ["empresa"] = u.EmpresaId, ["rolDeEmpresa"] = u.EmpresaRolId,
                        ["activo"] = u.IsActive, ["debeCambiarClave"] = u.RequirePasswordChange, ["persona"] = u.PersonaId,
                    });

                case Operacion.Baja:
                    return Crear(TiposDeEvento.UsuarioEliminado, "usuario", u.Id.ToString(), idTenant, idEmpresa,
                        new Dictionary<string, object?> { ["usuario"] = entrada.Property(nameof(User.Username)).OriginalValue as string ?? u.Username });

                default:
                    var detalle = Cambios(entrada, CamposDeUsuario);
                    if (Cambio(entrada, nameof(User.Password))) detalle["claveCambiada"] = true;
                    if (Cambio(entrada, nameof(User.Email))) detalle["correoCambiado"] = true;
                    if (detalle.Count == 0) return new List<object>();

                    detalle["usuario"] = u.Username;
                    return Crear(TiposDeEvento.UsuarioEditado, "usuario", u.Id.ToString(), idTenant, idEmpresa, detalle);
            }
        }

        // ── Roles ───────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> CamposDeRol = new()
        {
            [nameof(EmpresaRol.Nombre)] = "nombreDelRol",
            [nameof(EmpresaRol.EsAdmin)] = "esAdmin",
            [nameof(EmpresaRol.Activo)] = "activo",
        };

        private async Task<List<object>> RolAsync(EntityEntry entrada, EmpresaRol r, Operacion operacion, CancellationToken ct)
        {
            var idTenant = r.IdTenant > 0 ? r.IdTenant : await TenantDeEmpresaAsync(r.IdEmpresa, ct);

            switch (operacion)
            {
                case Operacion.Alta:
                    return Crear(TiposDeEvento.RolCreado, "rol", r.IdRol.ToString(), idTenant, r.IdEmpresa,
                        new Dictionary<string, object?> { ["nombre"] = r.Nombre, ["esAdmin"] = r.EsAdmin, ["activo"] = r.Activo });

                case Operacion.Baja:
                    return Crear(TiposDeEvento.RolEliminado, "rol", r.IdRol.ToString(), idTenant, r.IdEmpresa, new Dictionary<string, object?> { ["nombre"] = r.Nombre });

                default:
                    var detalle = Cambios(entrada, CamposDeRol);
                    if (Cambio(entrada, nameof(EmpresaRol.Descripcion))) detalle["descripcionCambiada"] = true;
                    if (detalle.Count == 0) return new List<object>();

                    detalle["rol"] = r.Nombre;
                    return Crear(TiposDeEvento.RolEditado, "rol", r.IdRol.ToString(), idTenant, r.IdEmpresa, detalle);
            }
        }

        // ── Permisos de un rol ──────────────────────────────────────────────

        /// <summary>Una fila por rol con lo que se concedió y lo que se retiró en este guardado, con los códigos del catálogo.</summary>
        private async Task<List<object>> FilasDePermisosAsync(DbContext contexto, IReadOnlyList<EntityEntry> entradas, CancellationToken ct)
        {
            var filas = new List<object>();
            var cambios = entradas.Where(e => e.State is EntityState.Added or EntityState.Deleted).ToList();
            if (cambios.Count == 0) return filas;

            var idsDePermiso = cambios.Select(e => ((RolPermiso)e.Entity).IdPermiso).Distinct().ToList();
            var codigos = await contexto.Set<Permiso>().AsNoTracking()
                .Where(p => idsDePermiso.Contains(p.IdPermiso))
                .Select(p => new { p.IdPermiso, p.Codigo })
                .ToDictionaryAsync(p => p.IdPermiso, p => p.Codigo, ct);

            foreach (var porRol in cambios.GroupBy(e => ((RolPermiso)e.Entity).IdRol))
            {
                var primero = (RolPermiso)porRol.First().Entity;
                var rol = await contexto.Set<EmpresaRol>().AsNoTracking()
                    .Where(r => r.IdRol == porRol.Key)
                    .Select(r => new { r.IdTenant, r.IdEmpresa, r.Nombre })
                    .FirstOrDefaultAsync(ct);

                var idTenant = rol?.IdTenant is > 0 ? rol.IdTenant : primero.IdTenant > 0 ? primero.IdTenant : (int?)null;
                string Codigo(RolPermiso p) => codigos.TryGetValue(p.IdPermiso, out var c) ? c : "permiso-" + p.IdPermiso;

                var concedidos = porRol.Where(e => e.State == EntityState.Added).Select(e => Codigo((RolPermiso)e.Entity)).Order(StringComparer.Ordinal).ToList();
                var retirados = porRol.Where(e => e.State == EntityState.Deleted).Select(e => Codigo((RolPermiso)e.Entity)).Order(StringComparer.Ordinal).ToList();

                filas.AddRange(Crear(TiposDeEvento.RolPermisos, "rol", porRol.Key.ToString(), idTenant, rol?.IdEmpresa, new Dictionary<string, object?>
                {
                    ["rol"] = rol?.Nombre, ["concedidos"] = concedidos, ["retirados"] = retirados,
                }));
            }

            return filas;
        }

        // ── Módulos de una empresa ──────────────────────────────────────────

        private static readonly Dictionary<string, string> CamposDeModulo = new()
        {
            [nameof(EmpresaModulo.Activo)] = "activo",
            [nameof(EmpresaModulo.FechaVencimiento)] = "vence",
        };

        private async Task<List<object>> ModuloAsync(DbContext contexto, EntityEntry entrada, EmpresaModulo m, Operacion operacion, CancellationToken ct)
        {
            var idTenant = m.IdTenant > 0 ? m.IdTenant : await TenantDeEmpresaAsync(m.IdEmpresa, ct);
            var modulo = await contexto.Set<Modulo>().AsNoTracking().Where(x => x.IdModulo == m.IdModulo).Select(x => x.Codigo).FirstOrDefaultAsync(ct)
                         ?? "modulo-" + m.IdModulo;

            Dictionary<string, object?> detalle;
            switch (operacion)
            {
                case Operacion.Alta:
                    detalle = new() { ["operacion"] = "alta", ["modulo"] = modulo, ["activo"] = m.Activo, ["vence"] = m.FechaVencimiento };
                    break;
                case Operacion.Baja:
                    detalle = new() { ["operacion"] = "baja", ["modulo"] = modulo };
                    break;
                default:
                    detalle = Cambios(entrada, CamposDeModulo);
                    if (detalle.Count == 0) return new List<object>();
                    detalle["operacion"] = "cambio";
                    detalle["modulo"] = modulo;
                    break;
            }

            return Crear(TiposDeEvento.ModuloCambiado, "empresa_modulo", m.Id.ToString(), idTenant, m.IdEmpresa, detalle);
        }

        // ── Catálogo global de módulos (plataforma) ─────────────────────────

        private static readonly Dictionary<string, string> CamposDelCatalogoDeModulos = new()
        {
            [nameof(Modulo.Activo)] = "activo",
        };

        /// <summary>Activar o desactivar un módulo para todos es cosa de la plataforma: no tiene tenant y la fila va solo a la auditoría de la plataforma.</summary>
        private List<object> ModuloDelCatalogo(EntityEntry entrada, Modulo m, Operacion operacion)
        {
            Dictionary<string, object?> detalle;
            switch (operacion)
            {
                case Operacion.Alta:
                    detalle = new() { ["operacion"] = "alta", ["ambito"] = "catalogo", ["modulo"] = m.Codigo, ["activo"] = m.Activo };
                    break;
                case Operacion.Baja:
                    detalle = new() { ["operacion"] = "baja", ["ambito"] = "catalogo", ["modulo"] = m.Codigo };
                    break;
                default:
                    detalle = Cambios(entrada, CamposDelCatalogoDeModulos);
                    if (detalle.Count == 0) return new List<object>();
                    detalle["operacion"] = "cambio";
                    detalle["ambito"] = "catalogo";
                    detalle["modulo"] = m.Codigo;
                    break;
            }

            return Crear(TiposDeEvento.ModuloCambiado, "modulo", m.IdModulo.ToString(), null, null, detalle);
        }

        // ── Accesos a una empresa ───────────────────────────────────────────

        private static readonly Dictionary<string, string> CamposDeAcceso = new()
        {
            [nameof(AccesoEmpresa.IdRol)] = "rolDeEmpresa",
            [nameof(AccesoEmpresa.RolLegado)] = "rol",
            [nameof(AccesoEmpresa.EsPrincipal)] = "principal",
            [nameof(AccesoEmpresa.Activo)] = "activo",
        };

        private async Task<List<object>> AccesoAsync(EntityEntry entrada, AccesoEmpresa a, Operacion operacion, CancellationToken ct)
        {
            var idTenant = a.IdTenant > 0 ? a.IdTenant : await TenantDeEmpresaAsync(a.IdEmpresa, ct);

            Dictionary<string, object?> detalle;
            switch (operacion)
            {
                case Operacion.Alta:
                    detalle = new()
                    {
                        ["operacion"] = "alta", ["membresia"] = a.IdMembresia, ["rol"] = a.RolLegado, ["rolDeEmpresa"] = a.IdRol,
                        ["principal"] = a.EsPrincipal, ["activo"] = a.Activo,
                    };
                    break;
                case Operacion.Baja:
                    detalle = new() { ["operacion"] = "baja", ["membresia"] = a.IdMembresia, ["rol"] = a.RolLegado };
                    break;
                default:
                    detalle = Cambios(entrada, CamposDeAcceso);
                    if (detalle.Count == 0) return new List<object>();
                    detalle["operacion"] = "cambio";
                    detalle["membresia"] = a.IdMembresia;
                    break;
            }

            return Crear(TiposDeEvento.AccesoCambiado, "acceso_empresa", a.IdAccesoEmpresa.ToString(), idTenant, a.IdEmpresa, detalle);
        }

        // ── Empresas ────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> CamposDeEmpresa = new()
        {
            [nameof(Empresa.RazonSocial)] = "razonSocial",
            [nameof(Empresa.NombreComercial)] = "nombreComercial",
            [nameof(Empresa.PaisIso)] = "pais",
            [nameof(Empresa.MonedaIso)] = "moneda",
            [nameof(Empresa.ZonaHoraria)] = "zonaHoraria",
            [nameof(Empresa.Activa)] = "activa",
            [nameof(Empresa.Eliminado)] = "eliminada",
        };

        private async Task<List<object>> EmpresaAsync(EntityEntry entrada, Empresa e, Operacion operacion, CancellationToken ct)
        {
            var idTenant = e.IdTenant > 0 ? e.IdTenant : await TenantDeEmpresaAsync(e.IdEmpresa, ct);

            switch (operacion)
            {
                case Operacion.Alta:
                    return Crear(TiposDeEvento.EmpresaCreada, "empresa", e.IdEmpresa.ToString(), idTenant, e.IdEmpresa, new Dictionary<string, object?>
                    {
                        ["empresaCodigo"] = e.Codigo, ["razonSocial"] = e.RazonSocial, ["pais"] = e.PaisIso, ["moneda"] = e.MonedaIso,
                        ["zonaHoraria"] = e.ZonaHoraria, ["activa"] = e.Activa,
                    });

                case Operacion.Baja:
                    return Crear(TiposDeEvento.EmpresaEliminada, "empresa", e.IdEmpresa.ToString(), idTenant, e.IdEmpresa,
                        new Dictionary<string, object?> { ["empresaCodigo"] = e.Codigo, ["razonSocial"] = e.RazonSocial });

                default:
                    var detalle = Cambios(entrada, CamposDeEmpresa);
                    if (Cambio(entrada, nameof(Empresa.IdentificadorFiscal))) detalle["identificadorFiscalCambiado"] = true;
                    if (detalle.Count == 0) return new List<object>();

                    detalle["empresaCodigo"] = e.Codigo;
                    var seElimino = e.Eliminado && entrada.Property(nameof(Empresa.Eliminado)).OriginalValue is false;
                    return Crear(seElimino ? TiposDeEvento.EmpresaEliminada : TiposDeEvento.EmpresaEditada, "empresa", e.IdEmpresa.ToString(), idTenant, e.IdEmpresa, detalle);
            }
        }

        // ── Ayudas ──────────────────────────────────────────────────────────

        private async Task<int?> TenantDeEmpresaAsync(int? idEmpresa, CancellationToken ct)
            => idEmpresa is > 0 && _resolvedor != null ? await _resolvedor.ResolverPorEmpresaAsync(idEmpresa.Value, ct) : null;

        private static bool Cambio(EntityEntry entrada, string propiedad)
        {
            var p = entrada.Property(propiedad);
            return p.IsModified && !Equals(p.OriginalValue, p.CurrentValue);
        }

        /// <summary>Solo los campos permitidos que cambiaron, como <c>{ "campo": [antes, después] }</c>.</summary>
        private static Dictionary<string, object?> Cambios(EntityEntry entrada, IReadOnlyDictionary<string, string> campos)
        {
            var cambios = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (propiedad, etiqueta) in campos)
            {
                if (!Cambio(entrada, propiedad)) continue;
                var p = entrada.Property(propiedad);
                cambios[etiqueta] = new[] { p.OriginalValue, p.CurrentValue };
            }
            return cambios;
        }

        /// <summary>La fila del tenant (si se sabe cuál es) y la de la plataforma (si lo hace el personal de la plataforma o no hay tenant).</summary>
        private List<object> Crear(string tipo, string objetoTipo, string objetoId, int? idTenant, int? idEmpresa, Dictionary<string, object?> detalle)
        {
            var json = AuditoriaDeSeguridad.SerializarDetalle(detalle, 0);
            var ahora = _tiempo.GetUtcNow().UtcDateTime;
            var correlacion = string.IsNullOrWhiteSpace(_contexto.IdCorrelacion) ? Guid.NewGuid().ToString("N") : _contexto.IdCorrelacion!;
            var usuario = Cortar(_contexto.Usuario, 100);
            var operador = _contexto.EsOperador;
            var filas = new List<object>();

            if (idTenant is > 0)
                filas.Add(new AuditoriaSeguridad
                {
                    IdTenant = idTenant.Value,
                    FechaHora = ahora, Tipo = tipo, Resultado = ResultadosDeEvento.Ok, IdCuenta = _contexto.IdCuenta, Usuario = usuario, EsOperador = operador,
                    IdEmpresa = idEmpresa, ObjetoTipo = objetoTipo, ObjetoId = objetoId, Detalle = json, Ip = _contexto.Ip, Agente = _contexto.Agente,
                    IdCorrelacion = correlacion,
                });

            if (operador || idTenant is not > 0)
                filas.Add(new AuditoriaPlataforma
                {
                    IdTenant = idTenant is > 0 ? idTenant : null,
                    FechaHora = ahora, Tipo = tipo, Resultado = ResultadosDeEvento.Ok, IdCuenta = _contexto.IdCuenta, Usuario = usuario, EsOperador = operador,
                    IdEmpresa = idEmpresa, ObjetoTipo = objetoTipo, ObjetoId = objetoId, Detalle = json, Ip = _contexto.Ip, Agente = _contexto.Agente,
                    IdCorrelacion = correlacion,
                });

            return filas;
        }

        private static string? Cortar(string? texto, int largo) => string.IsNullOrEmpty(texto) ? null : texto.Length <= largo ? texto : texto[..largo];
    }
}
