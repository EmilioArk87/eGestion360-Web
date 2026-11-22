using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Services.Tenancy;

namespace eGestion360Web.Services.Auditoria
{
    /// <summary>En qué tabla de auditoría se guarda un tipo de evento (D70, ADR-030).</summary>
    public enum DestinoDeEvento
    {
        /// <summary><c>saas.auditoria_plataforma</c>: lo de las cuentas y del operador.</summary>
        Plataforma,

        /// <summary><c>dbo.auditoria_seguridad</c>: lo que ocurre dentro de un tenant.</summary>
        Tenant,

        /// <summary>Las dos: una acción del operador sobre un tenant, que el tenant también ve (soporte).</summary>
        Ambos,

        /// <summary>Del tenant si el evento trae uno; si no, de la plataforma (por ejemplo, el operador entra sin empresa).</summary>
        SegunTenant
    }

    /// <summary>
    /// Catálogo de eventos de seguridad, con la forma <c>grupo.accion</c> (D71). F1.8a registra los de entrada; F1.8b agrega los de
    /// administración y soporte. Un tipo que no está aquí no se guarda: así el catálogo y la tabla no se separan.
    /// </summary>
    public static class TiposDeEvento
    {
        // Cuenta y entrada (plataforma)
        public const string SesionEntrada = "sesion.entrada";
        public const string SesionSalida = "sesion.salida";
        public const string SesionBloqueo = "sesion.bloqueo";
        public const string MfaVerificada = "mfa.verificada";
        public const string MfaAlta = "mfa.alta";
        public const string MfaRecuperacion = "mfa.recuperacion";
        public const string MfaDesactivada = "mfa.desactivada";
        public const string ClaveCambiada = "clave.cambiada";
        public const string ClaveRestablecida = "clave.restablecida";
        public const string InvitacionAceptada = "invitacion.aceptada";

        // Contexto (tenant, o plataforma si el operador entra sin empresa)
        public const string ContextoElegido = "contexto.elegido";

        // Administración de la empresa (F1.8b): los cambios de usuarios, roles, permisos, módulos, accesos y empresas los deja el interceptor
        // de la bitácora en el mismo guardado; las invitaciones y los accesos denegados, el servicio.
        public const string UsuarioCreado = "usuario.creado";
        public const string UsuarioEditado = "usuario.editado";
        public const string UsuarioEliminado = "usuario.eliminado";
        public const string RolCreado = "rol.creado";
        public const string RolEditado = "rol.editado";
        public const string RolEliminado = "rol.eliminado";
        public const string RolPermisos = "rol.permisos";
        public const string ModuloCambiado = "modulo.cambiado";
        public const string AccesoCambiado = "acceso.cambiado";
        public const string EmpresaCreada = "empresa.creada";
        public const string EmpresaEditada = "empresa.editada";
        public const string EmpresaEliminada = "empresa.eliminada";
        public const string InvitacionEnviada = "invitacion.enviada";
        public const string InvitacionAnulada = "invitacion.anulada";
        public const string AccesoDenegado = "acceso.denegado";

        // Soporte del operador en un tenant: la fila va a las dos tablas (D70).
        public const string SoporteEntrada = "soporte.entrada";
        public const string SoporteSalida = "soporte.salida";

        // Acciones del operador sobre la plataforma
        public const string CorreoConfigurado = "correo.configurado";
        public const string TasasConfigurada = "tasas.configurada";
        public const string TasasEjecutada = "tasas.ejecutada";
        public const string TasasOficialAprobada = "tasas.oficial_aprobada";
        public const string TasasOficialRechazada = "tasas.oficial_rechazada";
        public const string ColaReintento = "cola.reintento";
        public const string ColaProcesadoManual = "cola.procesado_manual";

        // Consulta de la propia auditoría (F1.8c): cada exportación a Excel o impresión deja su fila. Ver la pantalla no deja nada.
        public const string AuditoriaExportada = "auditoria.exportada";
        public const string AuditoriaPlataformaExportada = "auditoria.plataforma_exportada";

