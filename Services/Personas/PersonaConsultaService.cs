using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Auditoria;

namespace eGestion360Web.Services.Personas
{
    public sealed class PersonaConsultaService : IPersonaConsultaService
    {
        /// <summary>
        /// Entidades de la bitácora que son datos de la relación con UNA empresa (laboral o comercial): privadas de
        /// ella. Que alguien sea cliente de una empresa no lo ve otra.
        /// </summary>
        private static readonly string[] EntidadesDeLaRelacion = { "persona_empresa", "empleados", "clientes" };

        /// <summary>
        /// Columnas de dbo.personas que son de la relación con una empresa y no de la persona: las que se retiran en
        /// el script 019 (ya no están en la entidad) y "activo", que depende de los vínculos. La bitácora no se
        /// puede modificar, así que conserva los cambios históricos hechos en ellas; los hechos desde otra empresa
        /// no se muestran.
        /// </summary>
        private static readonly string[] CamposLegadosDeLaRelacion =
        {
            "id_empresa", "cargo", "tarifa_diaria", "moneda_tarifa", "fecha_ingreso", "fecha_baja", "activo",
            "tipo_documento", "documento"
        };

        private const string OtraEmpresa = "otra empresa";

        private readonly ApplicationDbContext _db;
        private readonly PersonaValidacionOptions _opt;

        public PersonaConsultaService(ApplicationDbContext db, IOptions<PersonaValidacionOptions> opciones)
        {
            _db = db;
            _opt = opciones.Value;
        }

        // ──────────────────────────────────────────────────────────────────
        //  LISTADO
        // ──────────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<PersonaFila>> ListarAsync(int idEmpresa, PersonaFiltro filtro, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(filtro);
            if (idEmpresa <= 0) return Array.Empty<PersonaFila>();

            var conductor = _opt.CodigoCargoConductor;
            var consulta = _db.PersonaEmpresas.AsNoTracking()
                .Where(v => v.IdEmpresa == idEmpresa && v.TipoVinculo == TiposVinculo.Empleado && !v.Eliminado
                            && !v.Persona.Eliminado && v.Persona.IdPersonaPrincipal == null);

            var texto = NombresPersona.Limpiar(filtro.Texto);
            if (texto.Length > 0)
            {
                var nombre = NombresPersona.Normalizar(texto);
                var digitos = DocumentosIdentidad.NormalizarNumero(texto);
                consulta = consulta.Where(v =>
                    (nombre != "" && v.Persona.NombreNormalizado != null && v.Persona.NombreNormalizado.Contains(nombre))
                    || (v.Empleado != null && v.Empleado.CodigoInterno != null && v.Empleado.CodigoInterno.Contains(texto))
                    || v.Persona.Documentos.Any(d => !d.Eliminado && d.NumeroNormalizado != null && d.NumeroNormalizado.Contains(digitos)));
            }

            if (!string.IsNullOrWhiteSpace(filtro.Cargo))
                consulta = consulta.Where(v => v.Empleado != null && v.Empleado.Cargo == filtro.Cargo);

            if (filtro.Activo is { } activo)
                consulta = consulta.Where(v => v.Activo == activo);

            switch (filtro.Perfil)
            {
                case FiltroPerfilPersona.NombresPorRevisar:
                    consulta = consulta.Where(v => v.Persona.PrimerNombre == null || v.Persona.PrimerApellido == null);
                    break;
                case FiltroPerfilPersona.Incompleto:
                    consulta = consulta.Where(v =>
                        v.Persona.PrimerNombre == null || v.Persona.PrimerApellido == null
                        || v.Persona.FechaNacimiento == null
                        || v.Persona.EstadoIdentidad != EstadosIdentidad.Verificada
                        || (v.Empleado != null && v.Empleado.Cargo == conductor
                            && (v.Persona.LicenciaTipo == null || v.Persona.LicenciaNumero == null)));
                    break;
            }

