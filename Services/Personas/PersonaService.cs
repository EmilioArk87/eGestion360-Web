using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;

namespace eGestion360Web.Services.Personas
{
    public sealed class PersonaService : IPersonaService
    {
        /// <summary>Se usa siempre que no coincide la verificación: no distingue si el documento existe o no (decisión D8).</summary>
        private const string MensajeNoVerificado =
            "No se pudo registrar a la persona con esos datos. Verifica el documento, el primer apellido y la fecha de nacimiento.";

        private const string MensajeNoGuardado = "No se pudo guardar a la persona. Revisa los datos e intenta de nuevo.";

        private const string MensajeDocumentoDeLaEmpresa = "Ya existe una persona con ese documento en esta empresa.";

        private const string MensajeConcurrencia = "Otro usuario modificó a esta persona. Recarga la pantalla y vuelve a intentarlo.";

        private static readonly JsonSerializerOptions OpcionesJson = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        private readonly ApplicationDbContext _db;
        private readonly IPersonaValidacionService _validacion;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<PersonaService> _log;

        public PersonaService(
            ApplicationDbContext db, IPersonaValidacionService validacion, TimeProvider tiempo, ILogger<PersonaService> log)
        {
            _db = db;
            _validacion = validacion;
            _tiempo = tiempo;
            _log = log;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;

        // ──────────────────────────────────────────────────────────────────
        //  CREAR
        // ──────────────────────────────────────────────────────────────────

        public async Task<ResultadoCrearPersona> CrearAsync(CrearPersonaInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            var datos = input.Datos;
            datos.Modo = ModoValidacionPersona.Alta;
            datos.IdPersona = null;

            // Este servicio decide qué hacer con un documento que la empresa ya tiene (ver más abajo).
            datos.DocumentoDeLaEmpresaEsDuplicado = false;

            if (datos.Empleado == null)
                return Rechazada(new ErrorValidacion("Empleado", "Este servicio solo registra empleados; los clientes se registran con IVinculoService."));

            var validacion = await _validacion.ValidarAsync(datos, ct);
            if (!validacion.Ok)
                return Rechazada(validacion.Errores, validacion.Advertencias);

            var n = validacion.Datos;

            // El documento ya es de una persona de ESTA empresa con otro rol (por ejemplo, un cliente): no se crea
            // otra persona, se le agrega el rol de empleado. Si ya es empleado de la empresa, es un duplicado. No hace
            // falta verificar nada: la empresa ya la tiene y la ve.
            if (validacion.DocumentoExistente is { TieneVinculoEnEstaEmpresa: true } deLaEmpresa)
            {
                var yaEsEmpleado = await _db.PersonaEmpresas.AsNoTracking()
                    .AnyAsync(v => v.IdPersona == deLaEmpresa.IdPersona && v.IdEmpresa == datos.IdEmpresa
                                   && v.TipoVinculo == TiposVinculo.Empleado && !v.Eliminado, ct);
                if (yaEsEmpleado)
                    return Rechazada(new ErrorValidacion(nameof(PersonaDatosInput.Documento), MensajeDocumentoDeLaEmpresa), validacion.Advertencias);

                return await VincularAsync(deLaEmpresa.IdPersona, datos.IdEmpresa, n, input.Usuario, validacion.Advertencias, ct);
            }

            // El documento ya existe en otra empresa: se verifica antes de revelar o vincular nada (D8).
            if (validacion.DocumentoExistente is { TieneVinculoEnEstaEmpresa: false } otra)
            {
                var existente = await _db.Personas.AsNoTracking().FirstAsync(p => p.IdPersona == otra.IdPersona, ct);
                if (!CoincideVerificacion(existente, n))
                    return Rechazada(new ErrorValidacion(nameof(PersonaDatosInput.Documento), MensajeNoVerificado), validacion.Advertencias);

                return await VincularAsync(existente.IdPersona, datos.IdEmpresa, n, input.Usuario, validacion.Advertencias, ct);
            }

            // Personas parecidas en la empresa: el usuario debe confirmar que es otra.
            var parecidas = await PersonasParecidas.BuscarAsync(
                _db, datos.IdEmpresa, n.NombreNormalizado, n.PrimerNombre, n.PrimerApellido, n.FechaNacimiento, excluirIdPersona: null, ct);
            if (parecidas.Count > 0 && !input.ConfirmarQueEsOtraPersona)
            {
                return new ResultadoCrearPersona(
                    EstadoCrearPersona.RequiereConfirmacion, null, null, Array.Empty<ErrorValidacion>(), validacion.Advertencias, parecidas);
            }

            var persona = ConstruirPersona(n, datos.IdEmpresa, input.Usuario);
            _db.Personas.Add(persona);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo crear la persona en la empresa {IdEmpresa}", datos.IdEmpresa);
                DescartarAltasPendientes();
                return Rechazada(new ErrorValidacion(string.Empty, MensajeNoGuardado), validacion.Advertencias);
            }

            return new ResultadoCrearPersona(
                EstadoCrearPersona.Creada, persona.IdPersona, persona.Vinculos.Single().IdPersonaEmpresa,
                Array.Empty<ErrorValidacion>(), validacion.Advertencias, Array.Empty<PersonaParecida>());
        }

