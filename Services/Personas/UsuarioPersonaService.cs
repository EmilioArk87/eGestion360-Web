using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;

namespace eGestion360Web.Services.Personas
{
    public sealed class UsuarioPersonaService : IUsuarioPersonaService
    {
        private const int MaximoResultados = 20;

        /// <summary>Verificación D8 fallida: no distingue si el documento existe o no.</summary>
        private const string MensajeNoVerificado =
            "No se pudo vincular a la persona con esos datos. Verifica el documento, el primer nombre y el primer apellido.";

        private const string MensajeYaRegistrada =
            "Esa persona ya está registrada: búscala por su nombre o su documento y vincúlala.";

        private const string MensajeNoGuardado = "No se pudo guardar. Revisa los datos e intenta de nuevo.";

        private readonly ApplicationDbContext _db;
        private readonly IPersonaValidacionService _validacion;
        private readonly PlataformaOptions _plataforma;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<UsuarioPersonaService> _log;

        public UsuarioPersonaService(
            ApplicationDbContext db, IPersonaValidacionService validacion, IOptions<PlataformaOptions> plataforma,
            TimeProvider tiempo, ILogger<UsuarioPersonaService> log)
        {
            _db = db;
            _validacion = validacion;
            _plataforma = plataforma.Value;
            _tiempo = tiempo;
            _log = log;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;

        /// <summary>Hoy en Honduras (UTC-6).</summary>
        private DateOnly Hoy() => DateOnly.FromDateTime(Ahora().AddHours(-6));

        /// <summary>La empresa donde vive la persona de un usuario: la suya o, si no tiene (el administrador general), la de la plataforma.</summary>
        private int EmpresaDeLaPersona(User usuario) => usuario.EmpresaId ?? _plataforma.IdEmpresaPropia;

        // ──────────────────────────────────────────────────────────────────
        //  CONSULTAS
        // ──────────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<PersonaParaUsuario>> BuscarPersonasAsync(
            QuienOperaUsuarios quien, string? texto, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            if (!PuedeOperar(quien)) return Array.Empty<PersonaParaUsuario>();

            var limpio = NombresPersona.Limpiar(texto);
            if (limpio.Length < 2) return Array.Empty<PersonaParaUsuario>();

            var nombre = NombresPersona.Normalizar(limpio);
            var digitos = DocumentosIdentidad.NormalizarNumero(limpio);
            var consulta = PersonasVisibles(quien).Where(p =>
                (nombre != "" && p.NombreNormalizado != null && p.NombreNormalizado.Contains(nombre))
                || (digitos.Length >= 4 && p.Documentos.Any(d => !d.Eliminado && d.NumeroNormalizado != null && d.NumeroNormalizado.Contains(digitos))));

            return await ProyectarAsync(quien, consulta.OrderBy(p => p.Apellidos).ThenBy(p => p.Nombres).Take(MaximoResultados), ct);
        }

        public async Task<PersonaParaUsuario?> PersonaVisibleAsync(QuienOperaUsuarios quien, int idPersona, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            if (!PuedeOperar(quien) || idPersona <= 0) return null;

            var filas = await ProyectarAsync(quien, PersonasVisibles(quien).Where(p => p.IdPersona == idPersona), ct);
            return filas.FirstOrDefault();
        }

        public async Task<PersonaParaUsuario?> PersonaDelUsuarioAsync(QuienOperaUsuarios quien, int idUsuario, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            var usuario = await UsuarioVisibleAsync(quien, idUsuario, ct);
            if (usuario?.PersonaId is not { } idPersona) return null;

            var filas = await ProyectarAsync(quien, _db.Personas.Where(p => p.IdPersona == idPersona), ct);
            return filas.FirstOrDefault();
        }

        /// <summary>
        /// Las personas que quien opera puede ver: vivas y sin fusionar. Un administrador de empresa solo ve las que tienen
        /// relación con su empresa (con cualquier rol, vigente o no): nunca personas de otra empresa.
        /// </summary>
        private IQueryable<Persona> PersonasVisibles(QuienOperaUsuarios quien)
        {
            var consulta = _db.Personas.Where(p => !p.Eliminado && p.IdPersonaPrincipal == null);
            if (quien.EsAdministradorGeneral) return consulta;

            var idEmpresa = quien.IdEmpresa ?? 0;
            return consulta.Where(p => p.Vinculos.Any(v => v.IdEmpresa == idEmpresa && !v.Eliminado));
        }

