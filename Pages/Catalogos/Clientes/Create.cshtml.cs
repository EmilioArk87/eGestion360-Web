using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Services;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Pages.Catalogos.Clientes
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IVinculoService _vinculos;
        private readonly IPersonaConsultaService _consulta;

        public CreateModel(ApplicationDbContext db, IVinculoService vinculos, IPersonaConsultaService consulta)
        {
            _db = db;
            _vinculos = vinculos;
            _consulta = consulta;
        }

        [BindProperty]
        public Cliente Cliente { get; set; } = new Cliente
        {
            Activo = true,
            Tipo = "natural",
            MonedaIsoDefault = "HNL"
        };

        /// <summary>
        /// Identificación de la persona, solo para un cliente natural con documento: la ficha de persona se busca por
        /// el documento y se reutiliza si ya existe. Sin documento (un consumidor final) o si el cliente es jurídico,
        /// no se usa y el cliente se crea como siempre, con su razón social.
        /// </summary>
        [BindProperty]
        public PersonaDatosInput Persona { get; set; } = new() { TipoDocumento = "DNI" };

        /// <summary>El usuario vio las personas parecidas y confirma que es otra.</summary>
        [BindProperty]
        public bool ConfirmarQueEsOtraPersona { get; set; }

        public IReadOnlyList<PersonaParecida> Parecidas { get; private set; } = Array.Empty<PersonaParecida>();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeCrear(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            await CargarListasAsync(AuthHelper.GetEmpresaId(HttpContext) ?? 0);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeCrear(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            // Un cliente natural con documento se registra con su ficha de persona.
            if (Cliente.Tipo == "natural" && !string.IsNullOrWhiteSpace(Persona.Documento))
                return await RegistrarConFichaAsync(idEmpresa);

            // Validar unicidad de código dentro del tenant
            var existeCodigo = await _db.Clientes
                .AnyAsync(c => c.IdEmpresa == idEmpresa && !c.Eliminado && c.Codigo == Cliente.Codigo);

            if (existeCodigo)
                ModelState.AddModelError("Cliente.Codigo", "Ya existe un cliente con este código en la empresa.");

            if (!ModelState.IsValid)
            {
                await CargarListasAsync(idEmpresa);
                return Page();
            }

            var now = DateTime.UtcNow;
            var user = HttpContext.Session.GetString("Username") ?? "system";

            Cliente.IdEmpresa = idEmpresa;
            Cliente.MonedaIsoDefault = (Cliente.MonedaIsoDefault ?? "HNL").ToUpperInvariant();
            Cliente.Eliminado = false;
            Cliente.FechaEliminado = null;
            Cliente.CreadoPor = user;
            Cliente.FechaCreacion = now;
            Cliente.ModificadoPor = null;
            Cliente.FechaModificacion = null;

            // Esta ruta no usa persona: nunca se enlaza un vínculo que venga del formulario.
            Cliente.IdPersonaEmpresa = null;
            Cliente.Vinculo = null;

            _db.Clientes.Add(Cliente);
            await _db.SaveChangesAsync();

            TempData["ClientesMessage"] = "Cliente creado correctamente.";
            return RedirectToPage("Index");
        }

        private async Task<IActionResult> RegistrarConFichaAsync(int idEmpresa)
        {
            // La razón social de este cliente es el nombre de la persona: no se pide.
            ModelState.Remove("Cliente.RazonSocial");

            if (!ModelState.IsValid)
            {
                await CargarListasAsync(idEmpresa);
                return Page();
            }

            var resultado = await _vinculos.RegistrarClienteNaturalAsync(new RegistrarClienteNaturalInput
            {
                IdEmpresa = idEmpresa,
                Usuario = HttpContext.Session.GetString("Username") ?? "system",
                ConfirmarQueEsOtraPersona = ConfirmarQueEsOtraPersona,
                Persona = Persona,
                Cliente = new ClienteDatosInput
                {
                    Codigo = Cliente.Codigo,
                    NombreComercial = Cliente.NombreComercial,
                    IdentificadorFiscal = Cliente.IdentificadorFiscal,
                    Email = Cliente.Email,
                    Telefono = Cliente.Telefono,
                    Direccion = Cliente.Direccion,
                    Ciudad = Cliente.Ciudad,
                    MonedaIsoDefault = Cliente.MonedaIsoDefault,
                    IdCondicionPagoDefault = Cliente.IdCondicionPagoDefault,
                    LimiteCredito = Cliente.LimiteCredito
                }
            });

            switch (resultado.Estado)
            {
                case EstadoRegistrarCliente.Creado:
                case EstadoRegistrarCliente.Vinculado:
                case EstadoRegistrarCliente.Reactivado:
                    TempData["ClientesMessage"] = resultado.Estado switch
                    {
                        EstadoRegistrarCliente.Vinculado =>
                            "Cliente registrado. La persona ya estaba en el sistema: se reutilizó su ficha y no se cambiaron sus datos personales.",
                        EstadoRegistrarCliente.Reactivado =>
                            "El cliente estaba dado de baja y se reactivó con su misma ficha.",
                        _ => "Cliente creado correctamente."
                    };
                    if (resultado.Advertencias.Count > 0)
                        TempData["ClientesAvisos"] = string.Join(" · ", resultado.Advertencias);
                    return RedirectToPage("Index");

                case EstadoRegistrarCliente.RequiereConfirmacion:
                    Parecidas = resultado.Parecidas;
                    ConfirmarQueEsOtraPersona = false;
                    break;

                default:
                    foreach (var error in resultado.Errores)
                        ModelState.AddModelError(error.Campo, error.Mensaje);
                    break;
            }

            await CargarListasAsync(idEmpresa);
            return Page();
        }

        private async Task CargarListasAsync(int idEmpresa)
        {
            var monedas = await _db.Monedas
                .Where(m => m.Activo)
                .OrderBy(m => m.CodigoIso)
                .Select(m => new SelectListItem { Value = m.CodigoIso, Text = $"{m.CodigoIso} - {m.Nombre}" })
                .ToListAsync();
            ViewData["Monedas"] = new SelectList(monedas, "Value", "Text");

            var condiciones = await _db.CondicionesPago
                .Where(c => c.IdEmpresa == idEmpresa && c.Activo && !c.Eliminado)
                .OrderBy(c => c.Nombre)
                .Select(c => new SelectListItem { Value = c.IdCondicionPago.ToString(), Text = c.Nombre })
                .ToListAsync();
            ViewData["CondicionesPago"] = new SelectList(condiciones, "Value", "Text");

            var catalogos = await _consulta.CatalogosAsync(idEmpresa);
            ViewData["TiposDocumento"] = new SelectList(catalogos.TiposDocumento, "Valor", "Texto");
        }
    }
}
