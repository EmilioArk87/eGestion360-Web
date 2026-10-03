using System.Text.Json;

namespace eGestion360Web.Services.Auditoria
{
    /// <summary>Nombres en español de las entidades, operaciones y campos de la bitácora, para mostrarlos al usuario.</summary>
    public static class EtiquetasBitacora
    {
        private static readonly Dictionary<string, string> Entidades = new()
        {
            ["personas"] = "Persona",
            ["persona_documentos"] = "Documento",
            ["persona_empresa"] = "Vínculo con la empresa",
            ["empleados"] = "Ficha de empleado",
            ["clientes"] = "Ficha de cliente"
        };

        private static readonly Dictionary<string, string> Operaciones = new()
        {
            ["INSERT"] = "Alta",
            ["UPDATE"] = "Cambio",
            ["DELETE"] = "Baja"
        };

        private static readonly Dictionary<string, string> Campos = new()
        {
            ["primer_nombre"] = "Primer nombre",
            ["segundo_nombre"] = "Segundo nombre",
            ["primer_apellido"] = "Primer apellido",
            ["segundo_apellido"] = "Segundo apellido",
            ["nombres"] = "Nombres (registro antiguo)",
            ["apellidos"] = "Apellidos (registro antiguo)",
            ["nombre_normalizado"] = "Nombre normalizado",
            ["sexo"] = "Sexo",
            ["estado_civil"] = "Estado civil",
            ["fecha_nacimiento"] = "Fecha de nacimiento",
            ["pais_nacionalidad"] = "Nacionalidad",
            ["tipo_sangre"] = "Tipo de sangre",
            ["id_municipio_nacimiento"] = "Municipio de nacimiento",
            ["id_municipio_residencia"] = "Municipio de residencia",
            ["direccion_residencia"] = "Dirección",
            ["telefono"] = "Teléfono",
            ["telefono_secundario"] = "Teléfono secundario",
            ["email"] = "Correo",
            ["contacto_emergencia_nombre"] = "Contacto de emergencia",
            ["contacto_emergencia_telefono"] = "Teléfono del contacto",
            ["contacto_emergencia_parentesco"] = "Parentesco del contacto",
            ["licencia_tipo"] = "Categoría de licencia",
            ["licencia_numero"] = "Número de licencia",
            ["licencia_vencimiento"] = "Vencimiento de la licencia",
            ["estado_identidad"] = "Identidad",
            ["id_persona_principal"] = "Fusionada en la persona",
            ["activo"] = "Activo",
            ["eliminado"] = "Eliminado",
            ["fecha_eliminado"] = "Fecha de eliminación",
            ["cargo"] = "Cargo",
            ["tarifa_diaria"] = "Tarifa diaria",
            ["moneda_tarifa"] = "Moneda de la tarifa",
            ["fecha_ingreso"] = "Fecha de ingreso",
            ["fecha_baja"] = "Fecha de baja",
            ["documento"] = "Documento (registro antiguo)",
            ["tipo_documento"] = "Tipo de documento",
            ["id_empresa"] = "Empresa",
            ["codigo_interno"] = "Código de empleado",
            ["tipo_vinculo"] = "Tipo de vínculo",
            ["fecha_inicio"] = "Inicio del vínculo",
            ["fecha_fin"] = "Fin del vínculo",
            ["motivo_fin"] = "Motivo del fin",
            ["numero"] = "Número",
            ["pais_emisor"] = "País emisor",
            ["es_principal"] = "Principal",
            ["fecha_vencimiento"] = "Vencimiento",
            ["fusion"] = "Fusión de fichas",
            ["codigo"] = "Código de cliente",
            ["razon_social"] = "Razón social",
            ["nombre_comercial"] = "Nombre comercial",
            ["tipo"] = "Tipo de cliente",
            ["identificador_fiscal"] = "Identificador fiscal (RTN)",
            ["direccion"] = "Dirección comercial",
            ["ciudad"] = "Ciudad",
            ["moneda_iso_default"] = "Moneda",
            ["id_condicion_pago_default"] = "Condición de pago",
            ["limite_credito"] = "Límite de crédito",
            ["id_persona_empresa"] = "Vínculo con la empresa"
        };

        public static string Entidad(string entidad) =>
            Entidades.TryGetValue(entidad, out var texto) ? texto : entidad;

        public static string Operacion(string operacion) =>
            Operaciones.TryGetValue(operacion, out var texto) ? texto : operacion;

        public static string Campo(string? campo)
        {
            if (string.IsNullOrEmpty(campo)) return string.Empty;
            if (Campos.TryGetValue(campo, out var texto)) return texto;

            var libre = campo.Replace('_', ' ');
            return char.ToUpperInvariant(libre[0]) + libre[1..];
        }

        /// <summary>Un valor guardado como texto, listo para mostrar: los booleanos pasan a Sí/No.</summary>
        public static string? Valor(string? valor) => valor switch
        {
            "true" => "Sí",
            "false" => "No",
            _ => valor
        };

        /// <summary>
        /// Resume la foto JSON de un alta o una baja como «Campo: valor, Campo: valor». Si el texto no es un JSON
        /// de objeto (o no hay), devuelve nulo.
        /// </summary>
        /// <param name="omitir">Nombres de columna que no se incluyen en el resumen (por ejemplo, los salarios).</param>
        public static string? Resumir(string? json, IReadOnlySet<string>? omitir = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var documento = JsonDocument.Parse(json);
                if (documento.RootElement.ValueKind != JsonValueKind.Object) return null;

                var partes = new List<string>();
                foreach (var propiedad in documento.RootElement.EnumerateObject())
                {
                    if (omitir != null && omitir.Contains(propiedad.Name)) continue;

                    var valor = propiedad.Value.ValueKind switch
                    {
                        JsonValueKind.String => propiedad.Value.GetString(),
                        JsonValueKind.True => "Sí",
                        JsonValueKind.False => "No",
                        _ => propiedad.Value.GetRawText()
                    };
                    partes.Add($"{Campo(propiedad.Name)}: {valor}");
                }
                return partes.Count == 0 ? null : string.Join(", ", partes);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
