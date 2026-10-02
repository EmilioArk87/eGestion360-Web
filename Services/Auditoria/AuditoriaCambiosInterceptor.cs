using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;

namespace eGestion360Web.Services.Auditoria
{
    /// <summary>
    /// Escribe en bitacora_cambios, dentro del mismo guardado, el historial por campo de las entidades de
    /// la persona maestra: Persona, PersonaDocumento, PersonaEmpresa, Empleado y, cuando está enlazado a una
    /// persona, Cliente (decisión D9). Un cliente sin ficha de persona (un «consumidor final») no se audita.
    ///
    ///   * Modificación: una fila por cada campo cuyo valor cambió (valor anterior y valor nuevo).
    ///   * Alta y baja física: una fila con campo nulo y la foto del registro en JSON.
    ///   * No se registran token_concurrencia, fecha_modificacion ni modificado_por (son consecuencia del
    ///     cambio y no el cambio), ni las claves ni las columnas calculadas. Las altas tampoco repiten
    ///     creado_por ni fecha_creacion: ya van en las columnas usuario y fecha_hora de la fila.
    ///   * El número de documento se guarda enmascarado (solo los 4 últimos dígitos): la bitácora no se
    ///     puede borrar y no debe conservar el documento completo.
    ///
    /// Las modificaciones y bajas se agregan al mismo SaveChanges. Las altas necesitan el id que genera la
    /// base, así que se registran justo después, en un segundo guardado dentro de la misma transacción
    /// (la interceptora abre una si no hay ninguna): o se guarda el cambio con su registro, o ninguno.
    ///
    /// Límite: solo ve lo que se guarda con EF Core. Un script SQL o una edición directa no pasan por aquí;
    /// por eso cada script que toca estos datos escribe sus propias filas (origen 'script:NNN').
    /// </summary>
    public sealed class AuditoriaCambiosInterceptor : SaveChangesInterceptor
    {
        private const int LargoMaximoValor = 2000;
        private const int LargoMaximoUsuario = 100;

        private static readonly Dictionary<Type, string> Entidades = new()
        {
            [typeof(Persona)] = "personas",
            [typeof(PersonaDocumento)] = "persona_documentos",
            [typeof(PersonaEmpresa)] = "persona_empresa",
            [typeof(Empleado)] = "empleados",
            [typeof(Cliente)] = "clientes"
        };

        private static readonly HashSet<string> PropiedadesIgnoradas = new()
        {
            "TokenConcurrencia", "FechaModificacion", "ModificadoPor"
        };

        private static readonly HashSet<string> PropiedadesIgnoradasEnAltas = new()
        {
            "CreadoPor", "FechaCreacion"
        };

        private static readonly JsonSerializerOptions OpcionesJson = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IContextoAuditoria _contexto;
        private readonly ConditionalWeakTable<DbContext, Estado> _estados = new();

        public AuditoriaCambiosInterceptor(IContextoAuditoria contexto) => _contexto = contexto;

        /// <summary>Lo que hay que recordar entre el antes y el después de un guardado.</summary>
        private sealed class Estado
        {
            public Guid IdTransaccion { get; } = Guid.NewGuid();
            public List<object> Altas { get; } = new();
            public IDbContextTransaction? Transaccion { get; set; }

            /// <summary>Verdadero mientras se hace el segundo guardado, para no auditar la propia bitácora.</summary>
            public bool Guardando { get; set; }
        }

