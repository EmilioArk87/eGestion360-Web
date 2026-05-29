using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Eventos;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.Outbox
{
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public DetailsModel(ApplicationDbContext db) => _db = db;

        public DomainEvent? Evento { get; set; }
        public string PayloadPretty { get; set; } = "";

        public async Task<IActionResult> OnGetAsync(long id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext)) return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))         return RedirectToPage("/MainMenu");

            Evento = await _db.DomainEvents.AsNoTracking().FirstOrDefaultAsync(e => e.IdEvento == id);
            if (Evento == null) return RedirectToPage("Index");

            try
            {
                using var doc = JsonDocument.Parse(Evento.Payload);
                PayloadPretty = JsonSerializer.Serialize(doc.RootElement,
                    new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                PayloadPretty = Evento.Payload;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostRetryAsync(long id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext) || !AuthHelper.IsAdmin(HttpContext))
                return Forbid();

            var e = await _db.DomainEvents.FirstOrDefaultAsync(x => x.IdEvento == id);
            if (e == null) return RedirectToPage("Index");

            e.Status            = DomainEventStatus.Pending;
            e.LockedUntil       = null;
            e.WorkerId          = null;
            e.ProximoIntentoEn  = null;
            // Intentos NO se resetea para preservar el historial, pero si quedó dead y
            // queremos darle más oportunidades, reseteamos:
            if (e.Intentos >= e.MaxIntentos) e.Intentos = 0;

            await _db.SaveChangesAsync();
            TempData["OutboxMessage"] = $"Evento {id} reencolado para reintento.";
            return RedirectToPage("Details", new { id });
        }

        public async Task<IActionResult> OnPostMarkProcessedAsync(long id)
        {
            if (!AuthHelper.IsAuthenticated(HttpContext) || !AuthHelper.IsAdmin(HttpContext))
                return Forbid();

            var e = await _db.DomainEvents.FirstOrDefaultAsync(x => x.IdEvento == id);
            if (e == null) return RedirectToPage("Index");

            e.Status        = DomainEventStatus.Processed;
            e.ProcessedAt   = DateTime.UtcNow;
            e.LockedUntil   = null;
            e.WorkerId      = null;
            e.ProximoIntentoEn = null;
            e.UltimoError   = (e.UltimoError ?? "") + " [marcado manualmente como processed]";

            await _db.SaveChangesAsync();
            TempData["OutboxMessage"] = $"Evento {id} marcado como procesado manualmente.";
            return RedirectToPage("Details", new { id });
        }
    }
}
