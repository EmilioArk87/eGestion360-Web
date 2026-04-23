using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;

namespace eGestion360Web.Pages
{
    public class DebugUsersModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public DebugUsersModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<User> Users { get; set; } = new List<User>();

        public async Task OnGetAsync()
        {
            Users = await _context.Users.ToListAsync();
        }
    }
}