        /// <summary>Vincula a una persona que ya existe (de otra empresa) con esta empresa como empleado. No toca los datos de la persona.</summary>
        private async Task<ResultadoCrearPersona> VincularAsync(
            int idPersona, int idEmpresa, PersonaDatosNormalizados n, string usuario,
            IReadOnlyList<string> advertencias, CancellationToken ct)
        {
            var vinculo = ConstruirVinculo(idEmpresa, n.Empleado!, usuario);
            vinculo.IdPersona = idPersona;
            _db.PersonaEmpresas.Add(vinculo);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo vincular a la persona {IdPersona} con la empresa {IdEmpresa}", idPersona, idEmpresa);
                DescartarAltasPendientes();
                return Rechazada(new ErrorValidacion(string.Empty, MensajeNoGuardado), advertencias);
            }

            return new ResultadoCrearPersona(
                EstadoCrearPersona.Vinculada, idPersona, vinculo.IdPersonaEmpresa,
                Array.Empty<ErrorValidacion>(), advertencias, Array.Empty<PersonaParecida>());
        }

        /// <summary>
        /// Verificación del rol de empleado (decisión D8): el primer apellido y la fecha de nacimiento escritos
        /// deben coincidir con los de la persona existente. Si ella no los tiene registrados, no se puede verificar.
        /// </summary>
        private static bool CoincideVerificacion(Persona existente, PersonaDatosNormalizados escritos) =>
            !string.IsNullOrWhiteSpace(existente.PrimerApellido)
            && existente.FechaNacimiento != null
            && escritos.FechaNacimiento != null
            && existente.FechaNacimiento == escritos.FechaNacimiento
            && NombresPersona.Normalizar(existente.PrimerApellido) == NombresPersona.Normalizar(escritos.PrimerApellido);

        private Persona ConstruirPersona(PersonaDatosNormalizados n, int idEmpresa, string usuario)
        {
            var persona = PersonasConstructor.Nueva(n, usuario, Ahora());
            persona.Vinculos.Add(ConstruirVinculo(idEmpresa, n.Empleado!, usuario));
            return persona;
        }

        private PersonaEmpresa ConstruirVinculo(int idEmpresa, EmpleadoDatosNormalizados e, string usuario)
        {
            var ahora = Ahora();
            return new PersonaEmpresa
            {
                IdEmpresa = idEmpresa,
                TipoVinculo = TiposVinculo.Empleado,
                FechaInicio = e.FechaIngreso,
                FechaFin = e.FechaBaja,
                Activo = true,
                CreadoPor = usuario,
                FechaCreacion = ahora,
                Empleado = new Empleado
                {
                    IdEmpresa = idEmpresa,
                    TipoVinculo = TiposVinculo.Empleado,
                    CodigoInterno = e.CodigoInterno,
                    Cargo = e.Cargo,
                    TarifaDiaria = e.TarifaDiaria,
                    MonedaTarifa = e.MonedaTarifa,
                    Activo = true,
                    CreadoPor = usuario,
                    FechaCreacion = ahora
                }
            };
        }

