using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Eventos;
using eGestion360Web.Services;

namespace eGestion360Web.Pages.Admin.Outbox
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public List<DomainEvent> Eventos { get; set; } = new();
        public Dictionary<string, int> ConteoPorStatus { get; set; } = new();

        [BindProperty(SupportsGet = true)] public string? Status { get; set; }
        [BindProperty(SupportsGet = true)] public string? EventType { get; set; }
        [BindProperty(SupportsGet = true)] public int? EmpresaId { get; set; }
        [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

        public int PageSize { get; private set; } = 50;
        public int TotalPages { get; private set; }
        public int Total { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext))
                return RedirectToPage("/Login");
            if (!AuthHelper.IsAdmin(HttpContext))
                return RedirectToPage("/MainMenu");

            // Conteos por status (resumen superior)
            ConteoPorStatus = await _db.DomainEvents
                .GroupBy(e => e.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            IQueryable<DomainEvent> q = _db.DomainEvents.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(Status))    q = q.Where(e => e.Status == Status);
            if (!string.IsNullOrWhiteSpace(EventType)) q = q.Where(e => e.EventType.Contains(EventType));
            if (EmpresaId.HasValue && EmpresaId > 0)   q = q.Where(e => e.IdEmpresa == EmpresaId.Value);

            Total = await q.CountAsync();
            TotalPages = (int)Math.Ceiling(Total / (double)PageSize);
            if (Pagina < 1) Pagina = 1;
            if (TotalPages > 0 && Pagina > TotalPages) Pagina = TotalPages;

            Eventos = await q
                .OrderByDescending(e => e.IdEvento)
                .Skip((Pagina - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostResetDeadAsync()
        {
            if (!AuthHelper.IsAuthenticated(HttpContext) || !AuthHelper.IsAdmin(HttpContext))
                return Forbid();

            var dead = await _db.DomainEvents
                .Where(e => e.Status == DomainEventStatus.Dead)
                .ToListAsync();

            foreach (var e in dead)
            {
                e.Status = DomainEventStatus.Pending;
                e.Intentos = 0;
                e.LockedUntil = null;
                e.WorkerId = null;
                e.ProximoIntentoEn = null;
            }
            await _db.SaveChangesAsync();

            TempData["OutboxMessage"] = $"{dead.Count} eventos en estado dead reactivados.";
            return RedirectToPage("Index");
        }
    }
}