        // ── Antes de guardar ────────────────────────────────────────────────

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context != null)
                PrepararAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null)
                await PrepararAsync(eventData.Context, cancellationToken);
            return result;
        }

        private async Task PrepararAsync(DbContext contexto, CancellationToken ct)
        {
            if (_estados.TryGetValue(contexto, out var anterior) && anterior.Guardando) return;

            var entradas = contexto.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                            && Auditable(e))
                .ToList();

            _estados.Remove(contexto);
            if (entradas.Count == 0) return;

            var estado = new Estado();
            _estados.Add(contexto, estado);

            var filas = new List<BitacoraCambio>();
            foreach (var entrada in entradas)
            {
                switch (entrada.State)
                {
                    case EntityState.Added:
                        estado.Altas.Add(entrada.Entity);
                        break;
                    case EntityState.Modified:
                        filas.AddRange(await CambiosAsync(contexto, entrada, estado.IdTransaccion, ct));
                        break;
                    case EntityState.Deleted:
                        filas.Add(await BajaAsync(contexto, entrada, estado.IdTransaccion, ct));
                        break;
                }
            }

            if (filas.Count > 0)
                contexto.Set<BitacoraCambio>().AddRange(filas);

            // Las altas se registran en un segundo guardado: deben ir en la misma transacción que el primero.
            if (estado.Altas.Count > 0 && contexto.Database.CurrentTransaction == null)
                estado.Transaccion = await contexto.Database.BeginTransactionAsync(ct);
        }

        /// <summary>
        /// Si la entidad es de las que se auditan. Un cliente solo cuenta si está enlazado a una persona (o lo estaba
        /// antes del cambio, para que quede constancia de que se desenlazó).
        /// </summary>
        private static bool Auditable(EntityEntry entrada)
        {
            if (!Entidades.ContainsKey(entrada.Entity.GetType())) return false;
            if (entrada.Entity is not Cliente cliente) return true;

            return cliente.IdPersonaEmpresa != null
                   || entrada.Property(nameof(Cliente.IdPersonaEmpresa)).OriginalValue != null;
        }

        // ── Después de guardar ──────────────────────────────────────────────

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (eventData.Context != null)
                RegistrarAltasAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null)
                await RegistrarAltasAsync(eventData.Context, cancellationToken);
            return result;
        }

        private async Task RegistrarAltasAsync(DbContext contexto, CancellationToken ct)
        {
            if (!_estados.TryGetValue(contexto, out var estado) || estado.Guardando) return;

            try
            {
                if (estado.Altas.Count > 0)
                {
                    var filas = new List<BitacoraCambio>();
                    foreach (var entidad in estado.Altas)
                        filas.Add(await AltaAsync(contexto, entidad, estado.IdTransaccion, ct));

                    contexto.Set<BitacoraCambio>().AddRange(filas);

                    estado.Guardando = true;
                    await contexto.SaveChangesAsync(ct);
                    estado.Guardando = false;

                    if (estado.Transaccion != null)
                        await estado.Transaccion.CommitAsync(ct);
                }
            }
            catch
            {
                if (estado.Transaccion != null)
                    await estado.Transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                estado.Transaccion?.Dispose();
                _estados.Remove(contexto);
            }
        }

        // ── Si el guardado falla o se cancela ───────────────────────────────

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            if (eventData.Context != null)
                DescartarAsync(eventData.Context).GetAwaiter().GetResult();
        }

        public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null)
                await DescartarAsync(eventData.Context);
        }

        public override async Task SaveChangesCanceledAsync(
            DbContextEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null)
                await DescartarAsync(eventData.Context);
        }

        private async Task DescartarAsync(DbContext contexto)
        {
            if (!_estados.TryGetValue(contexto, out var estado)) return;

            if (estado.Transaccion != null)
            {
                await estado.Transaccion.RollbackAsync(CancellationToken.None);
                estado.Transaccion.Dispose();
            }
            _estados.Remove(contexto);
        }

        // ── Armado de las filas ─────────────────────────────────────────────

        private async Task<List<BitacoraCambio>> CambiosAsync(DbContext contexto, EntityEntry entrada, Guid idTransaccion, CancellationToken ct)
        {
            var filas = new List<BitacoraCambio>();
            var (idEmpresa, idPersona, idRegistro) = await ReferenciasAsync(contexto, entrada, ct);

            foreach (var propiedad in entrada.Properties)
            {
                if (!propiedad.IsModified || Ignorar(propiedad.Metadata, enAlta: false)) continue;

                var antes = Texto(propiedad.Metadata, propiedad.OriginalValue);
                var despues = Texto(propiedad.Metadata, propiedad.CurrentValue);
                if (antes == despues) continue;

                filas.Add(Fila(idTransaccion, idEmpresa, idPersona, entrada, idRegistro, OperacionesBitacora.Update,
                    propiedad.Metadata.GetColumnName(), Enmascarar(propiedad.Metadata, antes), Enmascarar(propiedad.Metadata, despues)));
            }

            return filas;
        }

        private async Task<BitacoraCambio> BajaAsync(DbContext contexto, EntityEntry entrada, Guid idTransaccion, CancellationToken ct)
        {
            var (idEmpresa, idPersona, idRegistro) = await ReferenciasAsync(contexto, entrada, ct);
            return Fila(idTransaccion, idEmpresa, idPersona, entrada, idRegistro, OperacionesBitacora.Delete,
                campo: null, anterior: Foto(entrada, original: true), nuevo: null);
        }

        private async Task<BitacoraCambio> AltaAsync(DbContext contexto, object entidad, Guid idTransaccion, CancellationToken ct)
        {
            var entrada = contexto.Entry(entidad);
            var (idEmpresa, idPersona, idRegistro) = await ReferenciasAsync(contexto, entrada, ct);
            return Fila(idTransaccion, idEmpresa, idPersona, entrada, idRegistro, OperacionesBitacora.Insert,
                campo: null, anterior: null, nuevo: Foto(entrada, original: false));
        }

        private BitacoraCambio Fila(
            Guid idTransaccion, int? idEmpresa, int? idPersona, EntityEntry entrada, long idRegistro,
            string operacion, string? campo, string? anterior, string? nuevo) => new()
        {
            IdTransaccion = idTransaccion,
            IdEmpresa = idEmpresa,
            IdPersona = idPersona,
            Entidad = Entidades[entrada.Entity.GetType()],
            IdRegistro = idRegistro,
            Operacion = operacion,
            Campo = campo,
            ValorAnterior = Cortar(anterior, LargoMaximoValor),
            ValorNuevo = Cortar(nuevo, LargoMaximoValor),
            FechaHora = DateTime.UtcNow,
            Usuario = Cortar(_contexto.Usuario, LargoMaximoUsuario) ?? "system",
            Origen = _contexto.Origen
        };

        /// <summary>Empresa, persona y número de registro de lo que se audita.</summary>
        private async Task<(int? IdEmpresa, int? IdPersona, long IdRegistro)> ReferenciasAsync(
            DbContext contexto, EntityEntry entrada, CancellationToken ct)
        {
            var clave = entrada.Metadata.FindPrimaryKey()!.Properties[0];
            var idRegistro = Convert.ToInt64(entrada.Property(clave.Name).CurrentValue, CultureInfo.InvariantCulture);

            switch (entrada.Entity)
            {
                case Persona p:
                    return (_contexto.IdEmpresa ?? (p.IdEmpresa > 0 ? p.IdEmpresa : null), p.IdPersona, idRegistro);

                case PersonaDocumento d:
                    return (_contexto.IdEmpresa, d.IdPersona, idRegistro);

                case PersonaEmpresa v:
                    return (v.IdEmpresa, v.IdPersona, idRegistro);

                case Empleado e:
                    var idPersona = e.Vinculo?.IdPersona > 0
                        ? e.Vinculo.IdPersona
                        : await contexto.Set<PersonaEmpresa>().AsNoTracking()
                            .Where(v => v.IdPersonaEmpresa == e.IdPersonaEmpresa)
                            .Select(v => (int?)v.IdPersona)
                            .FirstOrDefaultAsync(ct);
                    return (e.IdEmpresa, idPersona, idRegistro);

                case Cliente c:
                    var idVinculo = c.IdPersonaEmpresa ?? (int?)entrada.Property(nameof(Cliente.IdPersonaEmpresa)).OriginalValue;
                    var idPersonaDelCliente = c.Vinculo?.IdPersona > 0
                        ? c.Vinculo.IdPersona
                        : idVinculo == null
                            ? null
                            : await contexto.Set<PersonaEmpresa>().AsNoTracking()
                                .Where(v => v.IdPersonaEmpresa == idVinculo)
                                .Select(v => (int?)v.IdPersona)
                                .FirstOrDefaultAsync(ct);
                    return (c.IdEmpresa, idPersonaDelCliente, idRegistro);

                default:
                    return (_contexto.IdEmpresa, null, idRegistro);
            }
        }

        // ── Valores ─────────────────────────────────────────────────────────

        /// <summary>Claves, columnas generadas por la base, concurrencia y las columnas de auditoría que no se registran.</summary>
        private static bool Ignorar(IReadOnlyProperty propiedad, bool enAlta) =>
            PropiedadesIgnoradas.Contains(propiedad.Name)
            || (enAlta && PropiedadesIgnoradasEnAltas.Contains(propiedad.Name))
            || propiedad.IsPrimaryKey()
            || propiedad.IsConcurrencyToken
            || propiedad.ValueGenerated is ValueGenerated.OnAddOrUpdate or ValueGenerated.OnUpdate;

        /// <summary>La foto del registro: un objeto JSON con las columnas que tienen valor.</summary>
        private static string Foto(EntityEntry entrada, bool original)
        {
            var datos = new Dictionary<string, object?>();
            foreach (var propiedad in entrada.Properties)
            {
                if (Ignorar(propiedad.Metadata, enAlta: true)) continue;

                var valor = original ? propiedad.OriginalValue : propiedad.CurrentValue;
                if (valor == null) continue;

                datos[propiedad.Metadata.GetColumnName()] = ValorParaJson(propiedad.Metadata, valor);
            }
            return JsonSerializer.Serialize(datos, OpcionesJson);
        }

        private static object ValorParaJson(IReadOnlyProperty propiedad, object valor) => valor switch
        {
            string s => Enmascarar(propiedad, s)!,
            bool or int or long or short or byte or decimal => valor,
            _ => Texto(propiedad, valor)!
        };

        private static string? Texto(IReadOnlyProperty propiedad, object? valor) => valor switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime t => t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => valor.ToString()
        };

        /// <summary>Los números de documento se guardan enmascarados: solo se ven los 4 últimos dígitos.</summary>
        private static string? Enmascarar(IReadOnlyProperty propiedad, string? texto)
        {
            if (texto == null) return null;

            var esDocumento = (propiedad.DeclaringType.ClrType == typeof(PersonaDocumento) && propiedad.Name == nameof(PersonaDocumento.Numero))
                              || (propiedad.DeclaringType.ClrType == typeof(Persona) && propiedad.Name == nameof(Persona.Documento));

            // Un código corto de empleado (por ejemplo "31234") no es un documento de identidad: se deja tal cual.
            return esDocumento && texto.Length > 6
                ? new string('*', texto.Length - 4) + texto[^4..]
                : texto;
        }

        private static string? Cortar(string? texto, int largo) =>
            texto != null && texto.Length > largo ? texto[..(largo - 1)] + "…" : texto;
    }
}
