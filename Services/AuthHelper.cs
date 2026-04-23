using Microsoft.AspNetCore.Http;

namespace eGestion360Web.Services
{
    public static class AuthHelper
    {
        public const string AdminRole = "admin";

        public static bool IsAuthenticated(HttpContext context)
            => !string.IsNullOrEmpty(context.Session.GetString("UserId"));

        public static bool IsAdmin(HttpContext context)
            => string.Equals(context.Session.GetString("Role"), AdminRole, StringComparison.OrdinalIgnoreCase);

        public static string ResolveRole(string username)
            => string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase) ? AdminRole : "user";
    }
}
