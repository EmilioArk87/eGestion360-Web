using System.Globalization;
using System.Net.Mail;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace eGestion360Web.Services.TasasCambio
{
    /// <summary>
    /// Opciones del job de tasas de cambio que se editan desde la pantalla (script 024). Los textos llegan tal como
    /// los escribió quien edita; <see cref="ConfiguracionTasas.Validar"/> los lee sin depender de la cultura del servidor.
    /// </summary>
    public sealed class ConfiguracionTasasInput
    {
        public bool Habilitado { get; set; }
        public string? Monedas { get; set; }
        public string? PrimerIntento { get; set; }
        public string? UltimoIntento { get; set; }
        public string? BarridoMatutino { get; set; }
        public string? DiasPuestaAlDia { get; set; }
        public string? MaxIntentosPorFecha { get; set; }
        public string? VariacionDiariaMaxPct { get; set; }
        public string? DiferenciaCompraVentaMaxPct { get; set; }
        public string? RangoUsdMin { get; set; }
        public string? RangoUsdMax { get; set; }
        public string? RangoEurMin { get; set; }
        public string? RangoEurMax { get; set; }
        public string? Destinatarios { get; set; }

        /// <summary>El formulario con lo que rige hoy (la base: appsettings más lo guardado).</summary>
        public static ConfiguracionTasasInput Desde(TasasCambioOptions o)
        {
            o.Validacion.Rangos.TryGetValue("USD", out var usd);
            o.Validacion.Rangos.TryGetValue("EUR", out var eur);
            return new ConfiguracionTasasInput
            {
                Habilitado = o.Habilitado,
                Monedas = string.Join(", ", o.MonedasEfectivas),
                PrimerIntento = o.HoraPrimerIntento.ToString("HH:mm", CultureInfo.InvariantCulture),
                UltimoIntento = o.HoraUltimoIntento.ToString("HH:mm", CultureInfo.InvariantCulture),
                BarridoMatutino = o.HoraBarridoMatutino.ToString("HH:mm", CultureInfo.InvariantCulture),
                DiasPuestaAlDia = o.DiasPuestaAlDia.ToString(CultureInfo.InvariantCulture),
                MaxIntentosPorFecha = o.Reintentos.MaxPorFecha.ToString(CultureInfo.InvariantCulture),
                VariacionDiariaMaxPct = Num(o.Validacion.VariacionDiariaMaxPct),
                DiferenciaCompraVentaMaxPct = Num(o.Validacion.DiferenciaCompraVentaMaxPct),
                RangoUsdMin = usd == null ? null : Num(usd.Min),
                RangoUsdMax = usd == null ? null : Num(usd.Max),
                RangoEurMin = eur == null ? null : Num(eur.Min),
                RangoEurMax = eur == null ? null : Num(eur.Max),
                Destinatarios = string.Join(Environment.NewLine, o.Alertas.Destinatarios)
            };
        }

        private static string Num(decimal valor) => valor.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>Lo que está vigente en las opciones antes de aplicar nada de la base (appsettings y variables de entorno).</summary>
    public sealed class ValoresBaseTasas
    {
        private ValoresBaseTasas() { }

        public bool Habilitado { get; private init; }
        public List<string> Monedas { get; private init; } = new();
        public string PrimerIntento { get; private init; } = string.Empty;
        public string UltimoIntento { get; private init; } = string.Empty;
        public string BarridoMatutino { get; private init; } = string.Empty;
        public int DiasPuestaAlDia { get; private init; }
        public int MaxPorFecha { get; private init; }
        public decimal Variacion { get; private init; }
        public decimal Diferencia { get; private init; }
        public Dictionary<string, RangoTasa> Rangos { get; private init; } = new();
        public List<string> Destinatarios { get; private init; } = new();

        public static ValoresBaseTasas Copiar(TasasCambioOptions o) => new()
        {
            Habilitado = o.Habilitado,
            Monedas = o.Monedas.ToList(),
            PrimerIntento = o.PrimerIntento,
            UltimoIntento = o.UltimoIntento,
            BarridoMatutino = o.BarridoMatutino,
            DiasPuestaAlDia = o.DiasPuestaAlDia,
            MaxPorFecha = o.Reintentos.MaxPorFecha,
            Variacion = o.Validacion.VariacionDiariaMaxPct,
            Diferencia = o.Validacion.DiferenciaCompraVentaMaxPct,
            Rangos = CopiarRangos(o.Validacion.Rangos),
            Destinatarios = o.Alertas.Destinatarios.ToList()
        };

        public static Dictionary<string, RangoTasa> CopiarRangos(Dictionary<string, RangoTasa> origen)
        {
            var copia = new Dictionary<string, RangoTasa>(StringComparer.OrdinalIgnoreCase);
            foreach (var (moneda, rango) in origen) copia[moneda] = new RangoTasa { Min = rango.Min, Max = rango.Max };
            return copia;
        }
    }

    /// <summary>Lectura, validación y aplicación de la configuración editable (script 024).</summary>
    public static class ConfiguracionTasas
    {
        public const int MaxMonedas = 10;
        public const int MaxDestinatarios = 20;

        /// <summary>
        /// Valida el formulario. Devuelve la fila a guardar o nulo si hay errores, que quedan en <paramref name="errores"/>
        /// por nombre de campo. Un campo en blanco queda nulo: se usa el valor de appsettings.
        /// </summary>
        public static TasaCambioConfiguracion? Validar(ConfiguracionTasasInput i, string monedaLocal, IDictionary<string, string> errores)
        {
            var fila = new TasaCambioConfiguracion { Habilitado = i.Habilitado };

            // Monedas: códigos de 3 letras, distintos de la moneda local, sin repetir.
            var monedas = Separar(i.Monedas).Select(m => m.ToUpperInvariant()).Distinct().ToList();
            if (monedas.Count > MaxMonedas)
                errores[nameof(i.Monedas)] = $"Máximo {MaxMonedas} monedas.";
            else if (monedas.Any(m => m.Length != 3 || !m.All(c => c is >= 'A' and <= 'Z')))
                errores[nameof(i.Monedas)] = "Escriba códigos de 3 letras separados por coma, por ejemplo USD, EUR.";
            else if (monedas.Contains(monedaLocal))
                errores[nameof(i.Monedas)] = $"{monedaLocal} es la moneda local: no se busca contra sí misma.";
            else
                fila.Monedas = monedas.Count == 0 ? null : string.Join(",", monedas);

            var primer = Hora(i.PrimerIntento, nameof(i.PrimerIntento), "primer intento", errores);
            var ultimo = Hora(i.UltimoIntento, nameof(i.UltimoIntento), "último intento", errores);
            var barrido = Hora(i.BarridoMatutino, nameof(i.BarridoMatutino), "barrido matutino", errores);
            fila.PrimerIntento = primer?.ToString("HH:mm", CultureInfo.InvariantCulture);
            fila.UltimoIntento = ultimo?.ToString("HH:mm", CultureInfo.InvariantCulture);
            fila.BarridoMatutino = barrido?.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (primer is { } p && ultimo is { } u && p >= u)
                errores[nameof(i.UltimoIntento)] = "El último intento debe ser después del primero.";

            fila.DiasPuestaAlDia = Entero(i.DiasPuestaAlDia, nameof(i.DiasPuestaAlDia), "Los días de puesta al día", 1, 30, errores);
            fila.MaxIntentosPorFecha = Entero(i.MaxIntentosPorFecha, nameof(i.MaxIntentosPorFecha), "Los intentos por fecha", 1, 20, errores);

            fila.VariacionDiariaMaxPct = Decimal(i.VariacionDiariaMaxPct, nameof(i.VariacionDiariaMaxPct), "La variación diaria", 0.01m, 50m, 2, errores);
            fila.DiferenciaCompraVentaMaxPct = Decimal(i.DiferenciaCompraVentaMaxPct, nameof(i.DiferenciaCompraVentaMaxPct), "La diferencia compra/venta", 0.01m, 50m, 2, errores);

            (fila.RangoUsdMin, fila.RangoUsdMax) = Rango(i.RangoUsdMin, i.RangoUsdMax, nameof(i.RangoUsdMin), nameof(i.RangoUsdMax), "dólar", errores);
            (fila.RangoEurMin, fila.RangoEurMax) = Rango(i.RangoEurMin, i.RangoEurMax, nameof(i.RangoEurMin), nameof(i.RangoEurMax), "euro", errores);

            var correos = Separar(i.Destinatarios).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (correos.Count > MaxDestinatarios)
                errores[nameof(i.Destinatarios)] = $"Máximo {MaxDestinatarios} correos.";
            else if (correos.FirstOrDefault(c => !CorreoValido(c)) is { } malo)
                errores[nameof(i.Destinatarios)] = $"«{malo}» no es un correo válido.";
            else
            {
                var unidos = string.Join(",", correos);
                if (unidos.Length > 1000) errores[nameof(i.Destinatarios)] = "La lista de correos es demasiado larga.";
                else fila.DestinatariosAlerta = correos.Count == 0 ? null : unidos;
            }

            return errores.Count == 0 ? fila : null;
        }

        /// <summary>
        /// Deja en <paramref name="destino"/> los valores base y encima lo guardado. La instancia es la misma que
        /// leen el worker y las pantallas, así el cambio rige sin reiniciar. Sin fila, vuelve a los valores base.
        /// </summary>
        public static void Aplicar(TasasCambioOptions destino, ValoresBaseTasas basales, TasaCambioConfiguracion? fila,
            bool usarInterruptorGuardado = true)
        {
            destino.Habilitado = usarInterruptorGuardado ? fila?.Habilitado ?? basales.Habilitado : basales.Habilitado;
            destino.Monedas = Texto(fila?.Monedas) is { } m ? Separar(m).ToList() : basales.Monedas.ToList();
            destino.PrimerIntento = Texto(fila?.PrimerIntento) ?? basales.PrimerIntento;
            destino.UltimoIntento = Texto(fila?.UltimoIntento) ?? basales.UltimoIntento;
            destino.BarridoMatutino = Texto(fila?.BarridoMatutino) ?? basales.BarridoMatutino;
            destino.DiasPuestaAlDia = fila?.DiasPuestaAlDia ?? basales.DiasPuestaAlDia;
            destino.Reintentos.MaxPorFecha = fila?.MaxIntentosPorFecha ?? basales.MaxPorFecha;
            destino.Validacion.VariacionDiariaMaxPct = fila?.VariacionDiariaMaxPct ?? basales.Variacion;
            destino.Validacion.DiferenciaCompraVentaMaxPct = fila?.DiferenciaCompraVentaMaxPct ?? basales.Diferencia;

            var rangos = ValoresBaseTasas.CopiarRangos(basales.Rangos);
            PonerRango(rangos, "USD", fila?.RangoUsdMin, fila?.RangoUsdMax);
            PonerRango(rangos, "EUR", fila?.RangoEurMin, fila?.RangoEurMax);
            destino.Validacion.Rangos = rangos;

            destino.Alertas.Destinatarios = Texto(fila?.DestinatariosAlerta) is { } d ? Separar(d).ToList() : basales.Destinatarios.ToList();
        }

        private static void PonerRango(Dictionary<string, RangoTasa> rangos, string moneda, decimal? min, decimal? max)
        {
            if (min is null || max is null) return;
            rangos[moneda] = new RangoTasa { Min = min.Value, Max = max.Value };
        }

        private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        private static IEnumerable<string> Separar(string? texto) =>
            (texto ?? string.Empty).Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static bool CorreoValido(string correo)
        {
            if (correo.Length > 254) return false;
            try { return new MailAddress(correo).Address == correo; }
            catch (FormatException) { return false; }
        }

        private static TimeOnly? Hora(string? texto, string campo, string nombre, IDictionary<string, string> errores)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            if (TimeOnly.TryParseExact(texto.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora))
                return hora;
            errores[campo] = $"La hora del {nombre} debe ser HH:mm, por ejemplo 17:00.";
            return null;
        }

        private static byte? Entero(string? texto, string campo, string nombre, int min, int max, IDictionary<string, string> errores)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            if (int.TryParse(texto.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var valor) && valor >= min && valor <= max)
                return (byte)valor;
            errores[campo] = $"{nombre} deben ser un número entero entre {min} y {max}.";
            return null;
        }

        private static decimal? Decimal(string? texto, string campo, string nombre, decimal min, decimal max, int decimales, IDictionary<string, string> errores)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            if (LeerDecimal(texto, out var valor) && valor >= min && valor <= max && decimal.Round(valor, decimales) == valor)
                return valor;
            errores[campo] = $"{nombre} debe ser un número entre {min.ToString(CultureInfo.InvariantCulture)} y {max.ToString(CultureInfo.InvariantCulture)} (hasta {decimales} decimales).";
            return null;
        }

        private static (decimal?, decimal?) Rango(string? minTexto, string? maxTexto, string campoMin, string campoMax, string nombre, IDictionary<string, string> errores)
        {
            var vacioMin = string.IsNullOrWhiteSpace(minTexto);
            var vacioMax = string.IsNullOrWhiteSpace(maxTexto);
            if (vacioMin && vacioMax) return (null, null);
            if (vacioMin || vacioMax)
            {
                errores[vacioMin ? campoMin : campoMax] = $"Indique el mínimo y el máximo del {nombre}, o deje ambos en blanco.";
                return (null, null);
            }

            var min = Decimal(minTexto, campoMin, $"El mínimo del {nombre}", 0.0001m, 100000m, 4, errores);
            var max = Decimal(maxTexto, campoMax, $"El máximo del {nombre}", 0.0001m, 100000m, 4, errores);
            if (min is { } a && max is { } b && a >= b)
            {
                errores[campoMax] = $"El máximo del {nombre} debe ser mayor que el mínimo.";
                return (null, null);
            }
            return (min, max);
        }

        /// <summary>Punto decimal o coma, sin depender de la cultura del servidor.</summary>
        private static bool LeerDecimal(string texto, out decimal valor)
        {
            var limpio = texto.Trim();
            if (!limpio.Contains('.')) limpio = limpio.Replace(',', '.');
            return decimal.TryParse(limpio, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor);
        }
    }

    public interface ITasasCambioConfiguracionService
    {
        /// <summary>Lee la fila de la base y la aplica a las opciones vivas. Sin tabla o sin fila, rigen los valores base.</summary>
        Task AplicarAsync(CancellationToken ct = default);

        /// <summary>Guarda la configuración (crea la fila si no existe) y la aplica de inmediato.</summary>
        Task GuardarAsync(TasaCambioConfiguracion fila, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Verdadero en desarrollo: el interruptor guardado en la base NO se aplica (así correr la aplicación en una
        /// máquina de desarrollo contra la base real no enciende el job); rige appsettings.
        /// </summary>
        bool IgnoraInterruptorGuardado { get; }

        /// <summary>La fila guardada, o nulo si todavía no se guardó nada.</summary>
        Task<TasaCambioConfiguracion?> LeerAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Singleton: guarda una copia de los valores base (appsettings y entorno) tomada al crearse y aplica encima lo
    /// guardado en dbo.tasas_cambio_configuracion sobre la instancia compartida de <see cref="TasasCambioOptions"/>.
    /// Se usa un contexto propio por operación (el servicio vive más que una petición).
    /// </summary>
    public sealed class TasasCambioConfiguracionService : ITasasCambioConfiguracionService
    {
        private readonly TasasCambioOptions _opt;
        private readonly ValoresBaseTasas _base;
        private readonly IServiceScopeFactory _scopes;
        private readonly TimeProvider _reloj;
        private readonly ILogger<TasasCambioConfiguracionService> _log;

        public bool IgnoraInterruptorGuardado { get; }

        public TasasCambioConfiguracionService(IOptions<TasasCambioOptions> opciones, IServiceScopeFactory scopes,
            TimeProvider reloj, IHostEnvironment entorno, ILogger<TasasCambioConfiguracionService> log)
        {
            IgnoraInterruptorGuardado = entorno.IsDevelopment();
            _opt = opciones.Value;
            _base = ValoresBaseTasas.Copiar(_opt);
            _scopes = scopes;
            _reloj = reloj;
            _log = log;
        }

        public async Task AplicarAsync(CancellationToken ct = default)
        {
            try
            {
                var fila = await LeerAsync(ct);
                ConfiguracionTasas.Aplicar(_opt, _base, fila, !IgnoraInterruptorGuardado);
                if (fila != null)
                    _log.LogInformation("Tasas de cambio: configuración de la base aplicada (job habilitado = {Habilitado}).", fila.Habilitado);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sin la tabla (script 024 sin aplicar) o sin base: rige appsettings, como antes.
                _log.LogWarning(ex, "Tasas de cambio: no se pudo leer la configuración de la base; rigen los valores de appsettings.");
            }
        }

        public async Task<TasaCambioConfiguracion?> LeerAsync(CancellationToken ct = default)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ITasasCambioContextos>().Crear();
            await using (db)
            {
                return await db.TasasCambioConfiguracion.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.IdConfiguracion == TasaCambioConfiguracion.IdUnico, ct);
            }
        }

        public async Task GuardarAsync(TasaCambioConfiguracion fila, string usuario, CancellationToken ct = default)
        {
            fila.IdConfiguracion = TasaCambioConfiguracion.IdUnico;
            fila.ActualizadoPor = string.IsNullOrWhiteSpace(usuario) ? "system" : usuario.Trim();
            fila.ActualizadoUtc = _reloj.GetUtcNow().UtcDateTime;

            await using (var scope = _scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ITasasCambioContextos>().Crear();
                await using (db)
                {
                    var actual = await db.TasasCambioConfiguracion.FirstOrDefaultAsync(c => c.IdConfiguracion == TasaCambioConfiguracion.IdUnico, ct);
                    if (actual == null) db.TasasCambioConfiguracion.Add(fila);
                    else db.Entry(actual).CurrentValues.SetValues(fila);
                    await db.SaveChangesAsync(ct);
                }
            }

            ConfiguracionTasas.Aplicar(_opt, _base, fila, !IgnoraInterruptorGuardado);
            _log.LogInformation("Tasas de cambio: {Usuario} guardó la configuración (job habilitado = {Habilitado}).", fila.ActualizadoPor, fila.Habilitado);
        }
    }
}