            var filas = await consulta
                .OrderBy(v => v.Persona.Apellidos).ThenBy(v => v.Persona.Nombres).ThenBy(v => v.Persona.IdPersona)
                .Select(v => new
                {
                    v.Persona.IdPersona,
                    v.Persona.PrimerNombre, v.Persona.SegundoNombre, v.Persona.PrimerApellido, v.Persona.SegundoApellido,
                    v.Persona.Nombres, v.Persona.Apellidos,
                    v.Persona.Telefono, v.Persona.FechaNacimiento, v.Persona.EstadoIdentidad,
                    v.Persona.LicenciaTipo, v.Persona.LicenciaNumero,
                    Documento = v.Persona.Documentos
                        .Where(d => !d.Eliminado)
                        .OrderByDescending(d => d.EsPrincipal).ThenBy(d => d.IdPersonaDocumento)
                        .Select(d => new { d.TipoDocumento, d.Numero })
                        .FirstOrDefault(),
                    CodigoInterno = v.Empleado != null ? v.Empleado.CodigoInterno : null,
                    Cargo = v.Empleado != null ? v.Empleado.Cargo : null,
                    TarifaDiaria = v.Empleado != null ? v.Empleado.TarifaDiaria : null,
                    Moneda = v.Empleado != null ? v.Empleado.MonedaTarifa : null,
                    v.FechaInicio, v.FechaFin, v.Activo
                })
                .ToListAsync(ct);

            return filas.Select(f =>
            {
                var separados = !string.IsNullOrWhiteSpace(f.PrimerNombre) && !string.IsNullOrWhiteSpace(f.PrimerApellido);

                var faltantes = new List<string>();
                if (!separados) faltantes.Add("Separar el nombre y los apellidos");
                if (f.FechaNacimiento == null) faltantes.Add("Fecha de nacimiento");
                if (f.EstadoIdentidad != EstadosIdentidad.Verificada) faltantes.Add("Documento de identidad");
                if (string.Equals(f.Cargo, conductor, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(f.LicenciaTipo) || string.IsNullOrWhiteSpace(f.LicenciaNumero)))
                    faltantes.Add("Licencia de conducir");

                var apellidos = separados ? NombresPersona.ComponerApellidos(f.PrimerApellido, f.SegundoApellido) : f.Apellidos;
                var nombres = separados ? NombresPersona.ComponerNombres(f.PrimerNombre, f.SegundoNombre) : f.Nombres;

                return new PersonaFila(
                    f.IdPersona, $"{apellidos}, {nombres}", separados,
                    f.Documento?.TipoDocumento, f.Documento?.Numero, f.CodigoInterno, f.Cargo, f.Telefono,
                    f.TarifaDiaria, f.Moneda, f.FechaInicio, f.FechaFin, f.Activo, f.EstadoIdentidad, faltantes);
            }).ToList();
        }

        // ──────────────────────────────────────────────────────────────────
        //  EDICIÓN
        // ──────────────────────────────────────────────────────────────────

        public async Task<PersonaEdicion?> ObtenerParaEdicionAsync(int idEmpresa, int idPersona, CancellationToken ct = default)
        {
            if (idEmpresa <= 0 || idPersona <= 0) return null;

            var persona = await _db.Personas.AsNoTracking()
                .Include(p => p.Documentos)
                .Include(p => p.Vinculos.Where(v => v.IdEmpresa == idEmpresa && !v.Eliminado && v.TipoVinculo == TiposVinculo.Empleado))
                    .ThenInclude(v => v.Empleado)
                .FirstOrDefaultAsync(p => p.IdPersona == idPersona && !p.Eliminado && p.IdPersonaPrincipal == null
                                          && p.Vinculos.Any(v => v.IdEmpresa == idEmpresa && !v.Eliminado
                                                                 && v.TipoVinculo == TiposVinculo.Empleado), ct);
            if (persona == null) return null;

            // El vínculo vigente primero; si no hay, el más reciente.
            var vinculo = persona.Vinculos
                .OrderBy(v => v.FechaFin == null ? 0 : 1).ThenByDescending(v => v.IdPersonaEmpresa)
                .First();
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

            var datos = new PersonaDatosInput
            {
                IdEmpresa = idEmpresa,
                IdPersona = persona.IdPersona,
                PrimerNombre = persona.PrimerNombre,
                SegundoNombre = persona.SegundoNombre,
                PrimerApellido = persona.PrimerApellido,
                SegundoApellido = persona.SegundoApellido,
                TipoDocumento = documento?.TipoDocumento,
                Documento = documento?.Numero,
                PaisEmisor = documento?.PaisEmisor,
                FechaNacimiento = persona.FechaNacimiento,
                Sexo = persona.Sexo,
                EstadoCivil = persona.EstadoCivil,
                TipoSangre = persona.TipoSangre,
                PaisNacionalidad = persona.PaisNacionalidad,
                IdMunicipioNacimiento = persona.IdMunicipioNacimiento,
                IdMunicipioResidencia = persona.IdMunicipioResidencia,
                DireccionResidencia = persona.DireccionResidencia,
                Telefono = persona.Telefono,
                TelefonoSecundario = persona.TelefonoSecundario,
                Email = persona.Email,
                ContactoEmergenciaNombre = persona.ContactoEmergenciaNombre,
                ContactoEmergenciaTelefono = persona.ContactoEmergenciaTelefono,
                ContactoEmergenciaParentesco = persona.ContactoEmergenciaParentesco,
                LicenciaTipo = persona.LicenciaTipo,
                LicenciaNumero = persona.LicenciaNumero,
                LicenciaVencimiento = persona.LicenciaVencimiento,
                Empleado = new EmpleadoDatosInput
                {
                    CodigoInterno = vinculo.Empleado?.CodigoInterno,
                    Cargo = vinculo.Empleado?.Cargo,
                    FechaIngreso = vinculo.FechaInicio,
                    FechaBaja = vinculo.FechaFin,
                    TarifaDiaria = vinculo.Empleado?.TarifaDiaria,
                    MonedaTarifa = vinculo.Empleado?.MonedaTarifa
                }
            };

            return new PersonaEdicion(
                datos,
                persona.IdMunicipioNacimiento is { } mn && departamentos.TryGetValue(mn, out var dn) ? dn : null,
                persona.IdMunicipioResidencia is { } mr && departamentos.TryGetValue(mr, out var dr) ? dr : null,
                persona.TokenConcurrencia,
                persona.EstadoIdentidad,
                persona.NombreCompleto,
                persona.NombresSeparados,
                vinculo.Activo,
                persona.CreadoPor,
                persona.FechaCreacion,
                persona.ModificadoPor,
                persona.FechaModificacion);
        }

