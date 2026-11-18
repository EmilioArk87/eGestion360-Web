using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Services.Permisos
{
    /// <summary>Un permiso del catálogo tal como lo ve la pantalla de roles: con su casilla marcada o no.</summary>
    public sealed record PermisoParaMarcar(int IdPermiso, string Codigo, string Accion, string Etiqueta, string Descripcion, bool Concedido);

    /// <summary>Un recurso (pantalla o grupo de pantallas) con sus permisos: una fila de la matriz.</summary>
    public sealed record RecursoParaMarcar(string Recurso, string Nombre, IReadOnlyList<PermisoParaMarcar> Permisos);

    /// <summary>Un módulo habilitado para la empresa del rol, o la administración de la empresa: una tabla de la matriz.</summary>
    public sealed record GrupoParaMarcar(string Codigo, string Nombre, string? Descripcion, string? Icono, bool EsAdministracion, IReadOnlyList<RecursoParaMarcar> Recursos)
    {
        /// <summary>Las acciones de las columnas de la tabla, en el orden en que aparecen (ver, crear, editar, eliminar y después las propias).</summary>
        public IReadOnlyList<string> Acciones => Recursos.SelectMany(r => r.Permisos).Select(p => p.Accion).Distinct()
            .OrderBy(a => PermisosDeRol.PosicionDeAccion(a)).ThenBy(a => a, StringComparer.Ordinal).ToList();
    }

    public sealed record PermisosDelRol(int IdRol, int IdEmpresa, int IdTenant, string NombreRol, bool MarcaEsAdmin, IReadOnlyList<GrupoParaMarcar> Grupos);

    public sealed record ResultadoDeGuardado(int Agregados, int Quitados, int Ignorados)
    {
        public bool HuboCambios => Agregados + Quitados > 0;
    }

    /// <summary>Lee y guarda los permisos de un rol de empresa en el catálogo nuevo (paso F1.6e, ADR-029).</summary>
    public interface IPermisosDeRol
    {
        /// <summary>Los permisos que se le pueden dar al rol, agrupados, con lo que ya tiene marcado. Nulo si el rol no existe (o es de otro tenant).</summary>
        Task<PermisosDelRol?> ObtenerAsync(int idRol, CancellationToken ct = default);

        /// <summary>
        /// Deja al rol con exactamente los permisos marcados <b>entre los que la pantalla ofrece</b> y avisa al evaluador para que el cambio rija en la
        /// petición siguiente. Un código que no se ofrece (de plataforma, de un módulo que la empresa no tiene activo, o inexistente) se ignora; lo que el rol
        /// ya tenga de un módulo que no se ofrece se conserva. Nulo si el rol no existe.
        /// </summary>
        Task<ResultadoDeGuardado?> GuardarAsync(int idRol, IEnumerable<string> codigosMarcados, string usuario, CancellationToken ct = default);
    }

    public sealed class PermisosDeRol : IPermisosDeRol
    {
        private readonly ApplicationDbContext _db;
        private readonly IEvaluadorDePermisos _evaluador;

        public PermisosDeRol(ApplicationDbContext db, IEvaluadorDePermisos evaluador)
        {
            _db = db;
            _evaluador = evaluador;
        }

        private static readonly string[] OrdenDeAcciones = { "ver", "crear", "editar", "eliminar" };

        internal static int PosicionDeAccion(string accion)
        {
            var i = Array.IndexOf(OrdenDeAcciones, accion);
            return i < 0 ? OrdenDeAcciones.Length : i;
        }

        private static readonly Dictionary<string, string> NombresDeRecursos = new(StringComparer.Ordinal)
        {
            ["administracion.usuarios"] = "Usuarios", ["administracion.roles"] = "Roles y permisos", ["administracion.invitaciones"] = "Invitaciones",
            ["flota.vehiculos"] = "Vehículos", ["flota.personas"] = "Personal", ["flota.catalogos"] = "Catálogos de flota", ["flota.mantenimiento"] = "Mantenimiento",
            ["flota.repuestos"] = "Repuestos", ["flota.seguros"] = "Seguros", ["flota.combustible"] = "Combustible", ["flota.peajes"] = "Peajes",
            ["flota.salarios"] = "Salarios", ["flota.salidas"] = "Control de salidas", ["flota.kpi"] = "Indicadores (KPI)",
            ["catalogos.clientes"] = "Clientes", ["catalogos.tasas_cambio"] = "Tasas de cambio",
            ["facturacion.factura"] = "Facturas", ["facturacion.nota"] = "Notas de crédito y débito", ["facturacion.pago"] = "Pagos", ["facturacion.cxc"] = "Cuentas por cobrar",
            ["psicologia.pacientes"] = "Pacientes", ["psicologia.jornadas"] = "Jornadas y brigadas", ["psicologia.atenciones"] = "Atenciones",
            ["psicologia.documentos"] = "Documentos", ["psicologia.catalogos"] = "Catálogos de psicología", ["psicologia.constancias"] = "Constancias",
            ["psicologia.profesionales"] = "Profesionales",
        };

        private static string NombreDeRecurso(string modulo, string recurso)
            => NombresDeRecursos.TryGetValue(modulo + "." + recurso, out var nombre) ? nombre : Capitalizar(recurso.Replace('_', ' '));

        private static string EtiquetaDeAccion(string accion) => Capitalizar(accion.Replace('_', ' '));

        private static string Capitalizar(string texto) => texto.Length == 0 ? texto : char.ToUpperInvariant(texto[0]) + texto[1..];

        /// <summary>Los permisos que se pueden marcar para un rol: la administración de la empresa y los permisos de los módulos que su empresa tiene activos. Nunca los de plataforma.</summary>
        private async Task<(List<Models.Plataforma.Permiso> Permisos, List<Modulo> Modulos)> OfrecidosAsync(EmpresaRol rol, CancellationToken ct)
        {
            var modulos = await _db.EmpresaModulos.AsNoTracking()
                .Where(em => em.IdEmpresa == rol.IdEmpresa && em.Activo && em.Modulo.Activo)
                .Select(em => em.Modulo)
                .OrderBy(m => m.Orden)
                .ToListAsync(ct);
            var codigos = modulos.Select(m => m.Codigo).ToList();

            var permisos = await _db.Permisos.AsNoTracking()
                .Where(p => p.Activo && (p.Ambito == AmbitoDePermiso.Empresa || (p.Ambito == AmbitoDePermiso.Modulo && codigos.Contains(p.Modulo))))
                .OrderBy(p => p.Orden)
                .ToListAsync(ct);

            return (permisos, modulos);
        }

        private Task<EmpresaRol?> RolAsync(int idRol, CancellationToken ct)
            => _db.EmpresaRoles.AsNoTracking().SingleOrDefaultAsync(r => r.IdRol == idRol, ct);

        public async Task<PermisosDelRol?> ObtenerAsync(int idRol, CancellationToken ct = default)
        {
            var rol = await RolAsync(idRol, ct);
            if (rol == null) return null;

            var (permisos, modulos) = await OfrecidosAsync(rol, ct);
            var concedidos = (await _db.RolPermisos.AsNoTracking()
                    .Where(rp => rp.IdRol == rol.IdRol && rp.IdTenant == rol.IdTenant)
                    .Select(rp => rp.IdPermiso)
                    .ToListAsync(ct))
                .ToHashSet();

            GrupoParaMarcar? Armar(string codigo, string nombre, string? descripcion, string? icono, bool administracion, IEnumerable<Models.Plataforma.Permiso> deGrupo)
            {
                var recursos = deGrupo
                    .GroupBy(p => p.Recurso)
                    .Select(g => new RecursoParaMarcar(g.Key, NombreDeRecurso(codigo, g.Key),
                        g.Select(p => new PermisoParaMarcar(p.IdPermiso, p.Codigo, p.Accion, EtiquetaDeAccion(p.Accion), p.Descripcion, concedidos.Contains(p.IdPermiso))).ToList()))
                    .ToList();
                return recursos.Count == 0 ? null : new GrupoParaMarcar(codigo, nombre, descripcion, icono, administracion, recursos);
            }

            var grupos = new List<GrupoParaMarcar>();
            if (Armar("administracion", "Administración de la empresa", "Usuarios, roles e invitaciones de la empresa", "fa-building", true,
                    permisos.Where(p => p.Ambito == AmbitoDePermiso.Empresa)) is { } administracion)
                grupos.Add(administracion);

            foreach (var m in modulos)
                if (Armar(m.Codigo, m.Nombre, m.Descripcion, m.Icono, false, permisos.Where(p => p.Ambito == AmbitoDePermiso.Modulo && p.Modulo == m.Codigo)) is { } grupo)
                    grupos.Add(grupo);

            return new PermisosDelRol(rol.IdRol, rol.IdEmpresa, rol.IdTenant, rol.Nombre, rol.EsAdmin, grupos);
        }

        public async Task<ResultadoDeGuardado?> GuardarAsync(int idRol, IEnumerable<string> codigosMarcados, string usuario, CancellationToken ct = default)
        {
            var rol = await RolAsync(idRol, ct);
            if (rol == null) return null;

            var (ofrecidos, _) = await OfrecidosAsync(rol, ct);
            var porCodigo = ofrecidos.ToDictionary(p => p.Codigo, StringComparer.Ordinal);

            var idsMarcados = new HashSet<int>();
            var ignorados = 0;
            foreach (var codigo in codigosMarcados.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.Ordinal))
            {
                if (porCodigo.TryGetValue(codigo, out var permiso)) idsMarcados.Add(permiso.IdPermiso);
                else ignorados++;
            }

            var idsOfrecidos = ofrecidos.Select(p => p.IdPermiso).ToHashSet();
            var actuales = await _db.RolPermisos
                .Where(rp => rp.IdRol == rol.IdRol && rp.IdTenant == rol.IdTenant)
                .ToListAsync(ct);

            var quitados = 0;
            foreach (var rp in actuales.Where(rp => idsOfrecidos.Contains(rp.IdPermiso) && !idsMarcados.Contains(rp.IdPermiso)))
            {
                _db.RolPermisos.Remove(rp);
                quitados++;
            }

            var yaTiene = actuales.Select(rp => rp.IdPermiso).ToHashSet();
            var agregados = 0;
            var ahora = DateTime.UtcNow;
            foreach (var id in idsMarcados.Where(i => !yaTiene.Contains(i)))
            {
                var permiso = ofrecidos.Single(p => p.IdPermiso == id);
                _db.RolPermisos.Add(new RolPermiso
                {
                    IdTenant = rol.IdTenant, IdRol = rol.IdRol, IdPermiso = id, AmbitoPermiso = permiso.Ambito, CreadoPor = usuario, FechaCreacion = ahora,
                });
                agregados++;
            }

            if (agregados + quitados > 0) await _db.SaveChangesAsync(ct);

            // Aunque no haya cambios se avisa: es barato y evita una lectura vieja si alguien tocó los permisos por otro camino.
            _evaluador.Invalidar(rol.IdTenant);
            return new ResultadoDeGuardado(agregados, quitados, ignorados);
        }
    }
}
