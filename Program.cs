using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;
using eGestion360Web.Services.Eventos;
using eGestion360Web.Services.Facturacion;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Add Entity Framework with SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Password Service
builder.Services.AddScoped<IPasswordService, PasswordService>();

// Add Encryption Service (for email passwords)
builder.Services.AddScoped<IEncryptionService, EncryptionService>();

// Add Email Configuration Service
builder.Services.AddScoped<IEmailConfigurationService, EmailConfigurationService>();

// Add Email Service
builder.Services.AddScoped<IEmailService, EmailService>();

// Add Email Manager Service
builder.Services.AddScoped<EmailManagerService>();

// Add Password Reset Service
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();

// Add KPI Service
builder.Services.AddScoped<KpiService>();

// ── Bus de eventos / Outbox (Fase 0 Sprint 2) ──────────────────────────────
// HttpContextAccessor: el publisher necesita el username de sesión para auditoría.
builder.Services.AddHttpContextAccessor();

// Publisher: scoped (comparte DbContext con el caller para commit atómico).
builder.Services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();

// Registro de handlers. Cada módulo agregará sus propios handlers acá.
// LoggingDomainEventHandler es el smoke-test: acepta TODOS los eventos y los loguea.
builder.Services.AddScoped<IDomainEventHandler, LoggingDomainEventHandler>();

// Handler contable (Fase 2). DESHABILITADO por ahora: su mapeo evento→cuentas todavía
// no está implementado (ver ConstruirAsientoAsync). Habilitarlo cuando existan el plan de
// cuentas por empresa y las reglas de mapeo validadas contra fuente oficial. Requiere haber
// ejecutado 2 - Script SQL/010_ct_nucleo_contable.sql.
// builder.Services.AddScoped<IDomainEventHandler, eGestion360Web.Services.Contabilidad.ContabilidadEventHandler>();

// Worker que reclama y despacha eventos pendientes del outbox.
builder.Services.AddHostedService<OutboxDispatcherBackgroundService>();

// ── Facturación (Fase 1 Sprint 3 + 4) ──────────────────────────────────────
builder.Services.AddScoped<IFacturacionService, FacturacionService>();
builder.Services.AddScoped<IPagoService, PagoService>();
builder.Services.AddScoped<INotaService, NotaService>();

// Add session support
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Initialize database - use migrations for SQL Server
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        // Try to apply any pending migrations
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Log the error but continue - database might already be initialized
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Could not apply migrations. Database may already exist or be inaccessible.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
