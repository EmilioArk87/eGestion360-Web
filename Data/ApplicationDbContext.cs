using Microsoft.EntityFrameworkCore;
using eGestion360Web.Models;

namespace eGestion360Web.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure User entity to map to existing 'Users' table.
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("Id");
                entity.Property(e => e.Username).HasColumnName("Username").IsRequired().HasMaxLength(50);
                entity.Property(e => e.Email).HasColumnName("Email").IsRequired().HasMaxLength(100);
                entity.Property(e => e.Password).HasColumnName("Password").IsRequired().HasMaxLength(255);
                entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt");
                entity.Property(e => e.IsActive).HasColumnName("IsActive").IsRequired();
                entity.Property(e => e.RequirePasswordChange).HasColumnName("RequirePasswordChange").IsRequired().HasDefaultValue(false);

                // Ignored for the legacy Users schema used by this version.
                entity.Ignore(e => e.PasswordHash);
                entity.Ignore(e => e.PasswordSalt);
                entity.Ignore(e => e.PasswordAlgorithm);
            });
        }
    }
}