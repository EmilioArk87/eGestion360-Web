using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;

namespace eGestion360Web.Services.Personas
{
    public sealed class VinculoService : IVinculoService
    {
        /// <summary>Se usa siempre que no coincide la verificación: no distingue si el documento existe o no (decisión D8).</summary>
        private const string MensajeNoVerificado =
            "No se pudo registrar al cliente con esos datos. Verifica el documento, el primer nombre y el primer apellido.";

        private const string MensajeNoGuardado = "No se pudo guardar al cliente. Revisa los datos e intenta de nuevo.";

        private const string MensajeConcurrencia = "Otro usuario modificó a este cliente. Recarga la pantalla y vuelve a intentarlo.";

        private const decimal LimiteCreditoMaximo = 9_999_999_999_999_999.99m;

        private static readonly Regex FormatoCodigo = new(
            "^[A-Za-z0-9._-]{1,30}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static readonly Regex FormatoIdentificadorFiscal = new(
            "^[A-Z0-9]{1,50}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private readonly ApplicationDbContext _db;
        private readonly IPersonaValidacionService _validacion;
        private readonly PersonaValidacionOptions _opt;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<VinculoService> _log;

        public VinculoService(
            ApplicationDbContext db, IPersonaValidacionService validacion, IOptions<PersonaValidacionOptions> opciones,
            TimeProvider tiempo, ILogger<VinculoService> log)
        {
            _db = db;
            _validacion = validacion;
            _opt = opciones.Value;
            _tiempo = tiempo;
            _log = log;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;

        /// <summary>Hoy en Honduras (UTC-6).</summary>
        private DateOnly Hoy() => DateOnly.FromDateTime(Ahora().AddHours(-6));

        // ──────────────────────────────────────────────────────────────────
        //  REGISTRAR CLIENTE NATURAL
        // ──────────────────────────────────────────────────────────────────

        public async Task<ResultadoRegistrarCliente> RegistrarClienteNaturalAsync(
            RegistrarClienteNaturalInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            if (input.IdEmpresa <= 0)
                return Rechazado(new ErrorValidacion(string.Empty, "Empresa requerida."));

            var datos = input.Persona ?? new PersonaDatosInput();
            datos.IdEmpresa = input.IdEmpresa;
            datos.IdPersona = null;
            datos.Modo = ModoValidacionPersona.Alta;
            datos.Empleado = null;                           // este servicio no registra empleados
            datos.DocumentoDeLaEmpresaEsDuplicado = false;   // aquí un documento de la empresa se resuelve más abajo

            var errores = new List<ErrorValidacion>();

            // Un cliente con ficha de persona se identifica por su documento; sin él se registra sin ficha («consumidor final»).
            if (string.IsNullOrWhiteSpace(datos.Documento))
            {
                errores.Add(new ErrorValidacion("Persona.Documento",
                    "Escribe el documento del cliente. Un consumidor final se registra sin ficha de persona."));
            }

            var validacion = await _validacion.ValidarAsync(datos, ct);
            errores.AddRange(validacion.Errores.Select(e => e with { Campo = Prefijo("Persona", e.Campo) }));
            var n = validacion.Datos;

            // Si el documento ya es de una persona de ESTA empresa, se ve qué rol de cliente tiene (si lo tiene).
            Persona? personaExistente = null;
            PersonaEmpresa? vinculo = null;
            Cliente? clienteExistente = null;

            if (validacion.DocumentoExistente is { } existente)
            {
                personaExistente = await _db.Personas.AsNoTracking().FirstAsync(p => p.IdPersona == existente.IdPersona, ct);

                if (existente.TieneVinculoEnEstaEmpresa)
                {
                    vinculo = await _db.PersonaEmpresas
                        .Where(v => v.IdPersona == existente.IdPersona && v.IdEmpresa == input.IdEmpresa
                                    && v.TipoVinculo == TiposVinculo.Cliente && !v.Eliminado)
                        .OrderBy(v => v.FechaFin == null ? 0 : 1)
                        .ThenByDescending(v => v.IdPersonaEmpresa)
                        .FirstOrDefaultAsync(ct);

                    if (vinculo != null)
                    {
                        var idVinculo = vinculo.IdPersonaEmpresa;
                        clienteExistente = await _db.Clientes
                            .FirstOrDefaultAsync(c => c.IdPersonaEmpresa == idVinculo && !c.Eliminado, ct);
                    }

                    if (vinculo is { FechaFin: null } && clienteExistente is { Activo: true })
                        errores.Add(new ErrorValidacion("Persona.Documento", "Esta persona ya es cliente de la empresa."));
                }
            }

            var c = await ValidarClienteAsync(input.Cliente ?? new ClienteDatosInput(), input.IdEmpresa, clienteExistente?.IdCliente, errores, ct);

            // Primero los errores de datos y después la verificación: así quien escribe mal un dato no descubre,
            // por el mensaje, que el documento existe en otra empresa.
            if (errores.Count > 0)
                return Rechazado(errores, validacion.Advertencias);

            // El documento es de una persona que solo está en otras empresas: se verifica antes de vincular nada (D8).
            if (personaExistente != null && validacion.DocumentoExistente is { TieneVinculoEnEstaEmpresa: false }
                && !CoincideVerificacion(personaExistente, n))
            {
                return Rechazado(new ErrorValidacion("Persona.Documento", MensajeNoVerificado), validacion.Advertencias);
            }

            // Persona nueva: se avisa de las parecidas que la empresa ya tiene, con cualquier rol.
            if (personaExistente == null)
            {
                var parecidas = await PersonasParecidas.BuscarAsync(
                    _db, input.IdEmpresa, n.NombreNormalizado, n.PrimerNombre, n.PrimerApellido, n.FechaNacimiento, excluirIdPersona: null, ct);
                if (parecidas.Count > 0 && !input.ConfirmarQueEsOtraPersona)
                {
                    return new ResultadoRegistrarCliente(
                        EstadoRegistrarCliente.RequiereConfirmacion, null, null, null,
                        Array.Empty<ErrorValidacion>(), validacion.Advertencias, parecidas);
                }
            }

            var ahora = Ahora();
            var usuario = input.Usuario;
            Persona? personaNueva = null;
            string razonSocial;
            EstadoRegistrarCliente estado;

            if (personaExistente == null)
            {
                personaNueva = PersonasConstructor.Nueva(n, usuario, ahora);

                // LEGADO: quien solo es cliente no tiene empresa ni cargo en las columnas viejas. Así no entra en
                // las listas de personal (salarios, peajes...) que todavía las leen.
                personaNueva.IdEmpresa = null;
                personaNueva.TipoDocumento = null;
                personaNueva.Documento = null;
                personaNueva.Cargo = null;

                vinculo = NuevoVinculo(input.IdEmpresa, usuario, ahora);
                personaNueva.Vinculos.Add(vinculo);
                _db.Personas.Add(personaNueva);

                razonSocial = RazonSocialCliente.De(n.Nombres, n.Apellidos);
                estado = EstadoRegistrarCliente.Creado;
            }
            else
            {
                // La persona ya existe: sus datos no se tocan, solo se le agrega (o se le reactiva) el rol.
                razonSocial = RazonSocialCliente.De(personaExistente.Nombres, personaExistente.Apellidos);

                if (vinculo == null)
                {
                    vinculo = NuevoVinculo(input.IdEmpresa, usuario, ahora);
                    vinculo.IdPersona = personaExistente.IdPersona;
                    _db.PersonaEmpresas.Add(vinculo);
                    estado = EstadoRegistrarCliente.Vinculado;
                }
                else
                {
                    if (vinculo.FechaFin != null || !vinculo.Activo)
                    {
                        vinculo.FechaFin = null;
                        vinculo.MotivoFin = null;
                        vinculo.Activo = true;
                        vinculo.ModificadoPor = usuario;
                        vinculo.FechaModificacion = ahora;
                    }
                    estado = clienteExistente != null ? EstadoRegistrarCliente.Reactivado : EstadoRegistrarCliente.Vinculado;
                }
            }

            var cliente = clienteExistente ?? new Cliente
            {
                IdEmpresa = input.IdEmpresa,
                Tipo = "natural",
                TipoVinculo = TiposVinculo.Cliente,
                CreadoPor = usuario,
                FechaCreacion = ahora
            };

            if (clienteExistente == null)
            {
                cliente.Vinculo = vinculo;
                _db.Clientes.Add(cliente);
            }
            else
            {
                cliente.ModificadoPor = usuario;
                cliente.FechaModificacion = ahora;
            }

            cliente.Codigo = c.Codigo;
            cliente.RazonSocial = razonSocial;
            cliente.NombreComercial = c.NombreComercial;
            cliente.IdentificadorFiscal = c.IdentificadorFiscal;
            cliente.Email = c.Email;
            cliente.Telefono = c.Telefono;
            cliente.Direccion = c.Direccion;
            cliente.Ciudad = c.Ciudad;
            cliente.MonedaIsoDefault = c.Moneda;
            cliente.IdCondicionPagoDefault = c.IdCondicionPago;
            cliente.LimiteCredito = c.LimiteCredito;
            cliente.Activo = true;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
                return Rechazado(new ErrorValidacion(string.Empty, MensajeConcurrencia), validacion.Advertencias);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo registrar al cliente natural en la empresa {IdEmpresa}", input.IdEmpresa);
                _db.ChangeTracker.Clear();
                return Rechazado(new ErrorValidacion(string.Empty, MensajeNoGuardado), validacion.Advertencias);
            }

            var idPersona = personaNueva?.IdPersona ?? personaExistente!.IdPersona;
            return new ResultadoRegistrarCliente(
                estado, idPersona, cliente.IdCliente, vinculo.IdPersonaEmpresa,
                Array.Empty<ErrorValidacion>(), validacion.Advertencias, Array.Empty<PersonaParecida>());
        }

        private PersonaEmpresa NuevoVinculo(int idEmpresa, string usuario, DateTime ahora) => new()
        {
            IdEmpresa = idEmpresa,
            TipoVinculo = TiposVinculo.Cliente,
            FechaInicio = Hoy(),
            Activo = true,
            CreadoPor = usuario,
            FechaCreacion = ahora
        };

        /// <summary>
        /// Verificación del rol de cliente (decisión D8): el primer nombre y el primer apellido escritos deben coincidir
        /// con los de la persona existente. Al cliente no se le suele pedir la fecha de nacimiento. Si ella no los tiene
        /// registrados, no se puede verificar.
        /// </summary>
        private static bool CoincideVerificacion(Persona existente, PersonaDatosNormalizados escritos) =>
            !string.IsNullOrWhiteSpace(existente.PrimerNombre)
            && !string.IsNullOrWhiteSpace(existente.PrimerApellido)
            && NombresPersona.Normalizar(existente.PrimerNombre) == NombresPersona.Normalizar(escritos.PrimerNombre)
            && NombresPersona.Normalizar(existente.PrimerApellido) == NombresPersona.Normalizar(escritos.PrimerApellido);

        private static ResultadoRegistrarCliente Rechazado(ErrorValidacion error, IReadOnlyList<string>? advertencias = null) =>
            Rechazado(new[] { error }, advertencias);

        private static ResultadoRegistrarCliente Rechazado(IReadOnlyList<ErrorValidacion> errores, IReadOnlyList<string>? advertencias = null) =>
            new(EstadoRegistrarCliente.Rechazado, null, null, null, errores, advertencias ?? Array.Empty<string>(), Array.Empty<PersonaParecida>());

        private static string Prefijo(string prefijo, string campo) =>
            string.IsNullOrEmpty(campo) ? string.Empty : prefijo + "." + campo;

        // ── Validación de los datos comerciales ─────────────────────────────

        private sealed record ClienteNormalizado(
            string Codigo, string? NombreComercial, string? IdentificadorFiscal, string? Email, string? Telefono,
            string? Direccion, string? Ciudad, string Moneda, int? IdCondicionPago, decimal LimiteCredito);

        /// <summary>Valida los datos comerciales y agrega sus errores (con el prefijo «Cliente.») a la lista. Siempre devuelve los datos limpios.</summary>
        private async Task<ClienteNormalizado> ValidarClienteAsync(
            ClienteDatosInput i, int idEmpresa, int? excluirIdCliente, List<ErrorValidacion> errores, CancellationToken ct)
        {
            void Error(string campo, string mensaje) => errores.Add(new ErrorValidacion("Cliente." + campo, mensaje));

            // Código: único dentro de la empresa (el índice cuenta también a los dados de baja).
            var codigo = NombresPersona.Limpiar(i.Codigo);
            if (codigo.Length == 0)
            {
                Error(nameof(ClienteDatosInput.Codigo), "El código del cliente es obligatorio.");
            }
            else if (!FormatoCodigo.IsMatch(codigo))
            {
                Error(nameof(ClienteDatosInput.Codigo),
                    "El código solo admite letras, números, punto, guion y guion bajo, sin espacios, hasta 30 caracteres.");
            }
            else if (await _db.Clientes.AsNoTracking()
                         .AnyAsync(x => x.IdEmpresa == idEmpresa && x.Codigo == codigo
                                        && (excluirIdCliente == null || x.IdCliente != excluirIdCliente), ct))
            {
                Error(nameof(ClienteDatosInput.Codigo), "Ya existe un cliente con ese código en esta empresa.");
            }

            var nombreComercial = Opcional(i.NombreComercial);
            if (nombreComercial is { Length: > 150 })
                Error(nameof(ClienteDatosInput.NombreComercial), "El nombre comercial no puede pasar de 150 caracteres.");

            // Identificador fiscal (RTN u otro): letras y números, sin separadores.
            var fiscal = Opcional(i.IdentificadorFiscal);
            if (fiscal != null)
            {
                fiscal = DocumentosIdentidad.NormalizarNumero(fiscal);
                if (!FormatoIdentificadorFiscal.IsMatch(fiscal))
                    Error(nameof(ClienteDatosInput.IdentificadorFiscal), "El identificador fiscal solo admite letras y números, hasta 50 caracteres.");
            }

            string? email = null;
            if (!string.IsNullOrWhiteSpace(i.Email))
            {
                if (!ContactoPersona.TryNormalizarCorreo(i.Email, out email) || email.Length > 100)
                {
                    email = null;
                    Error(nameof(ClienteDatosInput.Email), "El correo no es válido.");
                }
            }

            string? telefono = null;
            if (!string.IsNullOrWhiteSpace(i.Telefono))
            {
                if (!ContactoPersona.TryNormalizarTelefono(i.Telefono, out telefono))
                {
                    telefono = null;
                    Error(nameof(ClienteDatosInput.Telefono), "El teléfono debe tener 8 dígitos.");
                }
            }

            var direccion = Opcional(i.Direccion);
            if (direccion is { Length: > 300 })
                Error(nameof(ClienteDatosInput.Direccion), "La dirección no puede pasar de 300 caracteres.");

            var ciudad = Opcional(i.Ciudad);
            if (ciudad is { Length: > 100 })
                Error(nameof(ClienteDatosInput.Ciudad), "La ciudad no puede pasar de 100 caracteres.");

            var moneda = NombresPersona.Limpiar(i.MonedaIsoDefault).ToUpperInvariant();
            if (moneda.Length == 0) moneda = _opt.MonedaPorDefecto;
            if (!await _db.Monedas.AsNoTracking().AnyAsync(m => m.CodigoIso == moneda && m.Activo, ct))
                Error(nameof(ClienteDatosInput.MonedaIsoDefault), "La moneda no es válida.");

            if (i.IdCondicionPagoDefault is { } idCondicion
                && !await _db.CondicionesPago.AsNoTracking()
                    .AnyAsync(x => x.IdCondicionPago == idCondicion && x.IdEmpresa == idEmpresa && x.Activo && !x.Eliminado, ct))
            {
                Error(nameof(ClienteDatosInput.IdCondicionPagoDefault), "La condición de pago no es válida.");
            }

            var limite = i.LimiteCredito ?? 0m;
            if (limite < 0)
                Error(nameof(ClienteDatosInput.LimiteCredito), "El límite de crédito no puede ser negativo.");
            else if (decimal.Round(limite, 2) != limite)
                Error(nameof(ClienteDatosInput.LimiteCredito), "El límite de crédito admite como máximo 2 decimales.");
            else if (limite > LimiteCreditoMaximo)
                Error(nameof(ClienteDatosInput.LimiteCredito), "El límite de crédito es demasiado grande.");

            return new ClienteNormalizado(
                codigo, nombreComercial, fiscal, email, telefono, direccion, ciudad, moneda, i.IdCondicionPagoDefault, limite);
        }

        /// <summary>Texto sin espacios de más; nulo si queda vacío.</summary>
        private static string? Opcional(string? valor)
        {
            var limpio = NombresPersona.Limpiar(valor);
            return limpio.Length == 0 ? null : limpio;
        }

        // ──────────────────────────────────────────────────────────────────
        //  TERMINAR CLIENTE
        // ──────────────────────────────────────────────────────────────────

        public async Task<ResultadoTerminarCliente> TerminarClienteAsync(TerminarClienteInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            var noEncontrado = new ResultadoTerminarCliente(false, false, "No se encontró al cliente.");

            if (input.IdEmpresa <= 0 || input.IdCliente <= 0) return noEncontrado;

            // Solo clientes de la empresa de la sesión y enlazados a una persona: los demás siguen con su pantalla.
            var cliente = await _db.Clientes
                .Include(c => c.Vinculo)
                .FirstOrDefaultAsync(c => c.IdCliente == input.IdCliente && c.IdEmpresa == input.IdEmpresa
                                          && !c.Eliminado && c.IdPersonaEmpresa != null, ct);
            if (cliente?.Vinculo == null) return noEncontrado;

            var vinculo = cliente.Vinculo;
            if (vinculo.FechaFin != null && !cliente.Activo)
                return new ResultadoTerminarCliente(false, true, "El cliente ya estaba dado de baja.");

            var fin = input.FechaFin ?? Hoy();
            if (vinculo.FechaInicio is { } inicio && fin < inicio)
            {
                return new ResultadoTerminarCliente(false, true,
                    $"La fecha de fin no puede ser anterior al inicio de la relación ({inicio:dd/MM/yyyy}).");
            }

            var motivo = Opcional(input.Motivo);
            if (motivo is { Length: > 200 })
                return new ResultadoTerminarCliente(false, true, "El motivo no puede pasar de 200 caracteres.");

            var ahora = Ahora();
            vinculo.FechaFin = fin;
            vinculo.MotivoFin = motivo;
            vinculo.Activo = false;
            vinculo.ModificadoPor = input.Usuario;
            vinculo.FechaModificacion = ahora;

            cliente.Activo = false;
            cliente.ModificadoPor = input.Usuario;
            cliente.FechaModificacion = ahora;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
                return new ResultadoTerminarCliente(false, true, MensajeConcurrencia);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo dar de baja al cliente {IdCliente}", input.IdCliente);
                _db.ChangeTracker.Clear();
                return new ResultadoTerminarCliente(false, true, MensajeNoGuardado);
            }

            return new ResultadoTerminarCliente(true, true, string.Empty);
        }

