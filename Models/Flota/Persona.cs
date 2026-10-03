using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Personas;

namespace eGestion360Web.Models.Flota
{
    /// <summary>
    /// Persona maestra: una fila por persona real, compartida por varias empresas (script 014).
    /// Su relación con cada empresa y su rol (empleado, cliente...) están en <see cref="Vinculos"/>;
    /// sus documentos de identidad, en <see cref="Documentos"/>.
    ///
    /// Las columnas viejas de personas que describían a la persona como empleada de UNA empresa (id_empresa,
    /// documento, tipo_documento, cargo, tarifa_diaria, moneda_tarifa, fecha_ingreso y fecha_baja) ya no están
    /// en esta clase: pasaron a <see cref="PersonaEmpresa"/> (empresa, ingreso y baja), a <see cref="Empleado"/>
    /// (cargo, tarifa y moneda) y a <see cref="PersonaDocumento"/> (documento). El script 019 las retira de la
    /// tabla. Nombres y apellidos compuestos se conservan porque muchas pantallas muestran
    /// <see cref="NombreCompleto"/>.
    /// </summary>
    [Table("personas")]
    public class Persona
    {
        [Key]
        [Column("id_persona")]
        public int IdPersona { get; set; }

        /// <summary>COMPUESTO: se arma con <see cref="PrimerNombre"/> y <see cref="SegundoNombre"/> al guardar.</summary>
        [Required(ErrorMessage = "Los nombres son requeridos")]
        [StringLength(100)]
        [Display(Name = "Nombres")]
        [Column("nombres")]
        public string Nombres { get; set; } = string.Empty;

        /// <summary>COMPUESTO: se arma con <see cref="PrimerApellido"/> y <see cref="SegundoApellido"/> al guardar.</summary>
        [Required(ErrorMessage = "Los apellidos son requeridos")]
        [StringLength(100)]
        [Display(Name = "Apellidos")]
        [Column("apellidos")]
        public string Apellidos { get; set; } = string.Empty;

        // ── Contacto (se queda en la persona) ──

        [StringLength(30)]
        [Display(Name = "Teléfono")]
        [Column("telefono")]
        public string? Telefono { get; set; }

        [StringLength(150)]
        [Display(Name = "Email")]
        [Column("email")]
        public string? Email { get; set; }

        /// <summary>
        /// La persona está activa si lo está en alguna empresa; cada vínculo tiene además su propio estado
        /// (<see cref="PersonaEmpresa.Activo"/>), que es el que mandan las listas de cada empresa.
        /// </summary>
        [Display(Name = "Activo")]
        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("eliminado")]
        public bool Eliminado { get; set; }

        [Column("fecha_eliminado")]
        public DateTime? FechaEliminado { get; set; }

        [StringLength(100)]
        [Column("creado_por")]
        public string CreadoPor { get; set; } = string.Empty;

        [Column("fecha_creacion")]
        public DateTime FechaCreacion { get; set; }

        [StringLength(100)]
        [Column("modificado_por")]
        public string? ModificadoPor { get; set; }

        [Column("fecha_modificacion")]
        public DateTime? FechaModificacion { get; set; }

        [Timestamp]
        [Column("token_concurrencia")]
        public byte[] TokenConcurrencia { get; set; } = Array.Empty<byte>();

        // ── Persona maestra (script 014). Todas admiten NULL salvo EstadoIdentidad. ──

        [StringLength(50)]
        [Display(Name = "Primer nombre")]
        [Column("primer_nombre")]
        public string? PrimerNombre { get; set; }

        [StringLength(50)]
        [Display(Name = "Segundo nombre")]
        [Column("segundo_nombre")]
        public string? SegundoNombre { get; set; }

        [StringLength(50)]
        [Display(Name = "Primer apellido")]
        [Column("primer_apellido")]
        public string? PrimerApellido { get; set; }

        [StringLength(50)]
        [Display(Name = "Segundo apellido")]
        [Column("segundo_apellido")]
        public string? SegundoApellido { get; set; }

        /// <summary>
        /// Nombre completo en mayúsculas, sin tildes ni dieresis, Ñ como N y espacios simples, en el orden
        /// primer nombre, segundo nombre, primer apellido, segundo apellido. Sirve para detectar parecidos
        /// y no depende de cómo se repartan las palabras entre nombres y apellidos.
        /// </summary>
        [StringLength(200)]
        [Column("nombre_normalizado")]
        public string? NombreNormalizado { get; set; }

        /// <summary>'M' o 'F'.</summary>
        [StringLength(1)]
        [Display(Name = "Sexo")]
        [Column("sexo", TypeName = "char(1)")]
        public string? Sexo { get; set; }