        private static ResultadoCrearPersona Rechazada(ErrorValidacion error, IReadOnlyList<string>? advertencias = null) =>
            Rechazada(new[] { error }, advertencias);

        private static ResultadoCrearPersona Rechazada(IReadOnlyList<ErrorValidacion> errores, IReadOnlyList<string>? advertencias = null) =>
            new(EstadoCrearPersona.Rechazada, null, null, errores, advertencias ?? Array.Empty<string>(), Array.Empty<PersonaParecida>());

        // ──────────────────────────────────────────────────────────────────
        //  ACTUALIZAR
        // ──────────────────────────────────────────────────────────────────

        public async Task<ResultadoActualizarPersona> ActualizarAsync(ActualizarPersonaInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            var datos = input.Datos;
            datos.Modo = ModoValidacionPersona.Edicion;

            // Aquí un documento de otra persona de la empresa sí es un duplicado, diga lo que diga el formulario.
            datos.DocumentoDeLaEmpresaEsDuplicado = true;

            if (datos.IdPersona is not > 0 || datos.IdEmpresa <= 0)
                return Actualizacion(EstadoActualizarPersona.NoEncontrada, null);

            var idPersona = datos.IdPersona.Value;
            var idEmpresa = datos.IdEmpresa;

            // Solo se encuentra a una persona con vínculo en la empresa de la sesión. Del resto no se carga nada.
            var persona = await _db.Personas
                .Include(p => p.Documentos)
                .Include(p => p.Vinculos.Where(v => v.IdEmpresa == idEmpresa && !v.Eliminado)).ThenInclude(v => v.Empleado)
                .FirstOrDefaultAsync(p => p.IdPersona == idPersona && !p.Eliminado && p.IdPersonaPrincipal == null
                                          && p.Vinculos.Any(v => v.IdEmpresa == idEmpresa && !v.Eliminado), ct);
            if (persona == null)
                return Actualizacion(EstadoActualizarPersona.NoEncontrada, null);

            var validacion = await _validacion.ValidarAsync(datos, ct);
            if (!validacion.Ok)
                return Actualizacion(EstadoActualizarPersona.Rechazada, null, validacion.Errores, validacion.Advertencias);

            var n = validacion.Datos;

            // El documento escrito es de otra persona de otra empresa: se verifica y se ofrece fusionar.
            if (validacion.DocumentoExistente is { TieneVinculoEnEstaEmpresa: false } otra)
            {
                var principal = await _db.Personas.AsNoTracking().FirstAsync(p => p.IdPersona == otra.IdPersona, ct);
                if (!CoincideVerificacion(principal, n))
                {
                    return Actualizacion(EstadoActualizarPersona.Rechazada, null,
                        new[] { new ErrorValidacion(nameof(PersonaDatosInput.Documento), MensajeNoVerificado) }, validacion.Advertencias);
                }

                if (!input.ConfirmarFusion)
                    return Actualizacion(EstadoActualizarPersona.RequiereFusion, null, advertencias: validacion.Advertencias);

                var fusion = await FusionarNucleoAsync(idPersona, principal.IdPersona, input.Usuario, idEmpresa, ct);
                return fusion.Ok
                    ? Actualizacion(EstadoActualizarPersona.Fusionada, fusion.IdPersonaPrincipal, advertencias: validacion.Advertencias)
                    : Actualizacion(EstadoActualizarPersona.Rechazada, null,
                        fusion.Errores.Select(m => new ErrorValidacion(string.Empty, m)).ToList(), validacion.Advertencias);
            }

            return await GuardarCambiosAsync(persona, n, idEmpresa, input, validacion.Advertencias, ct);
        }

