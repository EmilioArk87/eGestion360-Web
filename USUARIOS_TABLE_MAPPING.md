# Mapping to 'usuarios' Table

## What Changed

The application has been configured to use the existing `usuarios` table in the database for user authentication instead of the `Users` table.

## Changes Made

### 1. ApplicationDbContext.cs
- Added `entity.ToTable("usuarios")` to map the User entity to the `usuarios` table
- Removed seed data configuration since we're using an existing table

## Expected Database Schema

The application expects the `usuarios` table to have the following columns:

| Column Name | Type | Description |
|------------|------|-------------|
| Id | int (Primary Key, Identity) | Unique user identifier |
| Username | nvarchar(50) | Username for login (unique) |
| Email | nvarchar(100) | User email address (unique) |
| Password | nvarchar(255) | User password (plain text - should be hashed in production) |
| CreatedAt | datetime2 | Account creation date |
| IsActive | bit | Whether the user account is active |

## Column Name Mapping

If your `usuarios` table uses different column names (e.g., Spanish names like `usuario`, `correo`, `contraseña`), you will need to add column mapping in `ApplicationDbContext.cs`:

```csharp
modelBuilder.Entity<User>(entity =>
{
    entity.ToTable("usuarios");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id"); // if needed
    entity.Property(e => e.Username).HasColumnName("usuario"); // if needed
    entity.Property(e => e.Email).HasColumnName("correo"); // if needed
    entity.Property(e => e.Password).HasColumnName("contraseña"); // if needed
    entity.Property(e => e.CreatedAt).HasColumnName("fecha_creacion"); // if needed
    entity.Property(e => e.IsActive).HasColumnName("activo"); // if needed
    // ... rest of configuration
});
```

## Testing

To test the changes:
1. Ensure the `usuarios` table exists in the database with the expected schema
2. Add test users to the `usuarios` table
3. Run the application: `dotnet run`
4. Navigate to `/Login`
5. Try logging in with a user from the `usuarios` table

## Security Note

⚠️ **WARNING**: The current implementation stores passwords in plain text. For production use, passwords should be hashed using a secure algorithm like bcrypt or PBKDF2.
