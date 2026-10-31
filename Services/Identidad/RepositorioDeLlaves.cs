using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Plataforma;

namespace eGestion360Web.Services.Identidad
{
    /// <summary>
    /// Guarda las llaves de ASP.NET Core Data Protection en <c>saas.llaves_proteccion_datos</c> (D48). Con llaves persistidas, la
    /// cookie de autenticación, la de sesión, los tokens antifalsificación y los secretos de MFA siguen siendo válidos después de
    /// reiniciar o reciclar el sitio. Las llaves van sin cifrar en la base mientras la aplicación sea su dueña (Somee); en Azure pasan a
    /// Key Vault (ADR-016).
    ///
    /// Data Protection crea este repositorio una sola vez y lo usa desde cualquier hilo, así que cada operación abre su propio
    /// ámbito de servicios (un <see cref="ApplicationDbContext"/> propio). La tabla es del plano de control: no necesita tenant.
    /// </summary>
    public sealed class RepositorioDeLlaves : IXmlRepository
    {
        private readonly IServiceScopeFactory _ambitos;

        public RepositorioDeLlaves(IServiceScopeFactory ambitos) => _ambitos = ambitos;

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            using var ambito = _ambitos.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var llaves = db.LlavesProteccionDatos.AsNoTracking().OrderBy(l => l.IdLlave).Select(l => l.Xml).ToList();
            return llaves.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => XElement.Parse(x!)).ToList().AsReadOnly();
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            using var ambito = _ambitos.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.LlavesProteccionDatos.Add(new LlaveProteccionDatos { NombreAmistoso = friendlyName, Xml = element.ToString(SaveOptions.DisableFormatting) });
            db.SaveChanges();
        }
    }
}
