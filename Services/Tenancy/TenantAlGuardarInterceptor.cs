using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using eGestion360Web.Models;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Services.Tenancy
{
    /// <summary>
    /// Pone el <c>id_tenant</c> a lo que se inserta y no deja que cambie lo ya guardado (paso F1.2, ADR-001). El código de las
    /// pantallas y los servicios no asigna el tenant: lo deduce este interceptor, siempre en el servidor y nunca desde un
    /// formulario. Para una fila nueva que no trae tenant, en este orden:
    ///
    ///   1. el tenant de su empresa (<c>IdEmpresa</c>), de la empresa rastreada o, si no, de la base;
    ///   2. el de su registro padre: el que enlaza por navegación o por clave y está rastreado (una línea de factura toma el
    ///      tenant de su factura, un permiso el de su rol, un documento el de su persona);
    ///   3. el de sus registros hijos ya cargados (una persona nueva toma el de su vínculo con la empresa);
    ///   4. el de su persona, para la bitácora de un usuario del sistema;
    ///   5. el tenant de la petición (<see cref="ITenantContext"/>), para lo que no cuelga de nada.
    ///
    /// Si ninguno lo da, falla en lugar de guardar la fila sin dueño: un dato sin tenant es un dato que ningún filtro de
    /// aislamiento ve (F1.3) y que RLS rechaza (F1.4). Un tenant que ya viene puesto se respeta: así el alta de una empresa
    /// siembra sus roles y módulos en el tenant que eligió el administrador, aunque su propia sesión sea de otro.
    ///
    /// En las filas modificadas, <c>id_tenant</c> nunca se actualiza: si el código intenta cambiarlo falla, y si la entidad
    /// llegó de un formulario (adjuntada con el tenant en cero) la columna se deja fuera del UPDATE para no borrarla.
    ///
    /// Una tabla mixta (<see cref="ITenantOpcionalEntity"/>, ADR-015) queda sin tenant cuando la fila no tiene empresa: es
    /// una fila global, como la tasa oficial del BCH.
    ///
    /// Va registrado después del interceptor de auditoría, que agrega las filas de la bitácora antes de guardar; igual, cada
    /// fila de la bitácora ya se arma con el tenant de lo que audita y este interceptor solo completa el resto.
    /// </summary>
    public sealed class TenantAlGuardarInterceptor : SaveChangesInterceptor
    {
        private const string PropiedadTenant = nameof(ITenantEntity.IdTenant);
        private const string PropiedadEmpresa = "IdEmpresa";

        private readonly ITenantContext _tenant;
        private readonly IResolvedorDeTenant _resolvedor;

        public TenantAlGuardarInterceptor(ITenantContext tenant, IResolvedorDeTenant resolvedor)
        {
            _tenant = tenant;
            _resolvedor = resolvedor;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context != null)
                AsignarAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null)
                await AsignarAsync(eventData.Context, cancellationToken);
            return result;
        }

        internal static bool EsDeTenant(object entidad) => entidad is ITenantEntity or ITenantOpcionalEntity;

        private async Task AsignarAsync(DbContext contexto, CancellationToken ct)
        {
            var nuevas = new List<EntityEntry>();
            foreach (var entrada in contexto.ChangeTracker.Entries().Where(e => EsDeTenant(e.Entity)).ToList())
            {
                switch (entrada.State)
                {
                    case EntityState.Modified:
                        ProtegerTenant(entrada);
                        break;
                    case EntityState.Added when SinTenant(entrada):
                        nuevas.Add(entrada);
                        break;
                }
            }

            if (nuevas.Count > 0) await AsignarNuevasAsync(contexto, nuevas, ct);

            VerificarReferencias(contexto);
        }

        private async Task AsignarNuevasAsync(DbContext contexto, List<EntityEntry> nuevas, CancellationToken ct)
        {
            // Por empresa y por padre, repitiendo mientras haya avance: una línea de factura espera a que su factura reciba tenant.
            var pendientes = nuevas;
            var avanzo = true;
            while (pendientes.Count > 0 && avanzo)
            {
                avanzo = false;
                foreach (var entrada in pendientes.ToList())
                {
                    var idTenant = await DeducirSinPeticionAsync(contexto, entrada, ct);
                    if (idTenant == null) continue;

                    Poner(entrada, idTenant.Value);
                    pendientes.Remove(entrada);
                    avanzo = true;
                }
            }

            // Lo que no cuelga de nada toma el tenant de la petición.
            foreach (var entrada in pendientes.ToList())
            {
                if (EsGlobal(entrada)) { pendientes.Remove(entrada); continue; }
                if (_tenant.IdTenant is not > 0) continue;

                Poner(entrada, _tenant.IdTenant.Value);
                pendientes.Remove(entrada);
            }

            if (pendientes.Count > 0)
                throw new InvalidOperationException(
                    "No se puede guardar sin tenant: " + string.Join(", ", pendientes.Select(Describir)) +
                    ". Indica la empresa de la fila, enlázala a su registro padre o guarda desde una petición con empresa en sesión.");
        }

        // ── Qué tenant corresponde ──────────────────────────────────────────

        private async Task<int?> DeducirSinPeticionAsync(DbContext contexto, EntityEntry entrada, CancellationToken ct)
        {
            // 1. Su empresa.
            var idEmpresa = LeerEntero(entrada, PropiedadEmpresa);
            if (idEmpresa is > 0)
            {
                var rastreada = contexto.ChangeTracker.Entries<Empresa>()
                    .Select(e => e.Entity)
                    .FirstOrDefault(e => e.IdEmpresa == idEmpresa && e.IdTenant > 0);
                if (rastreada != null) return rastreada.IdTenant;

                var deLaBase = await _resolvedor.ResolverPorEmpresaAsync(idEmpresa.Value, ct);
                if (deLaBase != null) return deLaBase;

                // Con empresa pero sin tenant no se adivina con el de la petición: sería asignarla a otro dueño.
                throw new InvalidOperationException(
                    $"La empresa {idEmpresa} no existe o no tiene tenant: no se puede guardar {Describir(entrada)}.");
            }

            // 2. Su registro padre.
            var delPadre = TenantDelPadre(contexto, entrada);
            if (delPadre != null) return delPadre;

            // 3. Sus registros hijos, ya con tenant: una persona nueva toma el de su vínculo con la empresa.
            var deUnHijo = TenantDeUnHijo(entrada);
            if (deUnHijo != null) return deUnHijo;

            // 4. Su persona (la bitácora de un usuario del sistema).
            if (entrada.Entity is BitacoraCambio { IdPersona: > 0 } fila)
            {
                var persona = contexto.ChangeTracker.Entries<Persona>()
                    .Select(e => e.Entity)
                    .FirstOrDefault(p => p.IdPersona == fila.IdPersona && p.IdTenant > 0);
                if (persona != null) return persona.IdTenant;

                return await _resolvedor.ResolverPorPersonaAsync(fila.IdPersona.Value, ct);
            }

            return null;
        }

        /// <summary>Tenant de un registro hijo que ya lo tiene, por las colecciones cargadas de la entidad.</summary>
        private static int? TenantDeUnHijo(EntityEntry entrada)
        {
            foreach (var coleccion in entrada.Collections)
            {
                if (coleccion.CurrentValue == null) continue;
                foreach (var hijo in coleccion.CurrentValue)
                {
                    if (hijo is ITenantEntity { IdTenant: > 0 } conTenant) return conTenant.IdTenant;
                }
            }

            return null;
        }

        /// <summary>Tenant del registro padre enlazado y ya rastreado, por navegación o por clave.</summary>
        private static int? TenantDelPadre(DbContext contexto, EntityEntry entrada)
        {
            foreach (var fk in entrada.Metadata.GetForeignKeys())
            {
                if (!typeof(ITenantEntity).IsAssignableFrom(fk.PrincipalEntityType.ClrType)) continue;

                var padre = PadreRastreado(contexto, entrada, fk);
                if (padre is { IdTenant: > 0 }) return padre.IdTenant;
            }

            return null;
        }

        /// <summary>El registro padre de una clave foránea si está rastreado: el de la navegación o el que coincide por clave.</summary>
        private static ITenantEntity? PadreRastreado(DbContext contexto, EntityEntry entrada, IForeignKey fk)
        {
            var navegacion = fk.DependentToPrincipal;
            if (navegacion != null && entrada.Reference(navegacion.Name).CurrentValue is ITenantEntity porNavegacion)
                return porNavegacion;

            if (fk.Properties.Count != 1 || fk.PrincipalKey.Properties.Count != 1) return null;

            var valor = entrada.Property(fk.Properties[0].Name).CurrentValue;
            if (valor == null) return null;

            var clavePadre = fk.PrincipalKey.Properties[0].Name;
            foreach (var otra in contexto.ChangeTracker.Entries().Where(e => e.Metadata == fk.PrincipalEntityType))
            {
                if (otra.Entity is ITenantEntity rastreado && Equals(otra.Property(clavePadre).CurrentValue, valor))
                    return rastreado;
            }

            return null;
        }

        /// <summary>
        /// Ninguna fila nueva, ni una referencia que cambia, puede apuntar a un registro rastreado de otro tenant. Es una red
        /// de seguridad del código: el aislamiento firme lo dan las claves compuestas (F1.3) y Row-Level Security (F1.4).
        /// </summary>
        private static void VerificarReferencias(DbContext contexto)
        {
            var entradas = contexto.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified && EsDeTenant(e.Entity))
                .ToList();

            foreach (var entrada in entradas)
            {
                var propio = TenantDe(entrada.Entity);
                if (propio == null) continue;

                foreach (var fk in entrada.Metadata.GetForeignKeys())
                {
                    if (!typeof(ITenantEntity).IsAssignableFrom(fk.PrincipalEntityType.ClrType)) continue;
                    if (entrada.State == EntityState.Modified && !fk.Properties.Any(p => entrada.Property(p.Name).IsModified)) continue;

                    var padre = PadreRastreado(contexto, entrada, fk);
                    if (padre is { IdTenant: > 0 } && padre.IdTenant != propio)
                        throw new InvalidOperationException(
                            $"{Describir(entrada)} (tenant {propio}) apunta a un registro de {fk.PrincipalEntityType.ClrType.Name} " +
                            $"de otro tenant ({padre.IdTenant}).");
                }
            }
        }

        private static int? TenantDe(object entidad) => entidad switch
        {
            ITenantEntity e => e.IdTenant > 0 ? e.IdTenant : null,
            ITenantOpcionalEntity o => o.IdTenant,
            _ => null
        };

        // ── Modificaciones ──────────────────────────────────────────────────

        /// <summary>El tenant de una fila guardada no cambia, y una entidad que llegó sin tenant no debe borrarlo.</summary>
        private static void ProtegerTenant(EntityEntry entrada)
        {
            var propiedad = entrada.Property(PropiedadTenant);
            var original = propiedad.OriginalValue;
            var actual = propiedad.CurrentValue;

            var vacio = actual == null || actual is 0;
            var originalVacio = original == null || original is 0;

            // Entidad adjuntada desde un formulario: no trae tenant. Se deja fuera del UPDATE.
            if (vacio && originalVacio)
            {
                propiedad.IsModified = false;
                return;
            }

            if (!Equals(original, actual))
                throw new InvalidOperationException(
                    $"El tenant de {Describir(entrada)} no se puede cambiar (de {original ?? "global"} a {actual ?? "global"}).");

            propiedad.IsModified = false;
        }

        // ── Utilidades ──────────────────────────────────────────────────────

        private static bool SinTenant(EntityEntry entrada) => entrada.Entity switch
        {
            ITenantEntity e => e.IdTenant == 0,
            // Una fila mixta sin empresa es global y se queda sin tenant; con empresa se le calcula.
            ITenantOpcionalEntity o => o.IdTenant == null && LeerEntero(entrada, PropiedadEmpresa) is > 0,
            _ => false
        };

        private static bool EsGlobal(EntityEntry entrada) =>
            entrada.Entity is ITenantOpcionalEntity && LeerEntero(entrada, PropiedadEmpresa) is not > 0;

        private static void Poner(EntityEntry entrada, int idTenant)
        {
            switch (entrada.Entity)
            {
                case ITenantEntity e: e.IdTenant = idTenant; break;
                case ITenantOpcionalEntity o: o.IdTenant = idTenant; break;
            }
        }

        private static int? LeerEntero(EntityEntry entrada, string propiedad)
        {
            if (entrada.Metadata.FindProperty(propiedad) == null) return null;
            return entrada.Property(propiedad).CurrentValue is int n ? n : null;
        }

        private static string Describir(EntityEntry entrada)
        {
            var clave = entrada.Metadata.FindPrimaryKey()?.Properties
                .Select(p => entrada.Property(p.Name).CurrentValue?.ToString())
                .Where(v => v != null);
            var id = clave == null ? "" : string.Join("-", clave);
            return string.IsNullOrEmpty(id) || id == "0"
                ? entrada.Metadata.ClrType.Name
                : $"{entrada.Metadata.ClrType.Name} {id}";
        }
    }
}
