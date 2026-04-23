using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Services;

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
