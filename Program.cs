using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;
using eGestion360Web.Services.Eventos;
using eGestion360Web.Services.Facturacion;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// AdminOnlyPageFilter cierra el acceso anónimo a las páginas de mantenimiento
// (/ResetAdmin, /DebugUsers, /ResetCodesHistory, config SMTP…). Ver el filtro.
builder.Services.AddRazorPages()
    .AddMvcOptions(options => options.Filters.Add<AdminOnlyPageFilter>());

// Add Entity Framework with SQL Server.
// La cadena ya no vive en appsettings.json: viene de la variable de entorno
// ConnectionStrings__DefaultConnection (ver 1 - Documetacion/CONFIGURACION_SECRETOS.md).
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Falta la variable de entorno ConnectionStrings__DefaultConnection. " +
        "Ver 1 - Documetacion/CONFIGURACION_SECRETOS.md.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

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

// ── Inicialización de base de datos ────────────────────────────────────────
// OJO: context.Database.Migrate() ejecuta DDL/DML contra la BD real (eBD_SPD).
// Por la "regla de oro" del proyecto, el esquema NO se modifica de forma
// implícita al arrancar. Para aplicar migraciones hay que habilitarlo
// explícitamente con "Database:AutoMigrate": true (previa aprobación /alerta-bd).
// Por defecto solo se reporta si hay migraciones pendientes, sin tocar nada.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    if (app.Configuration.GetValue("Database:AutoMigrate", false))
    {
        try
        {
            context.Database.Migrate();
            logger.LogInformation("Migraciones EF aplicadas correctamente.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudieron aplicar las migraciones EF.");
        }
    }
    else
    {
        try
        {
            var pendientes = context.Database.GetPendingMigrations().ToList();
            if (pendientes.Count > 0)
            {
                logger.LogWarning(
                    "AutoMigrate deshabilitado. Migraciones EF pendientes ({Cantidad}): {Migraciones}",
                    pendientes.Count, string.Join(", ", pendientes));
            }
            else
            {
                logger.LogInformation("Base de datos al día: no hay migraciones EF pendientes.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo consultar el estado de las migraciones EF.");
        }
    }
}

// Configure the HTTP request pipeline.
// Forzar HTTPS sólo sirve si el hosting termina TLS. En Somee el puerto 443 acepta la
// conexión pero el handshake falla, así que redirigir deja el portal inaccesible: todo
// HTTP responde 307 hacia una URL que no contesta. El interruptor permite reactivarlo
// apenas haya certificado, sin tocar código ni volver a compilar.
var forzarHttps = app.Configuration.GetValue("Seguridad:ForzarHttps", true);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    if (forzarHttps)
    {
        // HSTS sin TLS operativo dejaría a los navegadores forzando HTTPS por 30 días,
        // bloqueando el acceso incluso después de apagar la redirección.
        app.UseHsts();
    }
}

if (forzarHttps)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