        private async Task<IReadOnlyList<PersonaParaUsuario>> ProyectarAsync(
            QuienOperaUsuarios quien, IQueryable<Persona> consulta, CancellationToken ct)
        {
            var filas = await consulta
                .Select(p => new
                {
                    p.IdPersona, p.PrimerNombre, p.SegundoNombre, p.PrimerApellido, p.SegundoApellido, p.Nombres, p.Apellidos,
                    Documento = p.Documentos
                        .Where(d => !d.Eliminado)
                        .OrderByDescending(d => d.EsPrincipal).ThenBy(d => d.IdPersonaDocumento)
                        .Select(d => d.Numero)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);
            if (filas.Count == 0) return Array.Empty<PersonaParaUsuario>();

            var ids = filas.Select(f => f.IdPersona).ToList();

            // Los usuarios de la persona. A un administrador de empresa solo se le muestran los de su empresa.
            var usuarios = await _db.Users.AsNoTracking()
                .Where(u => u.PersonaId != null && ids.Contains(u.PersonaId.Value)
                            && (quien.EsAdministradorGeneral || u.EmpresaId == quien.IdEmpresa))
                .OrderBy(u => u.Username)
                .Select(u => new { IdPersona = u.PersonaId!.Value, u.Username })
                .ToListAsync(ct);

            // Las empresas, solo para el administrador general: a una empresa no se le dice con quién más se relaciona.
            var empresas = quien.EsAdministradorGeneral
                ? await _db.PersonaEmpresas.AsNoTracking()
                    .Where(v => ids.Contains(v.IdPersona) && !v.Eliminado)
                    .Select(v => new { v.IdPersona, v.Empresa.RazonSocial })
                    .Distinct()
                    .ToListAsync(ct)
                : new();

            return filas.Select(f =>
            {
                var separados = !string.IsNullOrWhiteSpace(f.PrimerNombre) && !string.IsNullOrWhiteSpace(f.PrimerApellido);
                var apellidos = separados ? NombresPersona.ComponerApellidos(f.PrimerApellido, f.SegundoApellido) : f.Apellidos;
                var nombres = separados ? NombresPersona.ComponerNombres(f.PrimerNombre, f.SegundoNombre) : f.Nombres;

                return new PersonaParaUsuario(
                    f.IdPersona,
                    $"{apellidos}, {nombres}",
                    f.Documento == null ? null : DocumentosIdentidad.Enmascarar(f.Documento),
                    empresas.Where(e => e.IdPersona == f.IdPersona).Select(e => e.RazonSocial).OrderBy(x => x).ToList(),
                    usuarios.Where(u => u.IdPersona == f.IdPersona).Select(u => u.Username).ToList());
            }).ToList();
        }

        // ──────────────────────────────────────────────────────────────────
        //  VINCULAR, CREAR Y QUITAR
        // ──────────────────────────────────────────────────────────────────

        public async Task<ResultadoUsuarioPersona> VincularAsync(
            QuienOperaUsuarios quien, int idUsuario, int idPersona, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            var usuario = await UsuarioVisibleAsync(quien, idUsuario, ct);
            if (usuario == null) return NoEncontrado("No se encontró al usuario.");

            var persona = await PersonasVisibles(quien)
                .Include(p => p.Vinculos.Where(v => !v.Eliminado))
                .FirstOrDefaultAsync(p => p.IdPersona == idPersona, ct);
            if (persona == null) return NoEncontrado("No se encontró a la persona.");

            var ahora = Ahora();
            var anterior = usuario.PersonaId;
            var relacionNueva = AsegurarRelacionConLaEmpresa(persona, EmpresaDeLaPersona(usuario), quien.Usuario, ahora);

            if (anterior == persona.IdPersona && !relacionNueva)
                return new ResultadoUsuarioPersona(EstadoUsuarioPersona.SinCambios, persona.IdPersona, Array.Empty<ErrorValidacion>(), Array.Empty<PersonaParecida>());

            usuario.PersonaId = persona.IdPersona;
            if (anterior is { } idAnterior && idAnterior != persona.IdPersona)
                await CerrarRelacionSiQuedaSinUsuarioAsync(idAnterior, EmpresaDeLaPersona(usuario), usuario.Id, quien.Usuario, ahora, ct);

            return await GuardarAsync(EstadoUsuarioPersona.Vinculado, persona.IdPersona, idUsuario, ct);
        }

        public async Task<ResultadoUsuarioPersona> CrearPersonaYVincularAsync(
            QuienOperaUsuarios quien, int idUsuario, PersonaDatosInput datos, bool confirmarQueEsOtraPersona, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            ArgumentNullException.ThrowIfNull(datos);
            var usuario = await UsuarioVisibleAsync(quien, idUsuario, ct);
            if (usuario == null) return NoEncontrado("No se encontró al usuario.");

            var idEmpresa = EmpresaDeLaPersona(usuario);
            datos.IdEmpresa = idEmpresa;
            datos.IdPersona = null;
            datos.Modo = ModoValidacionPersona.Alta;
            datos.Empleado = null;                           // aquí no se registran datos de empleo
            datos.DocumentoDeLaEmpresaEsDuplicado = false;   // el documento existente se resuelve más abajo

            var errores = new List<ErrorValidacion>();
            if (string.IsNullOrWhiteSpace(datos.Documento))
                errores.Add(new ErrorValidacion(nameof(PersonaDatosInput.Documento), "Escribe el documento de la persona."));
            if (datos.FechaNacimiento == null)
                errores.Add(new ErrorValidacion(nameof(PersonaDatosInput.FechaNacimiento), "Escribe la fecha de nacimiento."));

            var validacion = await _validacion.ValidarAsync(datos, ct);
            errores.AddRange(validacion.Errores);
            if (errores.Count > 0) return Rechazado(errores);

            var n = validacion.Datos;
            var ahora = Ahora();
            var anterior = usuario.PersonaId;

            // El documento ya es de alguien.
            if (validacion.DocumentoExistente is { } existente)
            {
                // De esta empresa, o quien opera es el administrador general (que puede buscar a todos): se busca y se vincula.
                if (existente.TieneVinculoEnEstaEmpresa || quien.EsAdministradorGeneral)
                    return Rechazado(new[] { new ErrorValidacion(nameof(PersonaDatosInput.Documento), MensajeYaRegistrada) });

                // De otra empresa: se verifica antes de vincular nada (D8, como un cliente: primer nombre y primer apellido).
                var deOtra = await _db.Personas
                    .Include(p => p.Vinculos.Where(v => !v.Eliminado))
                    .FirstAsync(p => p.IdPersona == existente.IdPersona, ct);
                if (!CoincideVerificacion(deOtra, n))
                    return Rechazado(new[] { new ErrorValidacion(nameof(PersonaDatosInput.Documento), MensajeNoVerificado) });

                AsegurarRelacionConLaEmpresa(deOtra, idEmpresa, quien.Usuario, ahora);
                usuario.PersonaId = deOtra.IdPersona;
                if (anterior is { } idAnteriorOtra && idAnteriorOtra != deOtra.IdPersona)
                    await CerrarRelacionSiQuedaSinUsuarioAsync(idAnteriorOtra, idEmpresa, usuario.Id, quien.Usuario, ahora, ct);

                return await GuardarAsync(EstadoUsuarioPersona.Vinculado, deOtra.IdPersona, idUsuario, ct);
            }

            // Persona nueva: primero se avisa de las parecidas que la empresa ya tiene.
            var parecidas = await PersonasParecidas.BuscarAsync(
                _db, idEmpresa, n.NombreNormalizado, n.PrimerNombre, n.PrimerApellido, n.FechaNacimiento, excluirIdPersona: null, ct);
            if (parecidas.Count > 0 && !confirmarQueEsOtraPersona)
                return new ResultadoUsuarioPersona(EstadoUsuarioPersona.RequiereConfirmacion, null, Array.Empty<ErrorValidacion>(), parecidas);

            var persona = PersonasConstructor.Nueva(n, quien.Usuario, ahora);
            persona.Vinculos.Add(NuevaRelacionDeUsuario(idEmpresa, quien.Usuario, ahora));
            _db.Personas.Add(persona);

            // Dos guardados en una transacción: la persona necesita su id antes de enlazarla, para que la bitácora del
            // usuario registre el id real y no uno temporal. Si quien llama ya abrió una transacción, se usa la suya.
            await using var transaccion = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync(ct) : null;
            try
            {
                await _db.SaveChangesAsync(ct);

                usuario.PersonaId = persona.IdPersona;
                if (anterior is { } idAnteriorNueva)
                    await CerrarRelacionSiQuedaSinUsuarioAsync(idAnteriorNueva, idEmpresa, usuario.Id, quien.Usuario, ahora, ct);
                await _db.SaveChangesAsync(ct);

                if (transaccion != null) await transaccion.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo crear la persona del usuario {IdUsuario}", idUsuario);
                _db.ChangeTracker.Clear();
                return Rechazado(new[] { new ErrorValidacion(string.Empty, MensajeNoGuardado) });
            }

            return new ResultadoUsuarioPersona(EstadoUsuarioPersona.Creado, persona.IdPersona, Array.Empty<ErrorValidacion>(), Array.Empty<PersonaParecida>());
        }

        public async Task<ResultadoUsuarioPersona> QuitarAsync(QuienOperaUsuarios quien, int idUsuario, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(quien);
            var usuario = await UsuarioVisibleAsync(quien, idUsuario, ct);
            if (usuario == null) return NoEncontrado("No se encontró al usuario.");
            if (usuario.PersonaId is not { } idPersona)
                return new ResultadoUsuarioPersona(EstadoUsuarioPersona.SinCambios, null, Array.Empty<ErrorValidacion>(), Array.Empty<PersonaParecida>());

            usuario.PersonaId = null;
            await CerrarRelacionSiQuedaSinUsuarioAsync(idPersona, EmpresaDeLaPersona(usuario), usuario.Id, quien.Usuario, Ahora(), ct);
            return await GuardarAsync(EstadoUsuarioPersona.Quitado, idPersona, idUsuario, ct);
        }

        // ──────────────────────────────────────────────────────────────────
        //  RELACIÓN DE LA PERSONA CON LA EMPRESA
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Que la persona quede dentro de la empresa: si no tiene ningún vínculo vigente con ella, se reabre su vínculo de
        /// «usuario» (si lo tuvo) o se registra uno nuevo. Devuelve verdadero si cambió algo. Quien llama guarda.
        /// </summary>
        private bool AsegurarRelacionConLaEmpresa(Persona persona, int idEmpresa, string usuario, DateTime ahora)
        {
            var vinculos = persona.Vinculos.Where(v => v.IdEmpresa == idEmpresa && !v.Eliminado).ToList();
            if (vinculos.Any(v => v.Activo && v.FechaFin == null)) return false;

            var deUsuario = vinculos
                .Where(v => v.TipoVinculo == TiposVinculo.Usuario)
                .OrderByDescending(v => v.IdPersonaEmpresa)
                .FirstOrDefault();

            if (deUsuario != null)
            {
                deUsuario.FechaFin = null;
                deUsuario.MotivoFin = null;
                deUsuario.Activo = true;
                deUsuario.ModificadoPor = usuario;
                deUsuario.FechaModificacion = ahora;
            }
            else
            {
                persona.Vinculos.Add(NuevaRelacionDeUsuario(idEmpresa, usuario, ahora));
            }

            if (!persona.Activo)
            {
                persona.Activo = true;
                persona.ModificadoPor = usuario;
                persona.FechaModificacion = ahora;
            }
            return true;
        }

        /// <summary>
        /// Al quitar o cambiar la persona de un usuario: si a la persona no le queda otro usuario en esa empresa, su vínculo
        /// de «usuario» con ella se cierra (fecha de fin hoy). Los vínculos de otro rol (empleado, cliente) no se tocan.
        /// </summary>
        private async Task CerrarRelacionSiQuedaSinUsuarioAsync(
            int idPersona, int idEmpresa, int excluirIdUsuario, string usuario, DateTime ahora, CancellationToken ct)
        {
            var plataforma = _plataforma.IdEmpresaPropia;
            var otroUsuario = await _db.Users.AnyAsync(u => u.PersonaId == idPersona && u.Id != excluirIdUsuario
                                                            && (u.EmpresaId ?? plataforma) == idEmpresa, ct);
            if (otroUsuario) return;

            var vinculo = await _db.PersonaEmpresas
                .Where(v => v.IdPersona == idPersona && v.IdEmpresa == idEmpresa && v.TipoVinculo == TiposVinculo.Usuario
                            && !v.Eliminado && v.FechaFin == null)
                .FirstOrDefaultAsync(ct);
            if (vinculo == null) return;

            vinculo.FechaFin = Hoy();
            vinculo.MotivoFin = "Ya no tiene usuario en esta empresa.";
            vinculo.Activo = false;
            vinculo.ModificadoPor = usuario;
            vinculo.FechaModificacion = ahora;

            // personas.activo queda encendida mientras le quede algún vínculo activo, en esta o en otra empresa.
            var persona = await _db.Personas.FirstAsync(p => p.IdPersona == idPersona, ct);
            var otroActivo = await _db.PersonaEmpresas.AnyAsync(v => v.IdPersona == idPersona && v.IdPersonaEmpresa != vinculo.IdPersonaEmpresa
                                                                     && v.Activo && !v.Eliminado, ct);
            if (persona.Activo && !otroActivo)
            {
                persona.Activo = false;
                persona.ModificadoPor = usuario;
                persona.FechaModificacion = ahora;
            }
        }

        private PersonaEmpresa NuevaRelacionDeUsuario(int idEmpresa, string usuario, DateTime ahora) => new()
        {
            IdEmpresa = idEmpresa,
            TipoVinculo = TiposVinculo.Usuario,
            FechaInicio = Hoy(),
            Activo = true,
            CreadoPor = usuario,
            FechaCreacion = ahora
        };

        // ──────────────────────────────────────────────────────────────────
        //  UTILIDADES
        // ──────────────────────────────────────────────────────────────────

        private static bool PuedeOperar(QuienOperaUsuarios quien) =>
            quien.EsAdministradorGeneral || quien.IdEmpresa is > 0;

        /// <summary>El usuario, si quien opera puede verlo: el administrador general a todos; un administrador de empresa, a los de su empresa.</summary>
        private async Task<User?> UsuarioVisibleAsync(QuienOperaUsuarios quien, int idUsuario, CancellationToken ct)
        {
            if (!PuedeOperar(quien) || idUsuario <= 0) return null;

            var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == idUsuario, ct);
            if (usuario == null) return null;

            return quien.EsAdministradorGeneral || usuario.EmpresaId == quien.IdEmpresa ? usuario : null;
        }

