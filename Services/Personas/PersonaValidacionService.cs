using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;

namespace eGestion360Web.Services.Personas
{
    public sealed class PersonaValidacionService : IPersonaValidacionService
    {
        private static readonly string[] EstadosCiviles = { "soltero", "casado", "union_libre", "divorciado", "viudo" };
        private static readonly string[] TiposSangre = { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };

        private static readonly Regex FormatoCodigoInterno = new(
            "^[A-Za-z0-9]{1,30}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static readonly Regex FormatoNumeroLicencia = new(
            "^[A-Za-z0-9-]{1,30}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private readonly ApplicationDbContext _db;
        private readonly PersonaValidacionOptions _opt;
        private readonly TimeProvider _tiempo;

        public PersonaValidacionService(ApplicationDbContext db, IOptions<PersonaValidacionOptions> opciones, TimeProvider tiempo)
        {
            _db = db;
            _opt = opciones.Value;
            _tiempo = tiempo;
        }

        public async Task<ResultadoValidacionPersona> ValidarAsync(PersonaDatosInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            var v = new Acumulador();
            var d = new PersonaDatosNormalizados();
            var hoy = Hoy();
            var esAlta = input.Modo == ModoValidacionPersona.Alta;
            var conRol = input.Empleado != null;
            var esConductor = conRol && string.Equals(
                NombresPersona.Limpiar(input.Empleado!.Cargo), _opt.CodigoCargoConductor, StringComparison.OrdinalIgnoreCase);

            if (input.IdEmpresa <= 0 && input.Modo != ModoValidacionPersona.EdicionAdministrador)
                v.Error(nameof(PersonaDatosInput.IdEmpresa), "Empresa requerida.");

            ValidarNombres(input, d, v);
            var existente = await ValidarDocumentoAsync(input, d, v, hoy, ct);
            await ValidarDatosPersonalesAsync(input, d, v, esAlta, conRol, esConductor, hoy, ct);
            await ValidarContactoAsync(input, d, v, ct);
            await ValidarLicenciaAsync(input, d, v, esAlta, esConductor, hoy, ct);
            await ValidarEmpleadoAsync(input, d, v, esAlta, ct);

            // Un empleado nuevo necesita un documento o, mientras lo consigue, su código de empleado.
            if (esAlta && conRol
                && string.IsNullOrWhiteSpace(input.Documento)
                && string.IsNullOrWhiteSpace(input.Empleado!.CodigoInterno))
            {
                v.Error(nameof(PersonaDatosInput.Documento),
                    "Escribe el documento de identidad o, mientras tanto, el código de empleado.");
            }

            return new ResultadoValidacionPersona(v.Errores.Count == 0, v.Errores, v.Advertencias, d, existente);
        }

        // ──────────────────────────────────────────────────────────────────
        //  NOMBRES
        // ──────────────────────────────────────────────────────────────────

        private static void ValidarNombres(PersonaDatosInput i, PersonaDatosNormalizados d, Acumulador v)
        {
            d.PrimerNombre = ParteDeNombre(v, nameof(PersonaDatosInput.PrimerNombre), "primer nombre", i.PrimerNombre, obligatorio: true) ?? string.Empty;
            d.SegundoNombre = ParteDeNombre(v, nameof(PersonaDatosInput.SegundoNombre), "segundo nombre", i.SegundoNombre, obligatorio: false);
            d.PrimerApellido = ParteDeNombre(v, nameof(PersonaDatosInput.PrimerApellido), "primer apellido", i.PrimerApellido, obligatorio: true) ?? string.Empty;
            d.SegundoApellido = ParteDeNombre(v, nameof(PersonaDatosInput.SegundoApellido), "segundo apellido", i.SegundoApellido, obligatorio: false);

            d.Nombres = NombresPersona.ComponerNombres(d.PrimerNombre, d.SegundoNombre);
            d.Apellidos = NombresPersona.ComponerApellidos(d.PrimerApellido, d.SegundoApellido);
            d.NombreNormalizado = NombresPersona.Normalizar(d.PrimerNombre, d.SegundoNombre, d.PrimerApellido, d.SegundoApellido);

            // Mientras existan las columnas legadas personas.nombres y personas.apellidos (100 caracteres)
            if (d.Nombres.Length > 100)
                v.Error(nameof(PersonaDatosInput.PrimerNombre), "El primer y el segundo nombre juntos no pueden pasar de 100 caracteres.");
            if (d.Apellidos.Length > 100)
                v.Error(nameof(PersonaDatosInput.PrimerApellido), "El primer y el segundo apellido juntos no pueden pasar de 100 caracteres.");
        }

        private static string? ParteDeNombre(Acumulador v, string campo, string etiqueta, string? valor, bool obligatorio)
        {
            var limpio = NombresPersona.Limpiar(valor);
            if (limpio.Length == 0)
            {
                if (obligatorio) v.Error(campo, $"El {etiqueta} es obligatorio.");
                return null;
            }

            if (!NombresPersona.EsValido(limpio))
                v.Error(campo, $"El {etiqueta} solo admite letras, espacios, apóstrofo y guion.");
            else if (limpio.Length > 50)
                v.Error(campo, $"El {etiqueta} no puede pasar de 50 caracteres.");

            return NombresPersona.AFormatoPropio(limpio);
        }

        // ──────────────────────────────────────────────────────────────────
        //  DOCUMENTO DE IDENTIDAD
        // ──────────────────────────────────────────────────────────────────

        private async Task<DocumentoExistente?> ValidarDocumentoAsync(
            PersonaDatosInput i, PersonaDatosNormalizados d, Acumulador v, DateOnly hoy, CancellationToken ct)
        {
            var tipo = NombresPersona.Limpiar(i.TipoDocumento).ToUpperInvariant();
            var hayNumero = !string.IsNullOrWhiteSpace(i.Documento);

            if (tipo.Length == 0 && !hayNumero) return null;
            if (tipo.Length == 0)
            {
                v.Error(nameof(PersonaDatosInput.TipoDocumento), "Elige el tipo de documento.");
                return null;
            }
            if (!hayNumero)
            {
                v.Error(nameof(PersonaDatosInput.Documento), "Escribe el número del documento.");
                return null;
            }

            var pais = NombresPersona.Limpiar(i.PaisEmisor).ToUpperInvariant();
            if (pais.Length == 0) pais = _opt.PaisPorDefecto;
            d.TipoDocumento = tipo;
            d.PaisEmisor = pais;

            var catalogo = await _db.CatalogoTiposDocumento.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Codigo == tipo && t.Activo, ct);
            if (catalogo == null)
            {
                v.Error(nameof(PersonaDatosInput.TipoDocumento), "El tipo de documento no es válido.");
                return null;
            }

            if (!await _db.Paises.AsNoTracking().AnyAsync(p => p.CodigoIso == pais && p.Activo, ct))
            {
                v.Error(nameof(PersonaDatosInput.PaisEmisor), "El país emisor no es válido.");
                return null;
            }
            if (catalogo.PaisIso != null && !string.Equals(catalogo.PaisIso, pais, StringComparison.OrdinalIgnoreCase))
            {
                v.Error(nameof(PersonaDatosInput.PaisEmisor), $"El {Etiqueta(catalogo)} no corresponde al país emisor elegido.");
                return null;
            }

            var numero = DocumentosIdentidad.NormalizarNumero(i.Documento);
            d.Documento = numero;
            d.DocumentoFormateado = tipo == DocumentosIdentidad.Dni ? DocumentosIdentidad.FormatearDni(numero) : numero;

            var errorFormato = ErrorDeFormato(catalogo, numero);
            if (errorFormato != null)
            {
                v.Error(nameof(PersonaDatosInput.Documento), errorFormato);
                return null;
            }

            if (tipo == DocumentosIdentidad.Dni && DocumentosIdentidad.TryAnalizarDni(numero, out var partes))
            {
                var errorPartes = DocumentosIdentidad.ValidarPartesDni(partes, hoy.Year);
                if (errorPartes != null)
                {
                    v.Error(nameof(PersonaDatosInput.Documento), errorPartes);
                    return null;
                }

                // El código de municipio del DNI lo asigna el RNP: si no está en el catálogo solo se avisa.
                if (!await _db.CatalogoMunicipios.AsNoTracking().AnyAsync(m => m.Codigo == partes.Municipio && m.Activo, ct))
                    v.Aviso("El código de municipio del DNI no figura en el catálogo; verifica el número.");
            }

            d.TieneDocumentoDeIdentidad = catalogo.EsIdentidad;

            // Unicidad global del documento (índice UX_persona_documentos_identidad), sin contar a la propia persona.
            var excluir = i.IdPersona ?? 0;
            var otraPersona = await _db.PersonaDocumentos.AsNoTracking()
                .Where(x => !x.Eliminado && x.TipoDocumento == tipo && x.PaisEmisor == pais
                            && x.NumeroNormalizado == numero && x.IdPersona != excluir)
                .Select(x => (int?)x.IdPersona)
                .FirstOrDefaultAsync(ct);
            if (otraPersona == null) return null;

            var idOtra = otraPersona.Value;

            // El administrador general ve todo el sistema: para él cualquier duplicado es un error, con su número.
            if (i.Modo == ModoValidacionPersona.EdicionAdministrador)
            {
                v.Error(nameof(PersonaDatosInput.Documento), $"Ese documento ya pertenece a otra persona registrada (n.º {idOtra}).");
                return new DocumentoExistente(idOtra, false);
            }

            var enEstaEmpresa = await _db.PersonaEmpresas.AsNoTracking()
                .AnyAsync(x => x.IdPersona == idOtra && x.IdEmpresa == i.IdEmpresa && !x.Eliminado, ct);

            // Solo dentro de la empresa hay error: fuera de ella no se revela nada (decisión D8).
            if (enEstaEmpresa && i.DocumentoDeLaEmpresaEsDuplicado)
                v.Error(nameof(PersonaDatosInput.Documento), "Ya existe una persona con ese documento en esta empresa.");

            return new DocumentoExistente(idOtra, enEstaEmpresa);
        }

