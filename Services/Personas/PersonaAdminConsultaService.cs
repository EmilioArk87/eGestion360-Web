using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Auditoria;

namespace eGestion360Web.Services.Personas
{
    public sealed class PersonaAdminConsultaService : IPersonaAdminConsultaService
    {
        private const int TamanoMaximo = 200;

        /// <summary>Columnas de la bitácora con lo que paga cada empresa: el administrador ve que cambiaron, no el monto.</summary>
        private static readonly IReadOnlySet<string> ColumnasDeSalario =
            new HashSet<string>(StringComparer.Ordinal) { "tarifa_diaria", "moneda_tarifa" };

        private const string TextoOculto = "(no se muestra)";

        private static string? Oculto(string? valor) => string.IsNullOrEmpty(valor) ? null : TextoOculto;

        private readonly ApplicationDbContext _db;
        private readonly PersonaValidacionOptions _opt;

        public PersonaAdminConsultaService(ApplicationDbContext db, IOptions<PersonaValidacionOptions> opciones)
        {
            _db = db;
            _opt = opciones.Value;
        }

        // ──────────────────────────────────────────────────────────────────
        //  LISTADO
        // ──────────────────────────────────────────────────────────────────

        public async Task<PersonaAdminPagina> ListarAsync(PersonaAdminFiltro filtro, int pagina, int tamano, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(filtro);
            tamano = Math.Clamp(tamano, 1, TamanoMaximo);
            pagina = Math.Max(1, pagina);

            var conductor = _opt.CodigoCargoConductor;
            var consulta = _db.Personas.AsNoTracking().Where(p => !p.Eliminado && p.IdPersonaPrincipal == null);

            var texto = NombresPersona.Limpiar(filtro.Texto);
            if (texto.Length > 0)
            {
                var nombre = NombresPersona.Normalizar(texto);
                var digitos = DocumentosIdentidad.NormalizarNumero(texto);
                consulta = consulta.Where(p =>
                    (nombre != "" && p.NombreNormalizado != null && p.NombreNormalizado.Contains(nombre))
                    || (digitos != "" && p.Documentos.Any(d => !d.Eliminado && d.NumeroNormalizado != null && d.NumeroNormalizado.Contains(digitos)))
                    || p.Vinculos.Any(v => !v.Eliminado && v.Empleado != null && v.Empleado.CodigoInterno != null && v.Empleado.CodigoInterno.Contains(texto))
                    || _db.Clientes.Any(c => !c.Eliminado && c.Vinculo != null && c.Vinculo.IdPersona == p.IdPersona && c.Codigo.Contains(texto)));
            }

            // Empresa y rol se piden juntos: «cliente en la empresa X» no es «cliente en otra y algo en X».
            var idEmpresa = filtro.IdEmpresa is > 0 ? filtro.IdEmpresa : null;
            var tipo = string.IsNullOrWhiteSpace(filtro.TipoVinculo) ? null : filtro.TipoVinculo.Trim().ToLowerInvariant();
            if (tipo != null && !TiposVinculo.Todos.Contains(tipo))
                return new PersonaAdminPagina(Array.Empty<PersonaAdminFila>(), 0, pagina, tamano);
            if (idEmpresa != null || tipo != null)
            {
                consulta = consulta.Where(p => p.Vinculos.Any(v => !v.Eliminado
                    && (idEmpresa == null || v.IdEmpresa == idEmpresa)
                    && (tipo == null || v.TipoVinculo == tipo)));
            }

            switch (filtro.Estado)
            {
                case EstadoVinculoFiltro.ConVinculoVigente:
                    consulta = consulta.Where(p => p.Vinculos.Any(v => !v.Eliminado && v.Activo && v.FechaFin == null));
                    break;
                case EstadoVinculoFiltro.SinVinculoVigente:
                    consulta = consulta.Where(p => !p.Vinculos.Any(v => !v.Eliminado && v.Activo && v.FechaFin == null));
                    break;
            }

            switch (filtro.Perfil)
            {
                case FiltroPerfilPersona.NombresPorRevisar:
                    consulta = consulta.Where(p => p.PrimerNombre == null || p.PrimerApellido == null);
                    break;
                case FiltroPerfilPersona.Incompleto:
                    consulta = consulta.Where(p =>
                        p.PrimerNombre == null || p.PrimerApellido == null
                        || p.FechaNacimiento == null
                        || p.EstadoIdentidad != EstadosIdentidad.Verificada
                        || ((p.LicenciaTipo == null || p.LicenciaNumero == null)
                            && p.Vinculos.Any(v => !v.Eliminado && v.Empleado != null && v.Empleado.Cargo == conductor)));
                    break;
            }

            var total = await consulta.CountAsync(ct);

            var personas = await consulta
                .OrderBy(p => p.Apellidos).ThenBy(p => p.Nombres).ThenBy(p => p.IdPersona)
                .Skip((pagina - 1) * tamano).Take(tamano)
                .Select(p => new
                {
                    p.IdPersona,
                    p.PrimerNombre, p.SegundoNombre, p.PrimerApellido, p.SegundoApellido,
                    p.Nombres, p.Apellidos,
                    p.Telefono, p.FechaNacimiento, p.EstadoIdentidad,
                    p.LicenciaTipo, p.LicenciaNumero,
                    Documento = p.Documentos
                        .Where(d => !d.Eliminado)
                        .OrderByDescending(d => d.EsPrincipal).ThenBy(d => d.IdPersonaDocumento)
                        .Select(d => new { d.TipoDocumento, d.Numero })
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var vinculos = await LeerVinculosAsync(personas.Select(p => p.IdPersona).ToList(), ct);

            var filas = personas.Select(p =>
            {
                var propios = vinculos.TryGetValue(p.IdPersona, out var lista) ? lista : new List<VinculoLeido>();
                var separados = !string.IsNullOrWhiteSpace(p.PrimerNombre) && !string.IsNullOrWhiteSpace(p.PrimerApellido);

                var faltantes = new List<string>();
                if (!separados) faltantes.Add("Separar el nombre y los apellidos");
                if (p.FechaNacimiento == null) faltantes.Add("Fecha de nacimiento");
                if (p.EstadoIdentidad != EstadosIdentidad.Verificada) faltantes.Add("Documento de identidad");
                var esConductor = propios.Any(v => string.Equals(v.Cargo, conductor, StringComparison.OrdinalIgnoreCase));
                if (esConductor && (string.IsNullOrWhiteSpace(p.LicenciaTipo) || string.IsNullOrWhiteSpace(p.LicenciaNumero)))
                    faltantes.Add("Licencia de conducir");

                var apellidos = separados ? NombresPersona.ComponerApellidos(p.PrimerApellido, p.SegundoApellido) : p.Apellidos;
                var nombres = separados ? NombresPersona.ComponerNombres(p.PrimerNombre, p.SegundoNombre) : p.Nombres;

                return new PersonaAdminFila(
                    p.IdPersona, $"{apellidos}, {nombres}", separados,
                    p.Documento?.TipoDocumento, p.Documento?.Numero, p.Telefono, p.EstadoIdentidad,
                    propios.Select(v => v.Vinculo).ToList(), faltantes);
            }).ToList();

            return new PersonaAdminPagina(filas, total, pagina, tamano);
        }

        public async Task<IReadOnlyList<OpcionCatalogo>> EmpresasAsync(CancellationToken ct = default)
        {
            var empresas = await _db.Empresas.AsNoTracking()
                .Where(e => !e.Eliminado)
                .Select(e => new { e.IdEmpresa, e.RazonSocial, e.NombreComercial })
                .ToListAsync(ct);

            return empresas
                .Select(e => new OpcionCatalogo(e.IdEmpresa.ToString(), NombreDeEmpresa(e.NombreComercial, e.RazonSocial)))
                .OrderBy(o => o.Texto, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        // ──────────────────────────────────────────────────────────────────
        //  DETALLE
        // ──────────────────────────────────────────────────────────────────

        public async Task<PersonaAdminDetalle?> ObtenerAsync(int idPersona, CancellationToken ct = default)
        {
            if (idPersona <= 0) return null;

            var persona = await _db.Personas.AsNoTracking()
                .Include(p => p.Documentos)
                .FirstOrDefaultAsync(p => p.IdPersona == idPersona && !p.Eliminado && p.IdPersonaPrincipal == null, ct);
            if (persona == null) return null;

            var vinculos = await LeerVinculosAsync(new[] { idPersona }, ct);

            return new PersonaAdminDetalle(
                persona.IdPersona,
                persona.NombreCompleto,
                persona.NombresSeparados,
                persona.EstadoIdentidad,
                persona.Documentos
                    .Where(d => !d.Eliminado)
                    .OrderByDescending(d => d.EsPrincipal).ThenBy(d => d.IdPersonaDocumento)
                    .Select(d => new PersonaAdminDocumento(d.TipoDocumento, d.Numero, d.EsPrincipal))
                    .ToList(),
                vinculos.TryGetValue(idPersona, out var lista) ? lista.Select(v => v.Vinculo).ToList() : new List<PersonaAdminVinculo>(),
                persona.CreadoPor, persona.FechaCreacion, persona.ModificadoPor, persona.FechaModificacion);
        }

        // ──────────────────────────────────────────────────────────────────
        //  EDICIÓN
        // ──────────────────────────────────────────────────────────────────

        public async Task<PersonaEdicion?> ObtenerParaEdicionAsync(int idPersona, CancellationToken ct = default)
        {
            if (idPersona <= 0) return null;

            var persona = await _db.Personas.AsNoTracking()
                .Include(p => p.Documentos)
                .FirstOrDefaultAsync(p => p.IdPersona == idPersona && !p.Eliminado && p.IdPersonaPrincipal == null, ct);
            if (persona == null) return null;

            var documento = persona.Documentos
                .Where(d => !d.Eliminado)
                .OrderByDescending(d => d.EsPrincipal).ThenBy(d => d.IdPersonaDocumento)
                .FirstOrDefault();

            var ids = new[] { persona.IdMunicipioNacimiento, persona.IdMunicipioResidencia }
                .Where(id => id != null).Select(id => id!.Value).ToList();
            var departamentos = ids.Count == 0
                ? new Dictionary<int, int>()
                : await _db.CatalogoMunicipios.AsNoTracking()
                    .Where(m => ids.Contains(m.IdMunicipio))
                    .ToDictionaryAsync(m => m.IdMunicipio, m => m.IdDepartamento, ct);

            return new PersonaEdicion(
                PersonaConsultaService.DatosPersonales(persona, documento, 0),
                persona.IdMunicipioNacimiento is { } mn && departamentos.TryGetValue(mn, out var dn) ? dn : null,
                persona.IdMunicipioResidencia is { } mr && departamentos.TryGetValue(mr, out var dr) ? dr : null,
                persona.TokenConcurrencia,
                persona.EstadoIdentidad,
                persona.NombreCompleto,
                persona.NombresSeparados,
                persona.Activo,
                persona.CreadoPor,
                persona.FechaCreacion,
                persona.ModificadoPor,
                persona.FechaModificacion);
        }

        // ──────────────────────────────────────────────────────────────────
        //  HISTORIAL
        // ──────────────────────────────────────────────────────────────────

        public async Task<HistorialPersona?> HistorialAsync(int idPersona, int maximo = 300, CancellationToken ct = default)
        {
            if (idPersona <= 0) return null;
            maximo = Math.Clamp(maximo, 1, 1000);

            var persona = await _db.Personas.AsNoTracking()
                .Where(p => p.IdPersona == idPersona && !p.Eliminado)
                .Select(p => new { p.Nombres, p.Apellidos })
                .FirstOrDefaultAsync(ct);
            if (persona == null) return null;

            var cambios = await _db.BitacoraCambios.AsNoTracking()
                .Where(b => b.IdPersona == idPersona)
                .OrderByDescending(b => b.FechaHora).ThenByDescending(b => b.IdBitacora)
                .Take(maximo + 1)
                .ToListAsync(ct);

            var idsEmpresa = cambios.Where(b => b.IdEmpresa != null).Select(b => b.IdEmpresa!.Value).Distinct().ToList();
            var empresas = idsEmpresa.Count == 0
                ? new Dictionary<int, string>()
                : (await _db.Empresas.AsNoTracking()
                    .Where(e => idsEmpresa.Contains(e.IdEmpresa))
                    .Select(e => new { e.IdEmpresa, e.RazonSocial, e.NombreComercial })
                    .ToListAsync(ct))
                  .ToDictionary(e => e.IdEmpresa, e => NombreDeEmpresa(e.NombreComercial, e.RazonSocial));

            var filas = cambios.Select(b =>
            {
                var esFoto = b.Campo == null;

                // Lo que paga cada empresa no se muestra aquí: el cambio se ve, pero no el monto.
                var esSalario = b.Campo != null && ColumnasDeSalario.Contains(b.Campo);

                return new HistorialFila(
                    b.FechaHora.AddHours(-6),
                    EtiquetasBitacora.Entidad(b.Entidad),
                    EtiquetasBitacora.Operacion(b.Operacion),
                    esFoto ? null : EtiquetasBitacora.Campo(b.Campo),
                    esFoto ? null : esSalario ? Oculto(b.ValorAnterior) : EtiquetasBitacora.Valor(b.ValorAnterior),
                    esFoto ? null : esSalario ? Oculto(b.ValorNuevo) : EtiquetasBitacora.Valor(b.ValorNuevo),
                    esFoto ? EtiquetasBitacora.Resumir(b.ValorNuevo ?? b.ValorAnterior, ColumnasDeSalario) : null,
                    b.Usuario,
                    b.IdTransaccion,
                    DeOtraEmpresa: false,
                    Empresa: b.IdEmpresa is { } id && empresas.TryGetValue(id, out var nombre) ? nombre : null);
            }).ToList();

            var truncado = filas.Count > maximo;
            if (truncado) filas.RemoveAt(filas.Count - 1);

            return new HistorialPersona($"{persona.Nombres} {persona.Apellidos}".Trim(), filas, truncado);
        }

        // ──────────────────────────────────────────────────────────────────
        //  VÍNCULOS
        // ──────────────────────────────────────────────────────────────────

        private sealed record VinculoLeido(PersonaAdminVinculo Vinculo, string? Cargo);

        /// <summary>Los vínculos no eliminados de las personas dadas, con la empresa y el detalle de su rol; los vigentes primero.</summary>
        private async Task<Dictionary<int, List<VinculoLeido>>> LeerVinculosAsync(IReadOnlyCollection<int> idsPersona, CancellationToken ct)
        {
            if (idsPersona.Count == 0) return new Dictionary<int, List<VinculoLeido>>();

            var filas = await _db.PersonaEmpresas.AsNoTracking()
                .Where(v => idsPersona.Contains(v.IdPersona) && !v.Eliminado)
                .Select(v => new
                {
                    v.IdPersonaEmpresa, v.IdPersona, v.IdEmpresa,
                    EmpresaRazonSocial = v.Empresa.RazonSocial, EmpresaComercial = v.Empresa.NombreComercial,
                    v.TipoVinculo, v.Activo, v.FechaInicio, v.FechaFin,
                    Cargo = v.Empleado != null ? v.Empleado.Cargo : null,
                    Codigo = v.Empleado != null ? v.Empleado.CodigoInterno : null
                })
                .ToListAsync(ct);

            var idsCliente = filas.Where(f => f.TipoVinculo == TiposVinculo.Cliente).Select(f => f.IdPersonaEmpresa).ToList();
            var codigosCliente = idsCliente.Count == 0
                ? new Dictionary<int, string>()
                : (await _db.Clientes.AsNoTracking()
                    .Where(c => !c.Eliminado && c.IdPersonaEmpresa != null && idsCliente.Contains(c.IdPersonaEmpresa.Value))
                    .Select(c => new { Id = c.IdPersonaEmpresa!.Value, c.Codigo })
                    .ToListAsync(ct))
                  .GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Codigo);

            return filas
                .Select(f =>
                {
                    string? detalle = null;
                    if (f.TipoVinculo == TiposVinculo.Empleado)
                    {
                        var partes = new List<string>();
                        if (!string.IsNullOrWhiteSpace(f.Cargo)) partes.Add(f.Cargo!);
                        if (!string.IsNullOrWhiteSpace(f.Codigo)) partes.Add("cód. " + f.Codigo);
                        detalle = partes.Count > 0 ? string.Join(" · ", partes) : null;
                    }
                    else if (f.TipoVinculo == TiposVinculo.Cliente && codigosCliente.TryGetValue(f.IdPersonaEmpresa, out var codigo))
                    {
                        detalle = "cód. " + codigo;
                    }

                    var vinculo = new PersonaAdminVinculo(
                        f.IdPersonaEmpresa, f.IdEmpresa, NombreDeEmpresa(f.EmpresaComercial, f.EmpresaRazonSocial),
                        f.TipoVinculo, f.Activo, f.FechaInicio, f.FechaFin, detalle);
                    return (f.IdPersona, Leido: new VinculoLeido(vinculo, f.Cargo));
                })
                .GroupBy(x => x.IdPersona)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Leido)
                          .OrderByDescending(x => x.Vinculo.Vigente)
                          .ThenBy(x => x.Vinculo.Empresa, StringComparer.CurrentCultureIgnoreCase)
                          .ThenBy(x => x.Vinculo.TipoVinculo, StringComparer.Ordinal)
                          .ThenByDescending(x => x.Vinculo.IdPersonaEmpresa)
                          .ToList());
        }

        private static string NombreDeEmpresa(string? comercial, string razonSocial) =>
            string.IsNullOrWhiteSpace(comercial) ? razonSocial : comercial;
    }
}