        private static readonly IReadOnlyDictionary<string, DestinoDeEvento> Destinos = new Dictionary<string, DestinoDeEvento>(StringComparer.Ordinal)
        {
            [SesionEntrada] = DestinoDeEvento.Plataforma,
            [SesionSalida] = DestinoDeEvento.Plataforma,
            [SesionBloqueo] = DestinoDeEvento.Plataforma,
            [MfaVerificada] = DestinoDeEvento.Plataforma,
            [MfaAlta] = DestinoDeEvento.Plataforma,
            [MfaRecuperacion] = DestinoDeEvento.Plataforma,
            [MfaDesactivada] = DestinoDeEvento.Plataforma,
            [ClaveCambiada] = DestinoDeEvento.Plataforma,
            [ClaveRestablecida] = DestinoDeEvento.Plataforma,
            [InvitacionAceptada] = DestinoDeEvento.Plataforma,
            [ContextoElegido] = DestinoDeEvento.SegunTenant,

            [UsuarioCreado] = DestinoDeEvento.Tenant,
            [UsuarioEditado] = DestinoDeEvento.Tenant,
            [UsuarioEliminado] = DestinoDeEvento.Tenant,
            [RolCreado] = DestinoDeEvento.Tenant,
            [RolEditado] = DestinoDeEvento.Tenant,
            [RolEliminado] = DestinoDeEvento.Tenant,
            [RolPermisos] = DestinoDeEvento.Tenant,
            [ModuloCambiado] = DestinoDeEvento.Tenant,
            [AccesoCambiado] = DestinoDeEvento.Tenant,
            [EmpresaCreada] = DestinoDeEvento.Tenant,
            [EmpresaEditada] = DestinoDeEvento.Tenant,
            [EmpresaEliminada] = DestinoDeEvento.Tenant,
            [InvitacionEnviada] = DestinoDeEvento.Tenant,
            [InvitacionAnulada] = DestinoDeEvento.Tenant,
            [AccesoDenegado] = DestinoDeEvento.Tenant,

            [SoporteEntrada] = DestinoDeEvento.Ambos,
            [SoporteSalida] = DestinoDeEvento.Ambos,

            [CorreoConfigurado] = DestinoDeEvento.Plataforma,
            [TasasConfigurada] = DestinoDeEvento.Plataforma,
            [TasasEjecutada] = DestinoDeEvento.Plataforma,
            [TasasOficialAprobada] = DestinoDeEvento.Plataforma,
            [TasasOficialRechazada] = DestinoDeEvento.Plataforma,
            [ColaReintento] = DestinoDeEvento.Plataforma,
            [ColaProcesadoManual] = DestinoDeEvento.Plataforma,

            [AuditoriaExportada] = DestinoDeEvento.Tenant,
            [AuditoriaPlataformaExportada] = DestinoDeEvento.Plataforma,
        };

        public static IReadOnlyCollection<string> Todos => Destinos.Keys.ToArray();

        public static bool Existe(string? tipo) => tipo != null && Destinos.ContainsKey(tipo);

        public static DestinoDeEvento DestinoDe(string tipo) => Destinos[tipo];
    }

    /// <summary>
    /// Un evento de seguridad por registrar. Lo que no se indica sale de la petición: la cuenta, su nombre y si es operador, de la
    /// cookie; el tenant y la empresa, de los de la petición. Cuando el evento ocurre antes de que la cookie lleve el contexto nuevo
    /// (la entrada, el cambio de sitio), quien lo registra pasa su tenant y su empresa.
    /// </summary>
    public sealed record EventoDeSeguridad(string Tipo)
    {
        public string Resultado { get; init; } = ResultadosDeEvento.Ok;
        public int? IdCuenta { get; init; }

