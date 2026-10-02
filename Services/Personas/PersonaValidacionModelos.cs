namespace eGestion360Web.Services.Personas
{
    // ── Opciones ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reglas parametrizables (sección "Personas:Validacion" de la configuración; si no existe, valen
    /// estos valores). El plan pide confirmar las edades con RR.HH. antes de darlas por definitivas.
    /// </summary>
    public sealed class PersonaValidacionOptions
    {
        public int EdadMinima { get; set; } = 16;
        public int EdadMinimaConductor { get; set; } = 18;

        /// <summary>Se avisa cuando la licencia vence en este número de días o menos.</summary>
        public int DiasAlertaLicencia { get; set; } = 30;

        /// <summary>Código del cargo que exige licencia de conducir.</summary>
        public string CodigoCargoConductor { get; set; } = "CONDUCTOR";

        public string PaisPorDefecto { get; set; } = DocumentosIdentidad.PaisHonduras;
        public string MonedaPorDefecto { get; set; } = "HNL";
    }

    // ── Entrada ─────────────────────────────────────────────────────────────

    public enum ModoValidacionPersona
    {
        /// <summary>Registro nuevo: se exigen los datos mínimos de la ficha.</summary>
        Alta,

        /// <summary>
        /// Edición de una persona existente: solo se exigen el primer nombre y el primer apellido, y lo
        /// que falta pasa a advertencia, para que las fichas antiguas se completen poco a poco.
        /// </summary>
        Edicion
    }

    public sealed class PersonaDatosInput
    {
        public ModoValidacionPersona Modo { get; set; } = ModoValidacionPersona.Alta;

        /// <summary>Empresa de la sesión. Nunca debe venir del navegador.</summary>
        public int IdEmpresa { get; set; }

        /// <summary>Persona que se edita; nulo en un alta. Se excluye al buscar duplicados.</summary>
        public int? IdPersona { get; set; }

        /// <summary>
        /// Si es verdadero (el valor por defecto), un documento que ya pertenece a otra persona de ESTA empresa es
        /// un error. Los flujos que le agregan otro rol a una persona que la empresa ya tiene (un cliente que
        /// también es empleado) lo apagan y deciden con <see cref="ResultadoValidacionPersona.DocumentoExistente"/>.
        /// Lo fija cada servicio, nunca la pantalla: un valor que llegue del formulario no cuenta.
        /// </summary>
        public bool DocumentoDeLaEmpresaEsDuplicado { get; set; } = true;

        // Nombre
        public string? PrimerNombre { get; set; }
        public string? SegundoNombre { get; set; }
        public string? PrimerApellido { get; set; }
        public string? SegundoApellido { get; set; }

        // Documento de identidad
        public string? TipoDocumento { get; set; }
        public string? Documento { get; set; }
        public string? PaisEmisor { get; set; }

        // Datos personales
        public DateOnly? FechaNacimiento { get; set; }
        public string? Sexo { get; set; }
        public string? EstadoCivil { get; set; }
        public string? TipoSangre { get; set; }
        public string? PaisNacionalidad { get; set; }
        public int? IdMunicipioNacimiento { get; set; }
        public int? IdMunicipioResidencia { get; set; }
        public string? DireccionResidencia { get; set; }

        // Contacto
        public string? Telefono { get; set; }
        public string? TelefonoSecundario { get; set; }
        public string? Email { get; set; }
        public string? ContactoEmergenciaNombre { get; set; }
        public string? ContactoEmergenciaTelefono { get; set; }
        public string? ContactoEmergenciaParentesco { get; set; }

        // Licencia de conducir
        public string? LicenciaTipo { get; set; }
        public string? LicenciaNumero { get; set; }
        public DateOnly? LicenciaVencimiento { get; set; }

        /// <summary>Datos del rol de empleado en esta empresa; nulo si solo se valida a la persona.</summary>
        public EmpleadoDatosInput? Empleado { get; set; }
    }

    public sealed class EmpleadoDatosInput
    {
        public string? CodigoInterno { get; set; }
        public string? Cargo { get; set; }
        public DateOnly? FechaIngreso { get; set; }
        public DateOnly? FechaBaja { get; set; }
        public decimal? TarifaDiaria { get; set; }
        public string? MonedaTarifa { get; set; }
    }

    // ── Salida ──────────────────────────────────────────────────────────────

    /// <param name="Campo">Nombre de la propiedad de <see cref="PersonaDatosInput"/> ("Empleado.Cargo" para el rol), listo para ModelState.</param>
    public sealed record ErrorValidacion(string Campo, string Mensaje);

    /// <summary>Documento que ya está registrado en otra persona. Es un dato interno: no se muestra a la pantalla.</summary>
    /// <param name="IdPersona">Persona que ya tiene el documento.</param>
    /// <param name="TieneVinculoEnEstaEmpresa">Si esa persona ya tiene un vínculo vigente con la empresa de la sesión.</param>
    public sealed record DocumentoExistente(int IdPersona, bool TieneVinculoEnEstaEmpresa);

    /// <param name="Ok">Verdadero si no hay errores (puede haber advertencias).</param>
    /// <param name="Errores">Impiden guardar.</param>
    /// <param name="Advertencias">No impiden guardar; se muestran al usuario.</param>
    /// <param name="Datos">Los datos ya limpios y normalizados, también cuando hay errores, para volver a mostrarlos.</param>
    /// <param name="DocumentoExistente">
    /// Se informa cuando el documento ya está registrado en OTRA persona. Si esa persona ya está en esta
    /// empresa, además hay un error. Si está solo en otra empresa NO hay error: el flujo de alta debe
    /// pedir la verificación del rol (decisión D8) y no revelar nada.
    /// </param>
    public sealed record ResultadoValidacionPersona(
        bool Ok,
        IReadOnlyList<ErrorValidacion> Errores,
        IReadOnlyList<string> Advertencias,
        PersonaDatosNormalizados Datos,
        DocumentoExistente? DocumentoExistente);

    /// <summary>Los datos de <see cref="PersonaDatosInput"/> ya limpios, normalizados y con los campos legados compuestos.</summary>
    public sealed class PersonaDatosNormalizados
    {
        public string PrimerNombre { get; set; } = string.Empty;
        public string? SegundoNombre { get; set; }
        public string PrimerApellido { get; set; } = string.Empty;
        public string? SegundoApellido { get; set; }

        /// <summary>Campo legado personas.nombres (primer y segundo nombre).</summary>
        public string Nombres { get; set; } = string.Empty;

        /// <summary>Campo legado personas.apellidos (primer y segundo apellido).</summary>
        public string Apellidos { get; set; } = string.Empty;

        public string NombreNormalizado { get; set; } = string.Empty;

        public string? TipoDocumento { get; set; }
        public string? PaisEmisor { get; set; }

        /// <summary>Número del documento tal como se guarda (para el DNI, solo dígitos).</summary>
        public string? Documento { get; set; }

        /// <summary>Número para mostrar (el DNI como 0801-1990-12345).</summary>
        public string? DocumentoFormateado { get; set; }

        /// <summary>Hay un documento de un tipo de identidad y es válido: la identidad pasa a 'verificada'.</summary>
        public bool TieneDocumentoDeIdentidad { get; set; }

        public DateOnly? FechaNacimiento { get; set; }
        public string? Sexo { get; set; }
        public string? EstadoCivil { get; set; }
        public string? TipoSangre { get; set; }
        public string? PaisNacionalidad { get; set; }
        public int? IdMunicipioNacimiento { get; set; }
        public int? IdMunicipioResidencia { get; set; }
        public string? DireccionResidencia { get; set; }

        /// <summary>Ocho dígitos.</summary>
        public string? Telefono { get; set; }
        public string? TelefonoSecundario { get; set; }

        /// <summary>En minúsculas.</summary>
        public string? Email { get; set; }

        public string? ContactoEmergenciaNombre { get; set; }
        public string? ContactoEmergenciaTelefono { get; set; }
        public string? ContactoEmergenciaParentesco { get; set; }

        public string? LicenciaTipo { get; set; }
        public string? LicenciaNumero { get; set; }
        public DateOnly? LicenciaVencimiento { get; set; }

        public EmpleadoDatosNormalizados? Empleado { get; set; }
    }

    public sealed class EmpleadoDatosNormalizados
    {
        public string? CodigoInterno { get; set; }
        public string? Cargo { get; set; }
        public DateOnly? FechaIngreso { get; set; }
        public DateOnly? FechaBaja { get; set; }
        public decimal? TarifaDiaria { get; set; }
        public string? MonedaTarifa { get; set; }
    }
}