        // ──────────────────────────────────────────────────────────────────
        //  CONSULTAR VÍNCULOS
        // ──────────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<VinculoDePersona>> ListarVinculosAsync(
            int idEmpresa, int idPersona, CancellationToken ct = default)
        {
            if (idEmpresa <= 0 || idPersona <= 0) return Array.Empty<VinculoDePersona>();

            // Solo si la persona es de esta empresa: de las demás no se revela nada.
            var esDeLaEmpresa = await _db.Personas.AsNoTracking()
                .AnyAsync(p => p.IdPersona == idPersona && !p.Eliminado && p.IdPersonaPrincipal == null
                               && p.Vinculos.Any(v => v.IdEmpresa == idEmpresa && !v.Eliminado), ct);
            if (!esDeLaEmpresa) return Array.Empty<VinculoDePersona>();

            return await _db.PersonaEmpresas.AsNoTracking()
                .Where(v => v.IdPersona == idPersona && v.IdEmpresa == idEmpresa && !v.Eliminado)
                .OrderBy(v => v.TipoVinculo)
                .ThenByDescending(v => v.IdPersonaEmpresa)
                .Select(v => new VinculoDePersona(
                    v.IdPersonaEmpresa, v.TipoVinculo, v.FechaInicio, v.FechaFin, v.MotivoFin, v.Activo, v.FechaFin == null))
                .ToListAsync(ct);
        }
    }
}