        /// <summary>Verificación D8 del rol de usuario, igual que la de un cliente: primer nombre y primer apellido.</summary>
        private static bool CoincideVerificacion(Persona existente, PersonaDatosNormalizados escritos) =>
            !string.IsNullOrWhiteSpace(existente.PrimerNombre)
            && !string.IsNullOrWhiteSpace(existente.PrimerApellido)
            && NombresPersona.Normalizar(existente.PrimerNombre) == NombresPersona.Normalizar(escritos.PrimerNombre)
            && NombresPersona.Normalizar(existente.PrimerApellido) == NombresPersona.Normalizar(escritos.PrimerApellido);

        private async Task<ResultadoUsuarioPersona> GuardarAsync(
            EstadoUsuarioPersona estado, int? idPersona, int idUsuario, CancellationToken ct)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _log.LogError(ex, "No se pudo guardar la persona del usuario {IdUsuario}", idUsuario);
                _db.ChangeTracker.Clear();
                return Rechazado(new[] { new ErrorValidacion(string.Empty, MensajeNoGuardado) });
            }

            return new ResultadoUsuarioPersona(estado, idPersona, Array.Empty<ErrorValidacion>(), Array.Empty<PersonaParecida>());
        }

        private static ResultadoUsuarioPersona Rechazado(IReadOnlyList<ErrorValidacion> errores) =>
            new(EstadoUsuarioPersona.Rechazado, null, errores, Array.Empty<PersonaParecida>());

        private static ResultadoUsuarioPersona NoEncontrado(string mensaje) =>
            new(EstadoUsuarioPersona.NoEncontrado, null, new[] { new ErrorValidacion(string.Empty, mensaje) }, Array.Empty<PersonaParecida>());
    }
}
