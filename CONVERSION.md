# Conversión de MVC a Razor Pages (.NET 8)

## Resumen
Este proyecto ha sido convertido exitosamente de **ASP.NET Core MVC** a **ASP.NET Core Razor Pages** (.NET 8).

## ¿Por qué Razor Pages en lugar de ASPX?
**ASPX (ASP.NET Web Forms) NO es compatible con .NET 8.** Web Forms fue descontinuado y solo funciona en .NET Framework 4.x.

**Razor Pages es el equivalente moderno** de Web Forms en .NET 8:
- ✅ Arquitectura basada en páginas (no en controladores)
- ✅ Archivos code-behind (.cshtml.cs) como los .aspx.cs
- ✅ Modelo de programación más simple que MVC
- ✅ Familiar para desarrolladores de Web Forms

## Estructura del Proyecto

### Antes (MVC)
```
Controllers/
  - HomeController.cs
  - AccountController.cs
Views/
  - Home/Index.cshtml
  - Account/Login.cshtml
  - Shared/_Layout.cshtml
```

### Ahora (Razor Pages)
```
Pages/
  - Index.cshtml + Index.cshtml.cs
  - Login.cshtml + Login.cshtml.cs
  - About.cshtml + About.cshtml.cs
  - Products.cshtml + Products.cshtml.cs
  - Contact.cshtml + Contact.cshtml.cs
  - Logout.cshtml + Logout.cshtml.cs
  - Error.cshtml + Error.cshtml.cs
  - Shared/_Layout.cshtml
  - Shared/_NavigationPartial.cshtml
```

## Cambios Principales

### 1. Program.cs
- Cambiado de `AddControllersWithViews()` a `AddRazorPages()`
- Cambiado de `MapControllerRoute()` a `MapRazorPages()`

### 2. Rutas
- **Antes**: `/Home/Index`, `/Account/Login`
- **Ahora**: `/Index`, `/Login` (más simple)

### 3. Code-Behind (PageModel)
Cada página tiene su propia clase PageModel con métodos:
- `OnGet()` - Similar a Page_Load en Web Forms
- `OnPost()` - Para manejar formularios POST

Ejemplo:
```csharp
public class LoginModel : PageModel
{
    [BindProperty]
    public string Username { get; set; }
    
    [BindProperty]
    public string Password { get; set; }
    
    public IActionResult OnGet()
    {
        // Lógica al cargar la página
        return Page();
    }
    
    public async Task<IActionResult> OnPostAsync()
    {
        // Lógica al enviar formulario
        if (ModelState.IsValid)
        {
            // Validar credenciales
        }
        return Page();
    }
}
```

## Funcionalidades Implementadas
- ✅ Sistema de login con autenticación
- ✅ Manejo de sesiones
- ✅ Validación de formularios
- ✅ Navegación entre páginas
- ✅ Layout compartido
- ✅ Página de error

## Cómo Ejecutar
```bash
dotnet run
```

Navega a: http://localhost:5000

**Credenciales de prueba:**
- Usuario: `admin`
- Contraseña: `admin123`

## Ventajas de Razor Pages sobre MVC
1. **Más Simple**: No hay separación Controller/View
2. **Organización**: Cada página es auto-contenida
3. **Code-Behind**: Familiar para desarrolladores de Web Forms
4. **Menos Código**: Menos archivos y configuración
5. **SEO Amigable**: URLs más limpias

## Recursos
- [Documentación Oficial Razor Pages](https://docs.microsoft.com/aspnet/core/razor-pages)
- [Migración de Web Forms a Razor Pages](https://docs.microsoft.com/aspnet/core/migration/webforms)