        // ──────────────────────────────────────────────────────────────────
        //  HISTORIAL
        // ──────────────────────────────────────────────────────────────────

        public async Task<HistorialPersona?> HistorialAsync(int idEmpresa, int idPersona, int maximo = 300, CancellationToken ct = default)
        {
            if (idEmpresa <= 0 || idPersona <= 0) return null;
            maximo = Math.Clamp(maximo, 1, 1000);

            var persona = await _db.Personas.AsNoTracking()
                .Where(p => p.IdPersona == idPersona && p.Vinculos.Any(v => v.IdEmpresa == idEmpresa && !v.Eliminado))
                .Select(p => new { p.Nombres, p.Apellidos })
                .FirstOrDefaultAsync(ct);
            if (persona == null) return null;

            // De otras empresas, los datos de la relación laboral no se ven.
            var cambios = await _db.BitacoraCambios.AsNoTracking()
                .Where(b => b.IdPersona == idPersona
                            && (b.IdEmpresa == idEmpresa || b.IdEmpresa == null || !EntidadesDeLaRelacion.Contains(b.Entidad)))
                .OrderByDescending(b => b.FechaHora).ThenByDescending(b => b.IdBitacora)
                .Take(maximo * 2 + 1)
                .ToListAsync(ct);

            var filas = new List<HistorialFila>();
            foreach (var b in cambios)
            {
                var deOtra = b.IdEmpresa != null && b.IdEmpresa != idEmpresa;

                // Los campos de la relación con una empresa que la bitácora guardó en "personas" tampoco se muestran si son de otra empresa.
                if (deOtra && b.Entidad == "personas" && b.Campo != null && CamposLegadosDeLaRelacion.Contains(b.Campo))
                    continue;

                var esFoto = b.Campo == null;
                filas.Add(new HistorialFila(
                    b.FechaHora.AddHours(-6),
                    EtiquetasBitacora.Entidad(b.Entidad),
                    EtiquetasBitacora.Operacion(b.Operacion),
                    esFoto ? null : EtiquetasBitacora.Campo(b.Campo),
                    esFoto ? null : EtiquetasBitacora.Valor(b.ValorAnterior),
                    esFoto ? null : EtiquetasBitacora.Valor(b.ValorNuevo),
                    // La foto de un alta o baja de otra empresa lleva datos de su relación laboral: no se detalla.
                    esFoto && !deOtra ? EtiquetasBitacora.Resumir(b.ValorNuevo ?? b.ValorAnterior) : null,
                    deOtra ? OtraEmpresa : b.Usuario,
                    b.IdTransaccion,
                    deOtra));

                if (filas.Count > maximo) break;
            }

            var truncado = filas.Count > maximo;
            if (truncado) filas.RemoveAt(filas.Count - 1);

            return new HistorialPersona($"{persona.Nombres} {persona.Apellidos}".Trim(), filas, truncado);
        }