        public async Task<ResultadoActualizarPersona> ActualizarComoAdministradorAsync(ActualizarPersonaInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            var datos = input.Datos;
            datos.Modo = ModoValidacionPersona.EdicionAdministrador;
            datos.IdEmpresa = 0;      // el administrador general no trabaja con ninguna empresa
            datos.Empleado = null;    // los datos de empleo son de cada empresa: aquí no se tocan
            datos.DocumentoDeLaEmpresaEsDuplicado = true;

            if (datos.IdPersona is not > 0)
                return Actualizacion(EstadoActualizarPersona.NoEncontrada, null);

            var idPersona = datos.IdPersona.Value;

            // Cualquier persona, de la empresa que sea, salvo las eliminadas y las fusionadas en otra.
            var persona = await _db.Personas
                .Include(p => p.Documentos)
                .FirstOrDefaultAsync(p => p.IdPersona == idPersona && !p.Eliminado && p.IdPersonaPrincipal == null, ct);
            if (persona == null)
                return Actualizacion(EstadoActualizarPersona.NoEncontrada, null);

            var validacion = await _validacion.ValidarAsync(datos, ct);
            if (!validacion.Ok)
                return Actualizacion(EstadoActualizarPersona.Rechazada, null, validacion.Errores, validacion.Advertencias);

            return await GuardarCambiosAsync(persona, validacion.Datos, 0, input, validacion.Advertencias, ct);
        }

        /// <summary>Aplica los datos validados a la persona y guarda: concurrencia, razón social de sus clientes y errores de la base.</summary>
        private async Task<ResultadoActualizarPersona> GuardarCambiosAsync(
            Persona persona, PersonaDatosNormalizados n, int idEmpresa, ActualizarPersonaInput input,
            IReadOnlyList<string> advertencias, CancellationToken ct)
        {
            if (input.TokenConcurrencia is { Length: > 0 } token)
                _db.Entry(persona).Property(p => p.TokenConcurrencia).OriginalValue = token;

            await AplicarCambiosAsync(persona, n, idEmpresa, input.Usuario, ct);
            await SincronizarRazonSocialAsync(persona.IdPersona, persona.Nombres, persona.Apellidos, input.Usuario, Ahora(), ct);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Actualizacion(EstadoActualizarPersona.Rechazada, null,
                    new[] { new ErrorValidacion(string.Empty, MensajeConcurrencia) }, advertencias);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo actualizar a la persona {IdPersona}", persona.IdPersona);
                return Actualizacion(EstadoActualizarPersona.Rechazada, null,
                    new[] { new ErrorValidacion(string.Empty, MensajeNoGuardado) }, advertencias);
            }