        /// <summary>Código para el DNI y el RTN; nombre en minúsculas para los demás ("pasaporte").</summary>
        private static string Etiqueta(CatalogoTipoDocumento t) =>
            t.Codigo is DocumentosIdentidad.Dni or DocumentosIdentidad.Rtn ? t.Codigo : t.Nombre.ToLowerInvariant();

        /// <summary>
        /// Aplica la regla del catálogo (patrón y largos) a un número ya normalizado. Si el catálogo no
        /// trae un patrón válido, exige solo letras y números y respeta los largos.
        /// </summary>
        private static string? ErrorDeFormato(CatalogoTipoDocumento t, string numero)
        {
            if (!string.IsNullOrWhiteSpace(t.Patron))
            {
                try
                {
                    return Regex.IsMatch(numero, t.Patron, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                        ? null
                        : MensajeDeLargo(t);
                }
                catch (ArgumentException) { /* patrón mal escrito en el catálogo: se usan las reglas generales */ }
                catch (RegexMatchTimeoutException) { /* igual */ }
            }

            if (!numero.All(char.IsAsciiLetterOrDigit))
                return $"El {Etiqueta(t)} solo admite letras y números.";

            if ((t.LargoMin != null && numero.Length < t.LargoMin) || (t.LargoMax != null && numero.Length > t.LargoMax))
                return MensajeDeLargo(t);

            return null;
        }

        private static string MensajeDeLargo(CatalogoTipoDocumento t)
        {
            var soloDigitos = t.Patron != null && t.Patron.Contains("[0-9]") && !t.Patron.Contains("A-Z");
            var unidad = soloDigitos ? "dígitos" : "caracteres";

            if (t.LargoMin != null && t.LargoMin == t.LargoMax)
                return $"El {Etiqueta(t)} debe tener {t.LargoMin} {unidad}.";
            if (t.LargoMin != null && t.LargoMax != null)
                return $"El {Etiqueta(t)} debe tener entre {t.LargoMin} y {t.LargoMax} {unidad}.";
            return $"El número del {Etiqueta(t)} no tiene un formato válido.";
        }

        // ──────────────────────────────────────────────────────────────────
        //  DATOS PERSONALES
        // ──────────────────────────────────────────────────────────────────

        private async Task ValidarDatosPersonalesAsync(
            PersonaDatosInput i, PersonaDatosNormalizados d, Acumulador v,
            bool esAlta, bool conRol, bool esConductor, DateOnly hoy, CancellationToken ct)
        {
            // Fecha de nacimiento
            if (i.FechaNacimiento is { } nacimiento)
            {
                d.FechaNacimiento = nacimiento;
                if (nacimiento > hoy)
                    v.Error(nameof(PersonaDatosInput.FechaNacimiento), "La fecha de nacimiento no puede ser futura.");
                else if (nacimiento < new DateOnly(1900, 1, 1))
                    v.Error(nameof(PersonaDatosInput.FechaNacimiento), "La fecha de nacimiento no es válida.");
                else
                {
                    var minima = esConductor ? _opt.EdadMinimaConductor : _opt.EdadMinima;
                    if (Edad(nacimiento, hoy) < minima)
                    {
                        v.Error(nameof(PersonaDatosInput.FechaNacimiento), esConductor
                            ? $"El conductor debe tener al menos {minima} años."
                            : $"La persona debe tener al menos {minima} años.");
                    }
                }
            }
            else if (esAlta && conRol)
            {
                v.Error(nameof(PersonaDatosInput.FechaNacimiento), "La fecha de nacimiento es obligatoria.");
            }

            // Listas cerradas (coinciden con los CHECK del script 014)
            var sexo = NombresPersona.Limpiar(i.Sexo).ToUpperInvariant();
            if (sexo.Length > 0)
            {
                if (sexo is "M" or "F") d.Sexo = sexo;
                else v.Error(nameof(PersonaDatosInput.Sexo), "El sexo debe ser M o F.");
            }

            var estadoCivil = NombresPersona.Limpiar(i.EstadoCivil).ToLowerInvariant();
            if (estadoCivil.Length > 0)
            {
                if (EstadosCiviles.Contains(estadoCivil)) d.EstadoCivil = estadoCivil;
                else v.Error(nameof(PersonaDatosInput.EstadoCivil), "El estado civil no es válido.");
            }

            var sangre = NombresPersona.Limpiar(i.TipoSangre).ToUpperInvariant();
            if (sangre.Length > 0)
            {
                if (TiposSangre.Contains(sangre)) d.TipoSangre = sangre;
                else v.Error(nameof(PersonaDatosInput.TipoSangre), "El tipo de sangre no es válido.");
            }

            // Referencias a catálogos
            var nacionalidad = NombresPersona.Limpiar(i.PaisNacionalidad).ToUpperInvariant();
            if (nacionalidad.Length > 0)
            {
                if (await _db.Paises.AsNoTracking().AnyAsync(p => p.CodigoIso == nacionalidad && p.Activo, ct))
                    d.PaisNacionalidad = nacionalidad;
                else
                    v.Error(nameof(PersonaDatosInput.PaisNacionalidad), "La nacionalidad no es válida.");
            }

            d.IdMunicipioNacimiento = await MunicipioValidoAsync(
                v, nameof(PersonaDatosInput.IdMunicipioNacimiento), "El municipio de nacimiento no es válido.", i.IdMunicipioNacimiento, ct);
            d.IdMunicipioResidencia = await MunicipioValidoAsync(
                v, nameof(PersonaDatosInput.IdMunicipioResidencia), "El municipio de residencia no es válido.", i.IdMunicipioResidencia, ct);

            var direccion = NombresPersona.Limpiar(i.DireccionResidencia);
            if (direccion.Length > 0)
            {
                if (direccion.Length > 300)
                    v.Error(nameof(PersonaDatosInput.DireccionResidencia), "La dirección no puede pasar de 300 caracteres.");
                else
                    d.DireccionResidencia = direccion;
            }
        }

        private async Task<int?> MunicipioValidoAsync(Acumulador v, string campo, string mensaje, int? idMunicipio, CancellationToken ct)
        {
            if (idMunicipio == null) return null;
            if (await _db.CatalogoMunicipios.AsNoTracking().AnyAsync(m => m.IdMunicipio == idMunicipio && m.Activo, ct))
                return idMunicipio;

            v.Error(campo, mensaje);
            return null;
        }

        // ──────────────────────────────────────────────────────────────────
        //  CONTACTO
        // ──────────────────────────────────────────────────────────────────

        private async Task ValidarContactoAsync(PersonaDatosInput i, PersonaDatosNormalizados d, Acumulador v, CancellationToken ct)
        {
            d.Telefono = Telefono(v, nameof(PersonaDatosInput.Telefono), "El teléfono", i.Telefono);
            d.TelefonoSecundario = Telefono(v, nameof(PersonaDatosInput.TelefonoSecundario), "El teléfono secundario", i.TelefonoSecundario);

            // Correo: formato válido y en minúsculas. No es único; si otra persona de la empresa lo usa, solo se avisa.
            if (!string.IsNullOrWhiteSpace(i.Email))
            {
                if (ContactoPersona.TryNormalizarCorreo(i.Email, out var correo))
                {
                    d.Email = correo;
                    var excluir = i.IdPersona ?? 0;
                    var detodas = i.Modo == ModoValidacionPersona.EdicionAdministrador;
                    var usado = await _db.Personas.AsNoTracking()
                        .AnyAsync(p => !p.Eliminado && p.IdPersona != excluir && p.Email == correo
                                       && (detodas || p.Vinculos.Any(x => x.IdEmpresa == i.IdEmpresa && !x.Eliminado)), ct);
                    if (usado)
                        v.Aviso(detodas ? "Ya hay otra persona con ese correo." : "Ya hay otra persona de esta empresa con ese correo.");
                }
                else
                {
                    v.Error(nameof(PersonaDatosInput.Email), "Escribe un correo válido, por ejemplo nombre@empresa.com.");
                }
            }

            // Contacto de emergencia: los tres datos o ninguno (CK_personas_emergencia).
            var nombre = NombresPersona.Limpiar(i.ContactoEmergenciaNombre);
            var parentesco = NombresPersona.Limpiar(i.ContactoEmergenciaParentesco);
            var hayTelefono = !string.IsNullOrWhiteSpace(i.ContactoEmergenciaTelefono);

            if (nombre.Length == 0 && parentesco.Length == 0 && !hayTelefono) return;

            if (nombre.Length == 0 || parentesco.Length == 0 || !hayTelefono)
            {
                v.Error(nameof(PersonaDatosInput.ContactoEmergenciaNombre), "Completa nombre, teléfono y parentesco del contacto.");
                return;
            }

            if (!NombresPersona.EsValido(nombre))
                v.Error(nameof(PersonaDatosInput.ContactoEmergenciaNombre), "El nombre del contacto solo admite letras, espacios, apóstrofo y guion.");
            else if (nombre.Length > 150)
                v.Error(nameof(PersonaDatosInput.ContactoEmergenciaNombre), "El nombre del contacto no puede pasar de 150 caracteres.");
            else
                d.ContactoEmergenciaNombre = NombresPersona.AFormatoPropio(nombre);

            if (!NombresPersona.EsValido(parentesco))
                v.Error(nameof(PersonaDatosInput.ContactoEmergenciaParentesco), "El parentesco solo admite letras, espacios, apóstrofo y guion.");
            else if (parentesco.Length > 30)
                v.Error(nameof(PersonaDatosInput.ContactoEmergenciaParentesco), "El parentesco no puede pasar de 30 caracteres.");
            else
                d.ContactoEmergenciaParentesco = parentesco.ToLowerInvariant();

            d.ContactoEmergenciaTelefono = Telefono(
                v, nameof(PersonaDatosInput.ContactoEmergenciaTelefono), "El teléfono del contacto", i.ContactoEmergenciaTelefono);
        }

        private static string? Telefono(Acumulador v, string campo, string etiqueta, string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return null;
            if (ContactoPersona.TryNormalizarTelefono(valor, out var digitos)) return digitos;

            v.Error(campo, $"{etiqueta} debe tener {ContactoPersona.DigitosTelefono} dígitos.");
            return null;
        }

        // ──────────────────────────────────────────────────────────────────
        //  LICENCIA DE CONDUCIR
        // ──────────────────────────────────────────────────────────────────

        private async Task ValidarLicenciaAsync(
            PersonaDatosInput i, PersonaDatosNormalizados d, Acumulador v,
            bool esAlta, bool esConductor, DateOnly hoy, CancellationToken ct)
        {
            var tipo = NombresPersona.Limpiar(i.LicenciaTipo).ToUpperInvariant();
            var numero = NombresPersona.Limpiar(i.LicenciaNumero).ToUpperInvariant();
            var vencimiento = i.LicenciaVencimiento;
            var exigida = esConductor && esAlta;

            if (tipo.Length == 0 && numero.Length == 0 && vencimiento == null)
            {
                if (exigida)
                {
                    v.Error(nameof(PersonaDatosInput.LicenciaTipo), "Elige la categoría de la licencia.");
                    v.Error(nameof(PersonaDatosInput.LicenciaNumero), "Escribe el número de la licencia.");
                    v.Error(nameof(PersonaDatosInput.LicenciaVencimiento), "Escribe el vencimiento de la licencia.");
                }
                else if (esConductor)
                {
                    v.Aviso("A este conductor le falta registrar la licencia.");
                }
                return;
            }

            // Categoría y número van juntos.
            if (tipo.Length == 0)
                v.Error(nameof(PersonaDatosInput.LicenciaTipo), "Elige la categoría de la licencia.");
            else if (await _db.CatalogoTiposLicencia.AsNoTracking().AnyAsync(t => t.Codigo == tipo && t.Activo, ct))
                d.LicenciaTipo = tipo;
            else
                v.Error(nameof(PersonaDatosInput.LicenciaTipo), "La categoría de licencia no es válida.");

            if (numero.Length == 0)
                v.Error(nameof(PersonaDatosInput.LicenciaNumero), "Escribe el número de la licencia.");
            else if (!FormatoNumeroLicencia.IsMatch(numero))
                v.Error(nameof(PersonaDatosInput.LicenciaNumero), "El número de licencia solo admite letras, números y guiones, hasta 30 caracteres.");
            else
                d.LicenciaNumero = numero;

            if (vencimiento is not { } vence)
            {
                if (exigida)
                    v.Error(nameof(PersonaDatosInput.LicenciaVencimiento), "Escribe el vencimiento de la licencia.");
                return;
            }

            d.LicenciaVencimiento = vence;
            var dias = vence.DayNumber - hoy.DayNumber;
            var sujeto = esConductor ? "La licencia de este conductor" : "La licencia";

            if (esAlta && dias <= 0)
                v.Error(nameof(PersonaDatosInput.LicenciaVencimiento), "La licencia ya venció o vence hoy; registra una licencia vigente.");
            else if (dias < 0)
                v.Aviso($"{sujeto} está vencida.");
            else if (dias <= _opt.DiasAlertaLicencia)
                v.Aviso(dias == 0 ? $"{sujeto} vence hoy." : $"{sujeto} vence en {dias} días.");
        }

        // ──────────────────────────────────────────────────────────────────
        //  ROL DE EMPLEADO
        // ──────────────────────────────────────────────────────────────────

        private async Task ValidarEmpleadoAsync(
            PersonaDatosInput i, PersonaDatosNormalizados d, Acumulador v, bool esAlta, CancellationToken ct)
        {
            var e = i.Empleado;
            if (e == null) return;

            var n = new EmpleadoDatosNormalizados();
            d.Empleado = n;

            // Código de empleado: único dentro de la empresa entre los empleados activos (D6).
            var codigo = NombresPersona.Limpiar(e.CodigoInterno);
            if (codigo.Length > 0)
            {
                if (!FormatoCodigoInterno.IsMatch(codigo))
                {
                    v.Error("Empleado.CodigoInterno", "El código de empleado solo admite letras y números, sin espacios, hasta 30 caracteres.");
                }
                else
                {
                    n.CodigoInterno = codigo;
                    var excluir = i.IdPersona ?? 0;
                    var repetido = await _db.Empleados.AsNoTracking()
                        .AnyAsync(x => x.IdEmpresa == i.IdEmpresa && x.CodigoInterno == codigo
                                       && !x.Eliminado && x.Activo && x.Vinculo.IdPersona != excluir, ct);
                    if (repetido)
                        v.Error("Empleado.CodigoInterno", "Ya existe un empleado con ese código en esta empresa.");
                }
            }

            // Cargo (D7): debe existir y estar activo entre los cargos de la empresa.
            var cargo = NombresPersona.Limpiar(e.Cargo).ToUpperInvariant();
            if (cargo.Length == 0)
            {
                if (esAlta) v.Error("Empleado.Cargo", "Elige un cargo definido para esta empresa.");
                else v.Aviso("La persona no tiene un cargo asignado.");
            }
            else if (await _db.Cargos.AsNoTracking().AnyAsync(c => c.IdEmpresa == i.IdEmpresa && c.Codigo == cargo && c.Activo && !c.Eliminado, ct))
            {
                n.Cargo = cargo;
            }
            else
            {
                v.Error("Empleado.Cargo", "Elige un cargo definido para esta empresa.");
            }

            // Fechas laborales
            n.FechaIngreso = e.FechaIngreso;
            n.FechaBaja = e.FechaBaja;
            if (e.FechaIngreso != null && e.FechaBaja != null && e.FechaBaja < e.FechaIngreso)
                v.Error("Empleado.FechaBaja", "La fecha de baja no puede ser anterior al ingreso.");

            // Tarifa y moneda
            var moneda = NombresPersona.Limpiar(e.MonedaTarifa).ToUpperInvariant();
            if (e.TarifaDiaria is { } tarifa)
            {
                if (tarifa < 0)
                    v.Error("Empleado.TarifaDiaria", "La tarifa no puede ser negativa.");
                else if (decimal.Round(tarifa, 2) != tarifa)
                    v.Error("Empleado.TarifaDiaria", "La tarifa admite como máximo 2 decimales.");
                else
                    n.TarifaDiaria = tarifa;

                if (moneda.Length == 0) moneda = _opt.MonedaPorDefecto;
            }

            if (moneda.Length > 0)
            {
                if (await _db.Monedas.AsNoTracking().AnyAsync(m => m.CodigoIso == moneda && m.Activo, ct))
                    n.MonedaTarifa = moneda;
                else
                    v.Error("Empleado.MonedaTarifa", "La moneda no es válida.");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  UTILIDADES
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Fecha de hoy en Honduras (UTC-6, sin horario de verano), para no adelantar el día por las noches.</summary>
        private DateOnly Hoy() => DateOnly.FromDateTime(_tiempo.GetUtcNow().UtcDateTime.AddHours(-6));

        private static int Edad(DateOnly nacimiento, DateOnly hoy)
        {
            var edad = hoy.Year - nacimiento.Year;
            if (nacimiento > hoy.AddYears(-edad)) edad--;
            return edad;
        }

        private sealed class Acumulador
        {
            public List<ErrorValidacion> Errores { get; } = new();
            public List<string> Advertencias { get; } = new();

            public void Error(string campo, string mensaje) => Errores.Add(new ErrorValidacion(campo, mensaje));
            public void Aviso(string mensaje) => Advertencias.Add(mensaje);
        }
    }
}