        // ──────────────────────────────────────────────────────────────────
        //  CATÁLOGOS
        // ──────────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<OpcionCatalogo>> CargosAsync(int idEmpresa, CancellationToken ct = default) =>
            await _db.Cargos.AsNoTracking()
                .Where(c => c.IdEmpresa == idEmpresa && c.Activo && !c.Eliminado)
                .OrderBy(c => c.Nombre)
                .Select(c => new OpcionCatalogo(c.Codigo, c.Nombre))
                .ToListAsync(ct);

        public async Task<IReadOnlyList<OpcionPersona>> PersonalParaSeleccionAsync(
            int idEmpresa, string? cargo = null, CancellationToken ct = default)
        {
            if (idEmpresa <= 0) return Array.Empty<OpcionPersona>();

            // Empleados activos de ESTA empresa: ni personas fusionadas o eliminadas, ni vínculos de otro rol.
            var consulta = _db.PersonaEmpresas.AsNoTracking()
                .Where(v => v.IdEmpresa == idEmpresa && v.TipoVinculo == TiposVinculo.Empleado
                            && v.Activo && !v.Eliminado
                            && !v.Persona.Eliminado && v.Persona.IdPersonaPrincipal == null);

            if (!string.IsNullOrWhiteSpace(cargo))
            {
                consulta = consulta.Where(v => v.Empleado != null && v.Empleado.Cargo == cargo
                                               && v.Empleado.Activo && !v.Empleado.Eliminado);
            }

            return await consulta
                .OrderBy(v => v.Persona.Apellidos).ThenBy(v => v.Persona.Nombres)
                .Select(v => new OpcionPersona(v.IdPersona, (v.Persona.Nombres + " " + v.Persona.Apellidos).Trim()))
                .ToListAsync(ct);
        }

        public async Task<PersonaCatalogos> CatalogosAsync(int idEmpresa, CancellationToken ct = default)
        {
            var cargos = await CargosAsync(idEmpresa, ct);

            var tipos = await _db.CatalogoTiposDocumento.AsNoTracking()
                .Where(t => t.Activo)
                .OrderBy(t => t.Codigo == DocumentosIdentidad.Dni ? 0 : 1).ThenBy(t => t.Nombre)
                .Select(t => new { t.Codigo, t.Nombre })
                .ToListAsync(ct);

            var licencias = await _db.CatalogoTiposLicencia.AsNoTracking()
                .Where(t => t.Activo)
                .OrderBy(t => t.Codigo)
                .Select(t => new OpcionCatalogo(t.Codigo, t.Codigo + " — " + t.Nombre))
                .ToListAsync(ct);

            var paises = await _db.Paises.AsNoTracking()
                .Where(p => p.Activo)
                .OrderBy(p => p.CodigoIso == _opt.PaisPorDefecto ? 0 : 1).ThenBy(p => p.Nombre)
                .Select(p => new OpcionCatalogo(p.CodigoIso, p.Nombre))
                .ToListAsync(ct);

            // Monedas: la de la empresa, el lempira y el dólar. El resto del catálogo no hace falta para una tarifa diaria.
            var monedaEmpresa = await _db.Empresas.AsNoTracking()
                .Where(e => e.IdEmpresa == idEmpresa).Select(e => e.MonedaIso).FirstOrDefaultAsync(ct);
            var codigos = new[] { monedaEmpresa, _opt.MonedaPorDefecto, "USD" }
                .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            var monedas = await _db.Monedas.AsNoTracking()
                .Where(m => m.Activo && codigos.Contains(m.CodigoIso))
                .OrderBy(m => m.CodigoIso)
                .Select(m => new OpcionCatalogo(m.CodigoIso, m.CodigoIso + " — " + m.Nombre))
                .ToListAsync(ct);

            var departamentos = await _db.CatalogoDepartamentos.AsNoTracking()
                .Where(d => d.Activo && d.PaisIso == _opt.PaisPorDefecto)
                .OrderBy(d => d.Codigo)
                .Select(d => new OpcionDepartamento(d.IdDepartamento, d.Nombre))
                .ToListAsync(ct);

            var municipios = await _db.CatalogoMunicipios.AsNoTracking()
                .Where(m => m.Activo && m.Departamento.PaisIso == _opt.PaisPorDefecto)
                .OrderBy(m => m.Nombre)
                .Select(m => new OpcionMunicipio(m.IdMunicipio, m.IdDepartamento, m.Nombre))
                .ToListAsync(ct);

            return new PersonaCatalogos(
                cargos,
                tipos.Select(t => new OpcionCatalogo(t.Codigo, t.Codigo is DocumentosIdentidad.Dni or DocumentosIdentidad.Rtn ? t.Codigo : t.Nombre)).ToList(),
                licencias, paises, monedas, departamentos, municipios);
        }
    }
}