            return Actualizacion(EstadoActualizarPersona.Actualizada, persona.IdPersona, advertencias: advertencias);
        }

        private async Task AplicarCambiosAsync(Persona persona, PersonaDatosNormalizados n, int idEmpresa, string usuario, CancellationToken ct)
        {
            var ahora = Ahora();
            var e = n.Empleado;

            // Nombre y datos personales. Lo que viene vacío borra el valor (ver ActualizarPersonaInput).
            persona.PrimerNombre = n.PrimerNombre;
            persona.SegundoNombre = n.SegundoNombre;
            persona.PrimerApellido = n.PrimerApellido;
            persona.SegundoApellido = n.SegundoApellido;
            persona.NombreNormalizado = n.NombreNormalizado;
            persona.Nombres = n.Nombres;       // compuesto
            persona.Apellidos = n.Apellidos;   // compuesto
            persona.Sexo = n.Sexo;
            persona.EstadoCivil = n.EstadoCivil;
            persona.FechaNacimiento = n.FechaNacimiento;
            persona.PaisNacionalidad = n.PaisNacionalidad;
            persona.TipoSangre = n.TipoSangre;
            persona.IdMunicipioNacimiento = n.IdMunicipioNacimiento;
            persona.IdMunicipioResidencia = n.IdMunicipioResidencia;
            persona.DireccionResidencia = n.DireccionResidencia;

            persona.Telefono = n.Telefono;
            persona.TelefonoSecundario = n.TelefonoSecundario;
            persona.Email = n.Email;
            persona.ContactoEmergenciaNombre = n.ContactoEmergenciaNombre;
            persona.ContactoEmergenciaTelefono = n.ContactoEmergenciaTelefono;
            persona.ContactoEmergenciaParentesco = n.ContactoEmergenciaParentesco;

            persona.LicenciaTipo = n.LicenciaTipo;
            persona.LicenciaNumero = n.LicenciaNumero;
            persona.LicenciaVencimiento = n.LicenciaVencimiento;

            // Documento: vacío lo deja como está; un número nuevo se agrega, y uno distinto del mismo tipo lo corrige.
            if (!string.IsNullOrEmpty(n.Documento))
            {
                var vigentes = persona.Documentos.Where(d => !d.Eliminado).ToList();
                var mismoNumero = vigentes.Any(d => d.TipoDocumento == n.TipoDocumento && d.PaisEmisor == n.PaisEmisor
                                                    && DocumentosIdentidad.NormalizarNumero(d.Numero) == n.Documento);
                if (!mismoNumero)
                {
                    var delMismoTipo = vigentes.FirstOrDefault(d => d.TipoDocumento == n.TipoDocumento && d.PaisEmisor == n.PaisEmisor);
                    if (delMismoTipo != null)
                    {
                        delMismoTipo.Numero = n.Documento;
                    }
                    else
                    {
                        persona.Documentos.Add(new PersonaDocumento
                        {
                            TipoDocumento = n.TipoDocumento!,
                            PaisEmisor = n.PaisEmisor!,
                            Numero = n.Documento,
                            EsPrincipal = !vigentes.Any(d => d.EsPrincipal),
                            CreadoPor = usuario,
                            FechaCreacion = ahora
                        });
                    }
                }
            }

            // La identidad pasa a verificada cuando tiene un documento de identidad; nunca retrocede.
            var tiposDeIdentidad = await _db.CatalogoTiposDocumento.AsNoTracking()
                .Where(t => t.EsIdentidad).Select(t => t.Codigo).ToListAsync(ct);
            if (persona.Documentos.Any(d => !d.Eliminado && tiposDeIdentidad.Contains(d.TipoDocumento)))
                persona.EstadoIdentidad = EstadosIdentidad.Verificada;

            // Rol de empleado en esta empresa
            if (e != null)
            {
                var vinculo = persona.Vinculos
                    .Where(v => v.IdEmpresa == idEmpresa && !v.Eliminado && v.TipoVinculo == TiposVinculo.Empleado)
                    .OrderBy(v => v.FechaFin == null ? 0 : 1)
                    .FirstOrDefault();

                if (vinculo == null)
                {
                    vinculo = ConstruirVinculo(idEmpresa, e, usuario);
                    persona.Vinculos.Add(vinculo);
                }
                else
                {
                    vinculo.FechaInicio = e.FechaIngreso;
                    vinculo.FechaFin = e.FechaBaja;

                    var empleado = vinculo.Empleado;
                    if (empleado == null)
                    {
                        vinculo.Empleado = new Empleado { IdEmpresa = idEmpresa, TipoVinculo = TiposVinculo.Empleado, CreadoPor = usuario, FechaCreacion = ahora };
                        empleado = vinculo.Empleado;
                    }
                    empleado.CodigoInterno = e.CodigoInterno;
                    empleado.Cargo = e.Cargo;
                    empleado.TarifaDiaria = e.TarifaDiaria;
                    empleado.MonedaTarifa = e.MonedaTarifa;
                }
            }

            MarcarModificados(usuario, ahora);
        }

        /// <summary>
        /// La razón social de un cliente natural es el nombre de la persona: cuando el nombre cambia (o dos fichas se
        /// fusionan) se actualiza la de todos los clientes naturales enlazados a ella, en cualquier empresa. Es un dato
        /// derivado del nombre, no revela nada de una empresa a otra. Quien llama guarda los cambios.
        /// </summary>
        private async Task SincronizarRazonSocialAsync(
            int idPersona, string nombres, string apellidos, string usuario, DateTime ahora, CancellationToken ct)
        {
            var razonSocial = RazonSocialCliente.De(nombres, apellidos);
            var clientes = await _db.Clientes
                .Where(c => !c.Eliminado && c.Tipo == "natural" && c.IdPersonaEmpresa != null
                            && c.Vinculo!.IdPersona == idPersona && c.RazonSocial != razonSocial)
                .ToListAsync(ct);

            foreach (var cliente in clientes)
            {
                cliente.RazonSocial = razonSocial;
                cliente.ModificadoPor = usuario;
                cliente.FechaModificacion = ahora;
            }
        }

        /// <summary>Pone quién y cuándo en lo que cambió de verdad, sin forzar un UPDATE en lo que no cambió.</summary>
        private void MarcarModificados(string usuario, DateTime ahora)
        {
            _db.ChangeTracker.DetectChanges();
            foreach (var entrada in _db.ChangeTracker.Entries().Where(x => x.State == EntityState.Modified).ToList())
            {
                switch (entrada.Entity)
                {
                    case Persona p: p.ModificadoPor = usuario; p.FechaModificacion = ahora; break;
                    case PersonaDocumento d: d.ModificadoPor = usuario; d.FechaModificacion = ahora; break;
                    case PersonaEmpresa v: v.ModificadoPor = usuario; v.FechaModificacion = ahora; break;
                    case Empleado em: em.ModificadoPor = usuario; em.FechaModificacion = ahora; break;
                }
            }
        }

        private static ResultadoActualizarPersona Actualizacion(
            EstadoActualizarPersona estado, int? idPersona,
            IReadOnlyList<ErrorValidacion>? errores = null, IReadOnlyList<string>? advertencias = null) =>
            new(estado, idPersona, errores ?? Array.Empty<ErrorValidacion>(), advertencias ?? Array.Empty<string>());

        // ──────────────────────────────────────────────────────────────────
        //  FUSIONAR
        // ──────────────────────────────────────────────────────────────────

        public async Task<ResultadoFusion> FusionarAsync(FusionarPersonasInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            if (input.IdPersonaSobrante <= 0 || input.IdPersonaPrincipal <= 0 || input.IdPersonaSobrante == input.IdPersonaPrincipal)
                return ErrorFusion("Elige dos personas distintas.");

            // Las dos deben ser de esta empresa: no se fusiona con alguien que la empresa no puede ver.
            var ids = new[] { input.IdPersonaSobrante, input.IdPersonaPrincipal };
            var visibles = await _db.Personas.AsNoTracking()
                .CountAsync(p => ids.Contains(p.IdPersona) && !p.Eliminado && p.IdPersonaPrincipal == null
                                 && p.Vinculos.Any(v => v.IdEmpresa == input.IdEmpresa && !v.Eliminado), ct);
            if (visibles != 2)
                return ErrorFusion("No se encontraron las dos personas en esta empresa.");

            return await FusionarNucleoAsync(input.IdPersonaSobrante, input.IdPersonaPrincipal, input.Usuario, input.IdEmpresa, ct);
        }

        /// <summary>
        /// Fusiona sin comprobar la visibilidad (quien llama ya la resolvió: en la edición, la verificación del
        /// rol). Todo ocurre en una transacción: si algo falla, no cambia nada.
        /// </summary>
        private async Task<ResultadoFusion> FusionarNucleoAsync(
            int idSobrante, int idPrincipal, string usuario, int? idEmpresaSesion, CancellationToken ct)
        {
            var propia = _db.Database.CurrentTransaction == null;
            await using var transaccion = propia ? await _db.Database.BeginTransactionAsync(ct) : null;

            try
            {
                var personas = await _db.Personas
                    .Include(p => p.Documentos)
                    .Include(p => p.Vinculos).ThenInclude(v => v.Empleado)
                    .Where(p => p.IdPersona == idSobrante || p.IdPersona == idPrincipal)
                    .ToListAsync(ct);

                var sobrante = personas.SingleOrDefault(p => p.IdPersona == idSobrante);
                var principal = personas.SingleOrDefault(p => p.IdPersona == idPrincipal);
                if (sobrante == null || principal == null)
                    return ErrorFusion("No se encontraron las dos personas.");
                if (sobrante.Eliminado || sobrante.IdPersonaPrincipal != null)
                    return ErrorFusion("La persona que sobra ya fue fusionada o eliminada.");
                if (principal.Eliminado || principal.IdPersonaPrincipal != null)
                    return ErrorFusion("La persona principal ya fue fusionada o eliminada.");

                var ahora = Ahora();
                int documentosMovidos = 0, documentosRepetidos = 0, vinculosMovidos = 0, vinculosEnConflicto = 0;

                // Documentos: los que la principal ya tiene se dan de baja; los demás pasan a ella.
                var principalTienePrincipal = principal.Documentos.Any(d => !d.Eliminado && d.EsPrincipal);
                foreach (var documento in sobrante.Documentos.Where(d => !d.Eliminado).ToList())
                {
                    var numero = DocumentosIdentidad.NormalizarNumero(documento.Numero);
                    var repetido = principal.Documentos.Any(d => !d.Eliminado && d.TipoDocumento == documento.TipoDocumento
                                                                 && d.PaisEmisor == documento.PaisEmisor
                                                                 && DocumentosIdentidad.NormalizarNumero(d.Numero) == numero);
                    if (repetido)
                    {
                        Baja(documento, ahora);
                        documento.EsPrincipal = false;
                        documentosRepetidos++;
                        continue;
                    }

                    documento.IdPersona = principal.IdPersona;
                    if (principalTienePrincipal) documento.EsPrincipal = false;
                    else if (documento.EsPrincipal) principalTienePrincipal = true;
                    documentosMovidos++;
                }

                // Vínculos: si la principal ya tiene uno vigente de la misma empresa y rol, el de la sobrante se da de baja.
                foreach (var vinculo in sobrante.Vinculos.Where(v => !v.Eliminado).ToList())
                {
                    var enConflicto = vinculo.FechaFin == null
                                      && principal.Vinculos.Any(x => !x.Eliminado && x.FechaFin == null
                                                                     && x.IdEmpresa == vinculo.IdEmpresa && x.TipoVinculo == vinculo.TipoVinculo);
                    if (enConflicto)
                    {
                        Baja(vinculo, ahora);
                        if (vinculo.Empleado != null) Baja(vinculo.Empleado, ahora);
                        vinculosEnConflicto++;
                        continue;
                    }

                    vinculo.IdPersona = principal.IdPersona;
                    vinculosMovidos++;
                }

                // La ficha sobrante queda apuntando a la principal y fuera de las listas.
                sobrante.IdPersonaPrincipal = principal.IdPersona;
                sobrante.Eliminado = true;
                sobrante.Activo = false;
                sobrante.FechaEliminado = ahora;
                sobrante.ModificadoPor = usuario;
                sobrante.FechaModificacion = ahora;

                // Si ahora tiene un documento de identidad, la principal queda verificada.
                var tiposDeIdentidad = await _db.CatalogoTiposDocumento.AsNoTracking()
                    .Where(t => t.EsIdentidad).Select(t => t.Codigo).ToListAsync(ct);
                var tieneIdentidad = principal.Documentos.Any(d => !d.Eliminado && tiposDeIdentidad.Contains(d.TipoDocumento))
                                     || sobrante.Documentos.Any(d => d.IdPersona == principal.IdPersona && !d.Eliminado && tiposDeIdentidad.Contains(d.TipoDocumento));
                if (tieneIdentidad && principal.EstadoIdentidad != EstadosIdentidad.Verificada)
                {
                    principal.EstadoIdentidad = EstadosIdentidad.Verificada;
                    principal.ModificadoPor = usuario;
                    principal.FechaModificacion = ahora;
                }

                await _db.SaveChangesAsync(ct);

                // Registros operativos que apuntan a la persona: se reasignan a la principal.
                var operativos = 0;
                operativos += await _db.CargasCombustible.Where(x => x.IdConductor == idSobrante)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IdConductor, idPrincipal), ct);
                operativos += await _db.ControlSalidas.Where(x => x.IdConductor == idSobrante)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IdConductor, idPrincipal), ct);
                operativos += await _db.OdometrosDiarios.Where(x => x.IdConductor == idSobrante)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IdConductor, idPrincipal), ct);
                operativos += await _db.Peajes.Where(x => x.IdConductor == idSobrante)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IdConductor, idPrincipal), ct);
                operativos += await _db.SalariosDiarios.Where(x => x.IdPersona == idSobrante)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IdPersona, idPrincipal), ct);

                // Los clientes naturales que venían de la ficha sobrante pasan a llamarse como la principal.
                await SincronizarRazonSocialAsync(principal.IdPersona, principal.Nombres, principal.Apellidos, usuario, ahora, ct);

                // Esos registros no pasan por el interceptor: se deja constancia de la fusión y de lo que se reasignó.
                _db.BitacoraCambios.Add(new BitacoraCambio
                {
                    IdTransaccion = Guid.NewGuid(),
                    IdEmpresa = idEmpresaSesion,
                    IdPersona = idPrincipal,
                    Entidad = "personas",
                    IdRegistro = idSobrante,
                    Operacion = OperacionesBitacora.Update,
                    Campo = "fusion",
                    ValorNuevo = JsonSerializer.Serialize(new
                    {
                        persona_principal = idPrincipal,
                        documentos = documentosMovidos,
                        documentos_repetidos = documentosRepetidos,
                        vinculos = vinculosMovidos,
                        vinculos_en_conflicto = vinculosEnConflicto,
                        registros_operativos = operativos
                    }, OpcionesJson),
                    FechaHora = ahora,
                    Usuario = usuario.Length > 100 ? usuario[..100] : usuario,
                    Origen = "app"
                });
                await _db.SaveChangesAsync(ct);

                if (transaccion != null) await transaccion.CommitAsync(ct);

                return new ResultadoFusion(true, idPrincipal, Array.Empty<string>(),
                    new ResumenFusion(documentosMovidos, documentosRepetidos, vinculosMovidos, vinculosEnConflicto, operativos));
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo fusionar a la persona {IdSobrante} en {IdPrincipal}", idSobrante, idPrincipal);
                if (transaccion != null) await transaccion.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                return ErrorFusion("No se pudo fusionar: hay datos que chocan al reasignarlos. No se cambió nada.");
            }
        }

        private static void Baja(PersonaDocumento d, DateTime ahora) { d.Eliminado = true; d.Activo = false; d.FechaEliminado = ahora; }
        private static void Baja(PersonaEmpresa v, DateTime ahora) { v.Eliminado = true; v.Activo = false; v.FechaEliminado = ahora; }
        private static void Baja(Empleado e, DateTime ahora) { e.Eliminado = true; e.Activo = false; e.FechaEliminado = ahora; }

        private static ResultadoFusion ErrorFusion(string mensaje) => new(false, null, new[] { mensaje }, null);

        // ──────────────────────────────────────────────────────────────────
        //  CAMBIAR ESTADO
        // ──────────────────────────────────────────────────────────────────

        public async Task<ResultadoCambioEstado> CambiarEstadoAsync(CambiarEstadoPersonaInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            var noEncontrada = new ResultadoCambioEstado(false, false, string.Empty);

            if (input.IdEmpresa <= 0 || input.IdPersona <= 0) return noEncontrada;

            // Se cargan todos los vínculos activos de la persona: personas.activo depende de los de otras empresas.
            var persona = await _db.Personas
                .Include(p => p.Vinculos.Where(v => !v.Eliminado)).ThenInclude(v => v.Empleado)
                .FirstOrDefaultAsync(p => p.IdPersona == input.IdPersona && !p.Eliminado && p.IdPersonaPrincipal == null
                                          && p.Vinculos.Any(v => v.IdEmpresa == input.IdEmpresa && !v.Eliminado
                                                                 && v.TipoVinculo == TiposVinculo.Empleado), ct);
            if (persona == null) return noEncontrada;

            var ahora = Ahora();
            foreach (var vinculo in persona.Vinculos.Where(v => v.IdEmpresa == input.IdEmpresa && v.TipoVinculo == TiposVinculo.Empleado))
            {
                vinculo.Activo = input.Activo;
                if (vinculo.Empleado != null) vinculo.Empleado.Activo = input.Activo;
            }

            // personas.activo es una sola columna: no se apaga mientras la persona siga activa en otra empresa.
            persona.Activo = persona.Vinculos.Any(v => v.Activo);

            MarcarModificados(input.Usuario, ahora);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo cambiar el estado de la persona {IdPersona}", input.IdPersona);
                return new ResultadoCambioEstado(false, true, persona.NombreCompleto);
            }

            return new ResultadoCambioEstado(true, true, persona.NombreCompleto);
        }

        // ──────────────────────────────────────────────────────────────────
        //  UTILIDADES
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Si un guardado falla, quita del contexto lo que se había agregado para que no estorbe al siguiente.</summary>
        private void DescartarAltasPendientes()
        {
            foreach (var entrada in _db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                entrada.State = EntityState.Detached;
        }
    }
}