        /// <summary>Id de <c>dbo.Users</c> cuando quien registra solo conoce al usuario anterior a F1.5 (restablecer la clave sin sesión); el servicio busca su cuenta.</summary>
        public int? IdUserLegado { get; init; }

        public string? Usuario { get; init; }
        public bool? EsOperador { get; init; }
        public int? IdTenant { get; init; }
        public int? IdEmpresa { get; init; }
        public string? ObjetoTipo { get; init; }
        public string? ObjetoId { get; init; }

        /// <summary>Datos breves del evento (un objeto anónimo o un diccionario). Nunca claves, códigos, tokens ni documentos completos.</summary>
        public object? Detalle { get; init; }

        /// <summary>
        /// El evento se agrega: una fila por dirección y por hora (los intentos con una cuenta que no existe, D71). Los omitidos se
        /// cuentan y se anotan en el detalle de la siguiente fila.
        /// </summary>
        public bool Agregable { get; init; }

        /// <summary>Qué se agrega junto con la dirección; por omisión, el tipo del evento. Un motivo distinto cuenta aparte.</summary>
        public string? ClaveDeAgregado { get; init; }
    }

    public interface IAuditoriaDeSeguridad
    {
        /// <summary>
        /// Guarda el evento en la tabla que le toca. Es de mejor esfuerzo (D73): si no se puede guardar, lo anota en el log y sigue; la
        /// persona no se queda sin entrar por un problema de la auditoría.
        /// </summary>
        Task RegistrarAsync(EventoDeSeguridad evento, CancellationToken ct = default);

        /// <summary>
        /// Como <see cref="RegistrarAsync"/>, pero para lo que no debe hacerse sin dejar rastro (entrar a un tenant en soporte, D73): si el
        /// evento no se puede guardar, lanza <see cref="AuditoriaNoDisponibleException"/> y quien llama no hace la acción. Un evento que va
        /// a las dos tablas se guarda en un solo guardado: o quedan las dos filas o ninguna.
        /// </summary>
        Task RegistrarEstrictoAsync(EventoDeSeguridad evento, CancellationToken ct = default);
    }

    public static class AuditoriaDeSeguridadExtensiones
    {
        /// <summary>
        /// Una acción del personal de la plataforma sobre la plataforma (correo, tasas, cola de eventos): de mejor esfuerzo, ya hecha la acción.
        /// Sin servicio (una prueba de otra pieza) no hace nada.
        /// </summary>
        public static Task RegistrarAccionDeLaPlataformaAsync(this IAuditoriaDeSeguridad? auditoria, string tipo, string? objetoTipo = null,
            string? objetoId = null, object? detalle = null, string resultado = ResultadosDeEvento.Ok, CancellationToken ct = default)
            => auditoria == null
                ? Task.CompletedTask
                : auditoria.RegistrarAsync(new EventoDeSeguridad(tipo) { Resultado = resultado, ObjetoTipo = objetoTipo, ObjetoId = objetoId, Detalle = detalle }, ct);
    }

    /// <summary>La auditoría estricta no pudo guardar el evento: la acción que lo exigía no se hace.</summary>
    public sealed class AuditoriaNoDisponibleException : Exception
    {
        public AuditoriaNoDisponibleException(string mensaje, Exception? causa = null) : base(mensaje, causa) { }
    }

    /// <summary>
    /// Escribe los eventos de seguridad (ADR-030, F1.8a). Cada evento se guarda con su propio ámbito de servicios, su propio
    /// <see cref="ApplicationDbContext"/> y su propia conexión, igual que el validador de identidad: así RLS comprueba el tenant
    /// del evento (que puede ser otro distinto del de la petición), un fallo de la auditoría no deja nada a medias en el contexto de
    /// la petición y la auditoría no guarda por accidente cambios pendientes que no le tocan.
    /// </summary>
    public sealed class AuditoriaDeSeguridad : IAuditoriaDeSeguridad
    {
        private const int LargoMaximoDetalle = 2000;
        private const int LargoMaximoAgente = 200;
        private const int LargoMaximoIp = 45;
        private const int LargoMaximoCorrelacion = 64;

