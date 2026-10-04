using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Flota;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Flota.Operacion.ControlSalidas
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IPersonaConsultaService _personal;

        public IndexModel(ApplicationDbContext db, IPersonaConsultaService personal)
        {
            _db = db;
            _personal = personal;
        }

        public class VehiculoEstadoDto
        {
            public int IdVehiculo { get; set; }
            public string Placa { get; set; } = string.Empty;
            public string? NumeroInterno { get; set; }
            public string? Descripcion { get; set; }
            public string Estado { get; set; } = "dentro"; // dentro | fuera
            public decimal UltimoOdometro { get; set; }
            public int? IdControlSalidaAbierto { get; set; }
            public string? SalidaHora { get; set; }
            public decimal? SalidaKm { get; set; }
            public string? SalidaConductor { get; set; }
            public string? SalidaDestino { get; set; }
        }

        public class MovimientoItemDto
        {
            public string Hora { get; set; } = string.Empty;
            public string Tipo { get; set; } = "SALIDA"; // SALIDA | ENTRADA
            public string Placa { get; set; } = string.Empty;
            public string? NumeroInterno { get; set; }
            public decimal Odometro { get; set; }
            public string Detalle { get; set; } = string.Empty;
            public decimal? KmRecorridos { get; set; }
            public string? TiempoFuera { get; set; }
        }

        public List<VehiculoEstadoDto> ListaVehiculos { get; set; } = new();
        public List<ControlSalida> FueraAhora { get; set; } = new();
        public List<MovimientoItemDto> MovimientosHoy { get; set; } = new();

        public int KpiDentro { get; set; }
        public int KpiFuera { get; set; }
        public int KpiMovimientosHoy { get; set; }
        public decimal KpiKmHoy { get; set; }

        public SelectList ConductoresList { get; set; } = null!;
        public SelectList RutasList { get; set; } = null!;

        // Form bindings
        [BindProperty] public int IdVehiculoSalida { get; set; }
        [BindProperty] public decimal OdometroSalida { get; set; }
        [BindProperty] public int? IdConductorSalida { get; set; }
        [BindProperty] public string? DestinoSalida { get; set; }
        [BindProperty] public string? ObservacionesSalida { get; set; }

        [BindProperty] public int IdControlSalidaEntrada { get; set; }
        [BindProperty] public decimal OdometroEntrada { get; set; }
        [BindProperty] public string? ObservacionesEntrada { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            await CargarDatosAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostSalidaAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            int idEmpresa = GetIdEmpresa();

            var vehiculo = await _db.Vehiculos
                .FirstOrDefaultAsync(v => v.IdVehiculo == IdVehiculoSalida && v.IdEmpresa == idEmpresa && v.Activo && !v.Eliminado);

            if (vehiculo == null)
            {
                TempData["Error"] = "Vehículo no válido.";
                return RedirectToPage();
            }

            // Verificar si ya tiene salida abierta
            bool tieneSalidaAbierta = await _db.ControlSalidas
                .AnyAsync(c => c.IdVehiculo == IdVehiculoSalida && c.Estado == "ABIERTO" && !c.Eliminado);

            if (tieneSalidaAbierta)
            {
                TempData["Error"] = $"El vehículo {vehiculo.Placa} ya tiene una salida abierta sin entrada registrada.";
                return RedirectToPage();
            }

            // Validar odómetro
            decimal ultimoKm = await ObtenerUltimoOdometroVehiculoAsync(vehiculo.IdVehiculo, vehiculo.KmInicial);
            if (OdometroSalida < ultimoKm)
            {
                TempData["Error"] = $"El odómetro de salida ({OdometroSalida:#,##0.#} km) no puede ser menor a la última lectura conocida ({ultimoKm:#,##0.#} km).";
                return RedirectToPage();
            }

            var salida = new ControlSalida
            {
                IdEmpresa = idEmpresa,
                IdVehiculo = IdVehiculoSalida,
                IdConductor = IdConductorSalida,
                Destino = DestinoSalida,
                FechaHoraSalida = DateTime.Now,
                OdometroSalida = OdometroSalida,
                ObservacionesSalida = ObservacionesSalida,
                Estado = "ABIERTO",
                Activo = true,
                CreadoPor = HttpContext.Session.GetString("Username") ?? "sistema",
                FechaCreacion = DateTime.UtcNow
            };

            _db.ControlSalidas.Add(salida);
            await _db.SaveChangesAsync();

            TempData["Exito"] = $"Salida registrada con éxito para {vehiculo.Placa} (Odómetro: {OdometroSalida:#,##0.#} km).";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEntradaAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            int idEmpresa = GetIdEmpresa();

            var salida = await _db.ControlSalidas
                .Include(c => c.Vehiculo)
                .FirstOrDefaultAsync(c => c.IdControlSalida == IdControlSalidaEntrada && c.IdEmpresa == idEmpresa && c.Estado == "ABIERTO" && !c.Eliminado);

            if (salida == null)
            {
                TempData["Error"] = "No se encontró el registro de salida abierta a cerrar.";
                return RedirectToPage();
            }

            if (OdometroEntrada < salida.OdometroSalida)
            {
                TempData["Error"] = $"El odómetro de entrada ({OdometroEntrada:#,##0.#} km) no puede ser menor al de salida ({salida.OdometroSalida:#,##0.#} km).";
                return RedirectToPage();
            }

            salida.FechaHoraEntrada = DateTime.Now;
            salida.OdometroEntrada = OdometroEntrada;
            salida.ObservacionesEntrada = ObservacionesEntrada;
            salida.Estado = "CERRADO";
            salida.ModificadoPor = HttpContext.Session.GetString("Username") ?? "sistema";
            salida.FechaModificacion = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            decimal recorrido = OdometroEntrada - salida.OdometroSalida;
            TempData["Exito"] = $"Entrada registrada para {salida.Vehiculo?.Placa}. Recorrido del viaje: {recorrido:#,##0.#} km.";
            return RedirectToPage();
        }

        // Endpoint AJAX para respuesta en tiempo real al seleccionar vehículo
        public async Task<JsonResult> OnGetVehiculoInfoAsync(int idVehiculo)
        {
            int idEmpresa = GetIdEmpresa();
            var v = await _db.Vehiculos
                .Where(x => x.IdVehiculo == idVehiculo && x.IdEmpresa == idEmpresa && x.Activo && !x.Eliminado)
                .Select(x => new { x.IdVehiculo, x.Placa, x.NumeroInterno, x.KmInicial })
                .FirstOrDefaultAsync();

            if (v == null) return new JsonResult(new { success = false });

            var abierta = await _db.ControlSalidas
                .Include(c => c.Conductor)
                .Where(c => c.IdVehiculo == idVehiculo && c.Estado == "ABIERTO" && !c.Eliminado)
                .OrderByDescending(c => c.FechaHoraSalida)
                .FirstOrDefaultAsync();

            decimal ultimoKm = await ObtenerUltimoOdometroVehiculoAsync(v.IdVehiculo, v.KmInicial);

            if (abierta != null)
            {
                return new JsonResult(new
                {
                    success = true,
                    estado = "fuera",
                    idControlSalida = abierta.IdControlSalida,
                    placa = v.Placa,
                    interno = v.NumeroInterno ?? "",
                    salidaHora = abierta.FechaHoraSalida.ToString("HH:mm"),
                    salidaFecha = abierta.FechaHoraSalida.ToString("dd/MM/yyyy"),
                    salidaKm = abierta.OdometroSalida,
                    conductor = abierta.Conductor?.NombreCompleto ?? "Sin asignar",
                    destino = abierta.Destino ?? "No especificado",
                    tiempoFuera = abierta.TiempoFuera
                });
            }

            return new JsonResult(new
            {
                success = true,
                estado = "dentro",
                placa = v.Placa,
                interno = v.NumeroInterno ?? "",
                ultimoKm = ultimoKm
            });
        }

        private async Task CargarDatosAsync()
        {
            int idEmpresa = GetIdEmpresa();
            var hoy = DateTime.Today;
            var finHoy = hoy.AddDays(1);

            // 1. Vehículos activos
            var vehiculos = await _db.Vehiculos
                .Where(v => v.IdEmpresa == idEmpresa && v.Activo && !v.Eliminado)
                .OrderBy(v => v.Placa)
                .ToListAsync();

            // 2. Salidas abiertas
            FueraAhora = await _db.ControlSalidas
                .Include(c => c.Vehiculo)
                .Include(c => c.Conductor)
                .Include(c => c.Ruta)
                .Where(c => c.IdEmpresa == idEmpresa && c.Estado == "ABIERTO" && !c.Eliminado)
                .OrderByDescending(c => c.FechaHoraSalida)
                .ToListAsync();

            var idsFuera = FueraAhora.ToDictionary(f => f.IdVehiculo, f => f);

            // 3. Preparar lista de vehículos para el selector
            ListaVehiculos = new List<VehiculoEstadoDto>();
            foreach (var v in vehiculos)
            {
                bool estaFuera = idsFuera.TryGetValue(v.IdVehiculo, out var abierta);
                decimal ultimoKm = await ObtenerUltimoOdometroVehiculoAsync(v.IdVehiculo, v.KmInicial);

                ListaVehiculos.Add(new VehiculoEstadoDto
                {
                    IdVehiculo = v.IdVehiculo,
                    Placa = v.Placa,
                    NumeroInterno = v.NumeroInterno,
                    Descripcion = $"{v.Marca} {v.Modelo}".Trim(),
                    Estado = estaFuera ? "fuera" : "dentro",
                    UltimoOdometro = estaFuera && abierta != null ? abierta.OdometroSalida : ultimoKm,
                    IdControlSalidaAbierto = estaFuera && abierta != null ? abierta.IdControlSalida : null,
                    SalidaHora = estaFuera && abierta != null ? abierta.FechaHoraSalida.ToString("HH:mm") : null,
                    SalidaKm = estaFuera && abierta != null ? abierta.OdometroSalida : null,
                    SalidaConductor = estaFuera && abierta != null ? abierta.Conductor?.NombreCompleto : null,
                    SalidaDestino = estaFuera && abierta != null ? abierta.Destino : null
                });
            }

            // 4. KPIs
            KpiFuera = FueraAhora.Count;
            KpiDentro = vehiculos.Count - KpiFuera;

            // Movimientos de hoy (salidas registradas hoy + entradas registradas hoy)
            var salidasHoy = await _db.ControlSalidas
                .Include(c => c.Vehiculo)
                .Include(c => c.Conductor)
                .Where(c => c.IdEmpresa == idEmpresa && !c.Eliminado && c.FechaHoraSalida >= hoy && c.FechaHoraSalida < finHoy)
                .ToListAsync();

            var entradasHoy = await _db.ControlSalidas
                .Include(c => c.Vehiculo)
                .Where(c => c.IdEmpresa == idEmpresa && !c.Eliminado && c.FechaHoraEntrada != null && c.FechaHoraEntrada >= hoy && c.FechaHoraEntrada < finHoy)
                .ToListAsync();

            KpiMovimientosHoy = salidasHoy.Count + entradasHoy.Count;
            KpiKmHoy = entradasHoy.Sum(e => (e.OdometroEntrada ?? 0) - e.OdometroSalida);

            // 5. Últimos movimientos combinados de hoy
            MovimientosHoy = new List<MovimientoItemDto>();
            foreach (var s in salidasHoy)
            {
                MovimientosHoy.Add(new MovimientoItemDto
                {
                    Hora = s.FechaHoraSalida.ToString("HH:mm"),
                    Tipo = "SALIDA",
                    Placa = s.Vehiculo?.Placa ?? "—",
                    NumeroInterno = s.Vehiculo?.NumeroInterno,
                    Odometro = s.OdometroSalida,
                    Detalle = $"{s.Conductor?.NombreCompleto ?? "Sin conductor"} · {s.Destino ?? "Sin destino"}"
                });
            }
            foreach (var e in entradasHoy)
            {
                decimal rec = (e.OdometroEntrada ?? 0) - e.OdometroSalida;
                MovimientosHoy.Add(new MovimientoItemDto
                {
                    Hora = e.FechaHoraEntrada?.ToString("HH:mm") ?? "—",
                    Tipo = "ENTRADA",
                    Placa = e.Vehiculo?.Placa ?? "—",
                    NumeroInterno = e.Vehiculo?.NumeroInterno,
                    Odometro = e.OdometroEntrada ?? 0,
                    Detalle = $"Recorrido {rec:#,##0.#} km · salió {e.FechaHoraSalida:HH:mm}",
                    KmRecorridos = rec,
                    TiempoFuera = e.TiempoFuera
                });
            }
            MovimientosHoy = MovimientosHoy.OrderByDescending(m => m.Hora).Take(15).ToList();

            // 6. Selects
            var conductores = await _personal.PersonalParaSeleccionAsync(idEmpresa, "CONDUCTOR");

            var rutas = await _db.Rutas
                .Where(r => r.IdEmpresa == idEmpresa && r.Activo && !r.Eliminado)
                .OrderBy(r => r.Nombre)
                .ToListAsync();

            ConductoresList = new SelectList(conductores, "IdPersona", "NombreCompleto");
            RutasList = new SelectList(rutas, "Nombre", "Nombre");
        }

        private async Task<decimal> ObtenerUltimoOdometroVehiculoAsync(int idVehiculo, decimal kmInicial)
        {
            // Odómetro más reciente entre control_salidas y odometro_diario
            decimal? maxControl = await _db.ControlSalidas
                .Where(c => c.IdVehiculo == idVehiculo && !c.Eliminado)
                .Select(c => (decimal?)(c.OdometroEntrada ?? c.OdometroSalida))
                .MaxAsync();

            decimal? maxDiario = await _db.OdometrosDiarios
                .Where(o => o.IdVehiculo == idVehiculo && !o.Eliminado)
                .Select(o => (decimal?)o.KmFinal)
                .MaxAsync();

            decimal max = kmInicial;
            if (maxControl.HasValue && maxControl.Value > max) max = maxControl.Value;
            if (maxDiario.HasValue && maxDiario.Value > max) max = maxDiario.Value;
            return max;
        }

        private int GetIdEmpresa() => AuthHelper.GetEmpresaIdRequerida(HttpContext);
    }
}
