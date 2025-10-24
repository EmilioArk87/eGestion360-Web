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

            // Configure User entity to map to 'usuarios' table
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("usuarios"); // Map to 'usuarios' table instead of 'Users'
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Password).IsRequired().HasMaxLength(255);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
            });

            // Note: Seed data removed as we're now using an existing 'usuarios' table
        }
    }
}