        /// <summary>Como mucho, las filas agregadas que se guardan por hora en toda la aplicación (un ataque desde muchas direcciones no llena la tabla).</summary>
        public const int TopeDeAgregadosPorHora = 200;

        // Nombres de campo que nunca se guardan en el detalle (D72), sin distinguir mayúsculas.
        private static readonly string[] CamposProhibidos =
        {
            "password", "contrasena", "clave", "hash", "token", "secreto", "secret", "codigo", "cadena", "connectionstring", "documento", "numero"
        };

        private static readonly JsonSerializerOptions OpcionesJson = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly IServiceScopeFactory _ambitos;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<AuditoriaDeSeguridad> _log;
        private readonly TimeProvider _tiempo;

        private readonly object _candado = new();
        private readonly Dictionary<string, (long Hora, int Omitidos)> _agregados = new(StringComparer.Ordinal);
        private long _horaDelTope;
        private int _agregadosEnLaHora;

        public AuditoriaDeSeguridad(IServiceScopeFactory ambitos, IHttpContextAccessor http, ILogger<AuditoriaDeSeguridad> log, TimeProvider? tiempo = null)
        {
            _ambitos = ambitos;
            _http = http;
            _log = log;
            _tiempo = tiempo ?? TimeProvider.System;
        }

