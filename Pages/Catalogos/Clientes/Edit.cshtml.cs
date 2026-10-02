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
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IVinculoService _vinculos;

        public EditModel(ApplicationDbContext db, IVinculoService vinculos)
        {
            _db = db;
            _vinculos = vinculos;
        }

        [BindProperty]
        public Cliente Cliente { get; set; } = new();

        /// <summary>El cliente está enlazado a una persona: su razón social es el nombre de ella y no se edita aquí.</summary>
        public bool Enlazado { get; private set; }

        /// <summary>Lo que se muestra de la persona de un cliente enlazado.</summary>
        public InfoPersonaCliente? Persona { get; private set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeEditar(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            var cliente = await _db.Clientes
                .FirstOrDefaultAsync(c => c.IdCliente == id && c.IdEmpresa == idEmpresa && !c.Eliminado);

            if (cliente == null) return RedirectToPage("Index");

            Cliente = cliente;
            await PrepararAsync(idEmpresa, cliente);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");

            int idEmpresa = AuthHelper.GetEmpresaId(HttpContext) ?? 0;

            if (!AuthHelper.HasModulo(HttpContext, "catalogos") || !AuthHelper.PuedeEditar(HttpContext, "catalogos"))
                return RedirectToPage("/Catalogos/Index");

            var dbCliente = await _db.Clientes
                .FirstOrDefaultAsync(c => c.IdCliente == id && c.IdEmpresa == idEmpresa && !c.Eliminado);

            if (dbCliente == null) return RedirectToPage("Index");

            // De un cliente enlazado a una persona, la razón social y el tipo no vienen del formulario.
            var enlazado = dbCliente.IdPersonaEmpresa != null;
            if (enlazado)
            {
                ModelState.Remove("Cliente.RazonSocial");
                ModelState.Remove("Cliente.Tipo");
            }

            // Validar unicidad de código (excluyendo el propio registro)
            var existeCodigo = await _db.Clientes
                .AnyAsync(c => c.IdEmpresa == idEmpresa && !c.Eliminado
                            && c.IdCliente != id && c.Codigo == Cliente.Codigo);

            if (existeCodigo)
                ModelState.AddModelError("Cliente.Codigo", "Ya existe otro cliente con este código en la empresa.");

            if (!ModelState.IsValid)
            {
                Cliente.IdCliente = id;
                Cliente.IdEmpresa = idEmpresa;
                if (enlazado)
                {
                    Cliente.RazonSocial = dbCliente.RazonSocial;
                    Cliente.Tipo = dbCliente.Tipo;
                    Cliente.IdPersonaEmpresa = dbCliente.IdPersonaEmpresa;
                }
                await PrepararAsync(idEmpresa, dbCliente);
                return Page();
            }

            var usuario = HttpContext.Session.GetString("Username") ?? "system";

            dbCliente.Codigo                = Cliente.Codigo;
            if (!enlazado)
            {
                dbCliente.RazonSocial       = Cliente.RazonSocial;
                dbCliente.Tipo              = Cliente.Tipo;
            }
            dbCliente.NombreComercial       = Cliente.NombreComercial;
            dbCliente.IdentificadorFiscal   = Cliente.IdentificadorFiscal;
            dbCliente.Email                 = Cliente.Email;
            dbCliente.Telefono              = Cliente.Telefono;
            dbCliente.Direccion             = Cliente.Direccion;
            dbCliente.Ciudad                = Cliente.Ciudad;
            dbCliente.MonedaIsoDefault      = (Cliente.MonedaIsoDefault ?? "HNL").ToUpperInvariant();
            dbCliente.IdCondicionPagoDefault= Cliente.IdCondicionPagoDefault;
            dbCliente.LimiteCredito         = Cliente.LimiteCredito;
            dbCliente.ModificadoPor         = usuario;
            dbCliente.FechaModificacion     = DateTime.UtcNow;

            // Activar o desactivar a un cliente enlazado a una persona abre o cierra su relación (su vínculo): lo hace
            // el servicio, que guarda también los demás cambios de esta pantalla. Un cliente sin persona sigue como siempre.
            var cambiaEstado = enlazado && dbCliente.Activo != Cliente.Activo;
            if (!enlazado)
                dbCliente.Activo = Cliente.Activo;

            try
            {
                if (cambiaEstado)
                {
                    var estado = Cliente.Activo
                        ? await _vinculos.ReactivarClienteAsync(idEmpresa, id, usuario)
                        : await _vinculos.TerminarClienteAsync(new TerminarClienteInput { IdEmpresa = idEmpresa, IdCliente = id, Usuario = usuario });

                    if (estado.SinCambios)
                    {
                        await _db.SaveChangesAsync();   // ya estaba en ese estado: solo quedan los otros cambios
                    }
                    else if (!estado.Ok)
                    {
                        ModelState.AddModelError(string.Empty, estado.Mensaje);
                        Cliente.IdCliente = id;
                        Cliente.IdEmpresa = idEmpresa;
                        Cliente.RazonSocial = dbCliente.RazonSocial;
                        Cliente.Tipo = dbCliente.Tipo;
                        Cliente.IdPersonaEmpresa = dbCliente.IdPersonaEmpresa;
                        await PrepararAsync(idEmpresa, dbCliente);
                        return Page();
                    }
                }
                else
                {
                    await _db.SaveChangesAsync();
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, "El registro fue modificado por otro usuario. Recargue e intente de nuevo.");
                Enlazado = enlazado;
                ViewData["Enlazado"] = enlazado;
                await CargarListasAsync(idEmpresa);
                return Page();
            }

            TempData["ClientesMessage"] = "Cliente actualizado correctamente.";
            return RedirectToPage("Index");
        }

        /// <summary>Listas del formulario y, si el cliente está enlazado a una persona, los datos de ella para mostrar.</summary>
        private async Task PrepararAsync(int idEmpresa, Cliente cliente)
        {
            Enlazado = cliente.IdPersonaEmpresa != null;
            ViewData["Enlazado"] = Enlazado;

            if (Enlazado)
            {
                Persona = await _db.PersonaEmpresas.AsNoTracking()
                    .Where(v => v.IdPersonaEmpresa == cliente.IdPersonaEmpresa)
                    .Select(v => new InfoPersonaCliente(
                        v.Persona.Documentos.Where(d => !d.Eliminado).OrderByDescending(d => d.EsPrincipal).Select(d => d.TipoDocumento).FirstOrDefault(),
                        v.Persona.Documentos.Where(d => !d.Eliminado).OrderByDescending(d => d.EsPrincipal).Select(d => d.Numero).FirstOrDefault(),
                        v.Persona.EstadoIdentidad,
                        v.FechaInicio, v.FechaFin, v.MotivoFin))
                    .FirstOrDefaultAsync();
            }

            await CargarListasAsync(idEmpresa);
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
        }

        /// <param name="TipoDocumento">Tipo y número del documento principal de la persona, o nulos si no tiene.</param>
        /// <param name="EstadoIdentidad">«verificada» (tiene un documento de identidad) o «pendiente».</param>
        public sealed record InfoPersonaCliente(
            string? TipoDocumento, string? Numero, string EstadoIdentidad, DateOnly? Desde, DateOnly? Hasta, string? Motivo);
    }
}
