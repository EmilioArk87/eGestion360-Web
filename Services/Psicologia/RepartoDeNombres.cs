using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    /// <summary>
    /// Reparte un nombre completo escrito en una sola celda ("Carlos Andrés Mejía Rivera") en primer y segundo nombre y primer y
    /// segundo apellido. Sin acceso a datos.
    ///
    /// Reglas:
    ///   * Las partículas de los apellidos (de, del, la, las, los, y, san, santa) se pegan a la palabra siguiente: "de la Cruz" cuenta
    ///     como una sola pieza. Un nombre con partículas siempre se revisa: el reparto es probable, no seguro.
    ///   * 4 piezas: dos nombres y dos apellidos (el caso normal en Honduras); no hace falta revisarlo.
    ///   * 3 piezas: se propone un nombre y dos apellidos, pero puede ser dos nombres y un apellido: hay que revisarlo.
    ///   * 2 piezas: un nombre y un apellido; se revisa porque falta el segundo apellido.
    ///   * 5 o más piezas: los dos últimos son los apellidos, el primero el primer nombre y el resto el segundo nombre; se revisa.
    ///   * 1 pieza o ninguna: no se puede repartir.
    /// </summary>
    public static class RepartoDeNombres
    {
        private static readonly HashSet<string> Particulas = new(StringComparer.OrdinalIgnoreCase)
        {
            "de", "del", "la", "las", "los", "y", "san", "santa"
        };

        /// <param name="Valido">Falso si no se pudo repartir (menos de dos piezas).</param>
        /// <param name="RequiereRevision">Verdadero si el reparto es una propuesta que una persona debe confirmar.</param>
        /// <param name="Motivo">Por qué hay que revisarlo o por qué no es válido; nulo si está bien.</param>
        public sealed record Reparto(
            bool Valido, bool RequiereRevision, string? PrimerNombre, string? SegundoNombre, string? PrimerApellido, string? SegundoApellido,
            int Piezas, string? Motivo);

        public static Reparto Repartir(string? nombreCompleto)
        {
            var piezas = Piezas(nombreCompleto);
            var reparto = SegunPiezas(piezas);

            // Con partículas ("María de Jesús…", "Pedro de León…") el reparto es probable pero no seguro: la confirma una persona.
            if (reparto.Valido && !reparto.RequiereRevision && piezas.Any(p => p.Contains(' ')))
                return reparto with { RequiereRevision = true, Motivo = "Trae «de», «del» o similares: confirma cuáles son los nombres y cuáles los apellidos." };

            return reparto;
        }

        private static Reparto SegunPiezas(IReadOnlyList<string> piezas)
        {
            switch (piezas.Count)
            {
                case 0:
                    return new Reparto(false, true, null, null, null, null, 0, "La fila no trae nombre.");
                case 1:
                    return new Reparto(false, true, piezas[0], null, null, null, 1, "Solo trae una palabra: escribe nombres y apellidos.");
                case 2:
                    return new Reparto(true, true, piezas[0], null, piezas[1], null, 2,
                        "Trae dos palabras: confirma el nombre y el apellido (falta el segundo apellido).");
                case 3:
                    return new Reparto(true, true, piezas[0], null, piezas[1], piezas[2], 3,
                        "Trae tres palabras: confirma si son un nombre y dos apellidos o dos nombres y un apellido.");
                case 4:
                    return new Reparto(true, false, piezas[0], piezas[1], piezas[2], piezas[3], 4, null);
                default:
                    var segundo = string.Join(' ', piezas.Skip(1).Take(piezas.Count - 3));
                    return new Reparto(true, true, piezas[0], segundo, piezas[^2], piezas[^1], piezas.Count,
                        $"Trae {piezas.Count} palabras: confirma cuáles son los nombres y cuáles los apellidos.");
            }
        }

        /// <summary>Las piezas del nombre: palabras limpias con las partículas pegadas a la palabra que sigue.</summary>
        public static IReadOnlyList<string> Piezas(string? nombreCompleto)
        {
            var palabras = NombresPersona.Limpiar(nombreCompleto)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var piezas = new List<string>();
            var pendientes = new List<string>();
            foreach (var palabra in palabras)
            {
                if (Particulas.Contains(palabra))
                {
                    pendientes.Add(palabra);
                    continue;
                }

                pendientes.Add(palabra);
                piezas.Add(string.Join(' ', pendientes));
                pendientes.Clear();
            }

            // Partículas sueltas al final ("Pérez de"): se pegan a la última pieza para no perderlas.
            if (pendientes.Count > 0)
            {
                if (piezas.Count > 0) piezas[^1] = piezas[^1] + " " + string.Join(' ', pendientes);
                else piezas.Add(string.Join(' ', pendientes));
            }

            return piezas;
        }
    }
}