        public async Task RegistrarAsync(EventoDeSeguridad evento, CancellationToken ct = default)
        {
            try
            {
                await GuardarAsync(evento, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Mejor esfuerzo (D73): la auditoría que falla no deja a nadie fuera; se avisa en el log para revisarlo.
                _log.LogError(ex, "Auditoría: no se pudo registrar el evento {Tipo} ({Resultado}).", evento.Tipo, evento.Resultado);
            }
        }

        public async Task RegistrarEstrictoAsync(EventoDeSeguridad evento, CancellationToken ct = default)
        {
            try
            {
                await GuardarAsync(evento, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Auditoría: no se pudo registrar el evento {Tipo} ({Resultado}) y la acción no se hace.", evento.Tipo, evento.Resultado);
                throw new AuditoriaNoDisponibleException($"No se pudo dejar el rastro de auditoría del evento «{evento.Tipo}».", ex);
            }
        }

        private async Task GuardarAsync(EventoDeSeguridad evento, CancellationToken ct)
        {
            if (!TiposDeEvento.Existe(evento.Tipo))
                throw new ArgumentException($"El tipo de evento «{evento.Tipo}» no está en el catálogo de auditoría.", nameof(evento));
            if (evento.Resultado is not (ResultadosDeEvento.Ok or ResultadosDeEvento.Fallo or ResultadosDeEvento.Denegado))
                throw new ArgumentException($"El resultado «{evento.Resultado}» no es válido.", nameof(evento));

            var ahora = _tiempo.GetUtcNow().UtcDateTime;
            var http = _http.HttpContext;
            var datos = Normalizar(evento, http, ahora, out var omitidos);
            if (datos == null) return;   // agregado: ya hay una fila de esta dirección en esta hora

            // Un evento de la entrada (segundo factor, bloqueo) ocurre antes de que la cookie lleve el nombre, y uno sin sesión (restablecer
            // la clave) solo conoce al usuario anterior a F1.5: el nombre y la cuenta se leen de la base.
            if ((datos.Usuario == null && datos.IdCuenta is > 0) || (datos.IdCuenta == null && evento.IdUserLegado is > 0))
            {
                var cuenta = await LeerCuentaAsync(datos.IdCuenta, evento.IdUserLegado, ct);
                if (cuenta != null)
                    datos = datos with
                    {
                        IdCuenta = datos.IdCuenta ?? cuenta.Value.IdCuenta,
                        Usuario = datos.Usuario ?? Cortar(cuenta.Value.Nombre, 100),
                        // En la entrada la cookie todavía no existe: si el evento no dice si es del personal de la plataforma, lo dice su cuenta.
                        EsOperador = evento.EsOperador ?? (datos.EsOperador || cuenta.Value.EsOperador),
                    };
            }

            var detalle = SerializarDetalle(evento.Detalle, omitidos);
            var destino = TiposDeEvento.DestinoDe(evento.Tipo);
            var enTenant = destino is DestinoDeEvento.Tenant or DestinoDeEvento.Ambos
                           || (destino == DestinoDeEvento.SegunTenant && datos.IdTenant is > 0);
            var enPlataforma = destino is DestinoDeEvento.Plataforma or DestinoDeEvento.Ambos
                               || (destino == DestinoDeEvento.SegunTenant && !enTenant)
                               || (destino == DestinoDeEvento.Tenant && datos.IdTenant is not > 0);   // sin tenant no se pierde: queda en la plataforma

            var iraAlTenant = enTenant && datos.IdTenant is > 0;
            if (iraAlTenant && enPlataforma) await EscribirEnAmbasAsync(datos, detalle, ct);   // un solo guardado: las dos filas o ninguna
            else if (iraAlTenant) await EscribirEnTenantAsync(datos, detalle, ct);
            else if (enPlataforma) await EscribirEnPlataformaAsync(datos, detalle, ct);
        }

        // ── Lo que se guarda ──────────────────────────────────────────────────

        private sealed record Datos(string Tipo, string Resultado, int? IdCuenta, string? Usuario, bool EsOperador, int? IdTenant, int? IdEmpresa,
            string? ObjetoTipo, string? ObjetoId, string? Ip, string? Agente, string Correlacion, DateTime Fecha);

        private Datos? Normalizar(EventoDeSeguridad e, HttpContext? http, DateTime ahora, out int omitidos)
        {
            omitidos = 0;

            var ip = http?.Connection.RemoteIpAddress is { } direccion
                ? (direccion.IsIPv4MappedToIPv6 ? direccion.MapToIPv4() : direccion).ToString()
                : null;

            if (e.Agregable && !PermiteAgregado((e.ClaveDeAgregado ?? e.Tipo) + "|" + (ip ?? "sin-direccion"), ahora, out omitidos)) return null;

            var agente = http?.Request.Headers.UserAgent.ToString();
            var correlacion = http?.TraceIdentifier;
            var idCuenta = e.IdCuenta ?? (http != null ? AuthHelper.GetCuentaId(http) : null);
            var usuario = e.Usuario ?? (http != null && AuthHelper.HayCuenta(http) ? AuthHelper.GetNombreUsuario(http) : null);
            var esOperador = e.EsOperador ?? (http != null && AuthHelper.EsOperador(http));
            var idTenant = e.IdTenant ?? LeerDeLaPeticion(http, TenantMiddleware.ClaveIdTenant);
            var idEmpresa = e.IdEmpresa ?? LeerDeLaPeticion(http, TenantMiddleware.ClaveIdEmpresa);

            return new Datos(e.Tipo, e.Resultado, idCuenta, Cortar(usuario, 100), esOperador, idTenant, idEmpresa,
                Cortar(e.ObjetoTipo, 40), Cortar(e.ObjetoId, 60), Cortar(ip, LargoMaximoIp), Cortar(agente, LargoMaximoAgente),
                Cortar(string.IsNullOrWhiteSpace(correlacion) ? Guid.NewGuid().ToString("N") : correlacion, LargoMaximoCorrelacion)!, ahora);
        }

        private static int? LeerDeLaPeticion(HttpContext? http, string clave)
            => http?.Items.TryGetValue(clave, out var valor) == true && valor is int numero ? numero : null;

        private static string? Cortar(string? texto, int largo)
            => string.IsNullOrEmpty(texto) ? null : texto.Length <= largo ? texto : texto[..largo];

        /// <summary>El detalle como JSON breve; si trae un campo que nunca se guarda, el evento se rechaza (y queda en el log).</summary>
        internal static string? SerializarDetalle(object? detalle, int omitidos)
        {
            var campos = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (detalle != null)
            {
                var elemento = JsonSerializer.SerializeToElement(detalle, OpcionesJson);
                if (elemento.ValueKind == JsonValueKind.Object)
                {
                    foreach (var campo in elemento.EnumerateObject())
                    {
                        if (CamposProhibidos.Contains(campo.Name, StringComparer.OrdinalIgnoreCase))
                            throw new ArgumentException($"El campo «{campo.Name}» no se guarda en la auditoría (D72).", nameof(detalle));
                        campos[campo.Name] = campo.Value.Clone();
                    }
                }
                else
                {
                    campos["valor"] = elemento.Clone();
                }
            }
            if (omitidos > 0) campos["omitidos_antes"] = omitidos;
            if (campos.Count == 0) return null;

            var json = JsonSerializer.Serialize(campos, OpcionesJson);
            return json.Length <= LargoMaximoDetalle ? json : "{\"truncado\":true}";
        }

        // ── Agregación (D71) ──────────────────────────────────────────────────

        /// <summary>Una fila por dirección y por hora, con un tope por hora para toda la aplicación; lo omitido se cuenta para la siguiente fila.</summary>
        private bool PermiteAgregado(string direccion, DateTime ahora, out int omitidos)
        {
            var hora = ahora.Ticks / TimeSpan.TicksPerHour;
            omitidos = 0;
            lock (_candado)
            {
                if (_horaDelTope != hora)
                {
                    _horaDelTope = hora;
                    _agregadosEnLaHora = 0;
                    foreach (var clave in _agregados.Where(a => a.Value.Hora < hora - 1).Select(a => a.Key).ToList()) _agregados.Remove(clave);
                }

                if (_agregados.TryGetValue(direccion, out var previo) && previo.Hora == hora)
                {
                    _agregados[direccion] = (hora, previo.Omitidos + 1);
                    return false;
                }
                if (_agregadosEnLaHora >= TopeDeAgregadosPorHora)
                {
                    _agregados[direccion] = (hora, (_agregados.TryGetValue(direccion, out var otro) ? otro.Omitidos : 0) + 1);
                    return false;
                }

                omitidos = _agregados.TryGetValue(direccion, out var anterior) ? anterior.Omitidos : 0;
                _agregados[direccion] = (hora, 0);
                _agregadosEnLaHora++;
                return true;
            }
        }

        private async Task<(int IdCuenta, string? Nombre, bool EsOperador)?> LeerCuentaAsync(int? idCuenta, int? idUserLegado, CancellationToken ct)
        {
            await using var ambito = _ambitos.CreateAsyncScope();
            var servicios = ambito.ServiceProvider;
            servicios.GetRequiredService<TenantContext>().FijarPlataforma();
            var db = servicios.GetRequiredService<ApplicationDbContext>();

            var consulta = db.Cuentas.AsNoTracking();
            consulta = idCuenta is > 0 ? consulta.Where(c => c.IdCuenta == idCuenta) : consulta.Where(c => c.IdUserLegado == idUserLegado);
            var fila = await consulta.Select(c => new { c.IdCuenta, Nombre = c.NombreUsuario ?? c.Correo, c.EsOperador }).FirstOrDefaultAsync(ct);
            return fila == null ? null : (fila.IdCuenta, fila.Nombre, fila.EsOperador);
        }

        // ── Escritura aparte ──────────────────────────────────────────────────

        private static AuditoriaSeguridad FilaDeTenant(Datos d, string? detalle) => new()
        {
            IdTenant = d.IdTenant!.Value,
            FechaHora = d.Fecha, Tipo = d.Tipo, Resultado = d.Resultado, IdCuenta = d.IdCuenta, Usuario = d.Usuario, EsOperador = d.EsOperador,
            IdEmpresa = d.IdEmpresa, ObjetoTipo = d.ObjetoTipo, ObjetoId = d.ObjetoId, Detalle = detalle, Ip = d.Ip, Agente = d.Agente,
            IdCorrelacion = d.Correlacion,
        };

        private static AuditoriaPlataforma FilaDePlataforma(Datos d, string? detalle) => new()
        {
            IdTenant = d.IdTenant,
            FechaHora = d.Fecha, Tipo = d.Tipo, Resultado = d.Resultado, IdCuenta = d.IdCuenta, Usuario = d.Usuario, EsOperador = d.EsOperador,
            IdEmpresa = d.IdEmpresa, ObjetoTipo = d.ObjetoTipo, ObjetoId = d.ObjetoId, Detalle = detalle, Ip = d.Ip, Agente = d.Agente,
            IdCorrelacion = d.Correlacion,
        };

        private async Task EscribirEnTenantAsync(Datos d, string? detalle, CancellationToken ct)
        {
            await using var ambito = _ambitos.CreateAsyncScope();
            var servicios = ambito.ServiceProvider;
            servicios.GetRequiredService<TenantContext>().Fijar(d.IdTenant, d.IdEmpresa);
            var db = servicios.GetRequiredService<ApplicationDbContext>();

            db.AuditoriaSeguridad.Add(FilaDeTenant(d, detalle));
            await db.SaveChangesAsync(ct);
        }

        private async Task EscribirEnPlataformaAsync(Datos d, string? detalle, CancellationToken ct)
        {
            await using var ambito = _ambitos.CreateAsyncScope();
            var servicios = ambito.ServiceProvider;
            servicios.GetRequiredService<TenantContext>().FijarPlataforma();
            var db = servicios.GetRequiredService<ApplicationDbContext>();

            db.AuditoriaPlataforma.Add(FilaDePlataforma(d, detalle));
            await db.SaveChangesAsync(ct);
        }

        /// <summary>Las dos filas de un evento «Ambos» en un solo guardado (una transacción): la de plataforma no lleva RLS, así que cabe en el contexto del tenant.</summary>
        private async Task EscribirEnAmbasAsync(Datos d, string? detalle, CancellationToken ct)
        {
            await using var ambito = _ambitos.CreateAsyncScope();
            var servicios = ambito.ServiceProvider;
            servicios.GetRequiredService<TenantContext>().Fijar(d.IdTenant, d.IdEmpresa);
            var db = servicios.GetRequiredService<ApplicationDbContext>();

            db.AuditoriaSeguridad.Add(FilaDeTenant(d, detalle));
            db.AuditoriaPlataforma.Add(FilaDePlataforma(d, detalle));
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Enmascara lo que identifica a alguien cuando la cuenta no existe: la auditoría no guarda el correo ni el usuario completos de un desconocido.</summary>
    public static class EnmascaradoDeAuditoria
    {
        /// <summary>«maria.lopez@empresa.hn» → «ma***@e***.hn»; «admin» → «ad***». Nunca devuelve el texto completo.</summary>
        public static string? Identificador(string? texto)
        {
            var t = texto?.Trim();
            if (string.IsNullOrEmpty(t)) return null;

            var arroba = t.IndexOf('@');
            if (arroba > 0 && arroba < t.Length - 1)
            {
                var dominio = t[(arroba + 1)..];
                var punto = dominio.LastIndexOf('.');
                var cola = punto > 0 ? dominio[punto..] : string.Empty;
                return Inicio(t[..arroba], 2) + "@" + Inicio(dominio, 1) + cola;
            }
            return Inicio(t, 2);
        }

        private static string Inicio(string texto, int largo) => (texto.Length <= largo ? texto[..1] : texto[..largo]) + "***";
    }
}