        /// <summary>soltero, casado, union_libre, divorciado o viudo.</summary>
        [StringLength(15)]
        [Display(Name = "Estado civil")]
        [Column("estado_civil", TypeName = "varchar(15)")]
        public string? EstadoCivil { get; set; }

        [Display(Name = "Fecha de nacimiento")]
        [Column("fecha_nacimiento")]
        public DateOnly? FechaNacimiento { get; set; }

        [StringLength(2)]
        [Display(Name = "Nacionalidad")]
        [Column("pais_nacionalidad", TypeName = "char(2)")]
        public string? PaisNacionalidad { get; set; }

        [StringLength(3)]
        [Display(Name = "Tipo de sangre")]
        [Column("tipo_sangre", TypeName = "varchar(3)")]
        public string? TipoSangre { get; set; }

        [Display(Name = "Municipio de nacimiento")]
        [Column("id_municipio_nacimiento")]
        public int? IdMunicipioNacimiento { get; set; }

        [Display(Name = "Municipio de residencia")]
        [Column("id_municipio_residencia")]
        public int? IdMunicipioResidencia { get; set; }

        [StringLength(300)]
        [Display(Name = "Dirección")]
        [Column("direccion_residencia")]
        public string? DireccionResidencia { get; set; }

        [StringLength(30)]
        [Display(Name = "Teléfono secundario")]
        [Column("telefono_secundario", TypeName = "varchar(30)")]
        public string? TelefonoSecundario { get; set; }

        // Contacto de emergencia: los tres campos o ninguno (CK_personas_emergencia).

        [StringLength(150)]
        [Display(Name = "Contacto de emergencia")]
        [Column("contacto_emergencia_nombre")]
        public string? ContactoEmergenciaNombre { get; set; }

        [StringLength(30)]
        [Display(Name = "Teléfono del contacto")]
        [Column("contacto_emergencia_telefono", TypeName = "varchar(30)")]
        public string? ContactoEmergenciaTelefono { get; set; }

        [StringLength(30)]
        [Display(Name = "Parentesco")]
        [Column("contacto_emergencia_parentesco", TypeName = "varchar(30)")]
        public string? ContactoEmergenciaParentesco { get; set; }

        // Licencia de conducir (es dato de la persona, no del empleo).

        [StringLength(10)]
        [Display(Name = "Categoría de licencia")]
        [Column("licencia_tipo", TypeName = "varchar(10)")]
        public string? LicenciaTipo { get; set; }

        [StringLength(30)]
        [Display(Name = "Número de licencia")]
        [Column("licencia_numero", TypeName = "varchar(30)")]
        public string? LicenciaNumero { get; set; }

        [Display(Name = "Vencimiento de la licencia")]
        [Column("licencia_vencimiento")]
        public DateOnly? LicenciaVencimiento { get; set; }

        /// <summary>
        /// 'verificada' si tiene un documento de identidad registrado; 'pendiente' si no (ver
        /// <see cref="EstadosIdentidad"/>). El valor por defecto es obligatorio: la BD rechaza un texto vacío.
        /// </summary>
        [Required]
        [StringLength(15)]
        [Column("estado_identidad", TypeName = "varchar(15)")]
        public string EstadoIdentidad { get; set; } = EstadosIdentidad.Pendiente;

        /// <summary>
        /// Fusión de duplicados: si esta ficha sobra, apunta a la persona principal que la reemplaza.
        /// </summary>
        [Column("id_persona_principal")]
        public int? IdPersonaPrincipal { get; set; }

        // ── Navegación ──

        public Pais? Nacionalidad { get; set; }
        public CatalogoMunicipio? MunicipioNacimiento { get; set; }
        public CatalogoMunicipio? MunicipioResidencia { get; set; }
        public CatalogoTipoLicencia? TipoLicencia { get; set; }
        public Persona? PersonaPrincipal { get; set; }

        public ICollection<PersonaDocumento> Documentos { get; set; } = new List<PersonaDocumento>();
        public ICollection<PersonaEmpresa> Vinculos { get; set; } = new List<PersonaEmpresa>();

        [NotMapped]
        public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();

        /// <summary>
        /// Falso para las personas migradas con un reparto de nombres ambiguo: están en la lista de
        /// revisión hasta que se corrijan desde la pantalla.
        /// </summary>
        [NotMapped]
        public bool NombresSeparados => !string.IsNullOrWhiteSpace(PrimerNombre) && !string.IsNullOrWhiteSpace(PrimerApellido);
    }
}
