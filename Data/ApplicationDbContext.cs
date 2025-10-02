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
        public DbSet<Company> Companies { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Company entity
            modelBuilder.Entity<Company>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.LegalName).HasMaxLength(100);
                entity.Property(e => e.TaxId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Address).HasMaxLength(500);
                entity.Property(e => e.City).HasMaxLength(100);
                entity.Property(e => e.State).HasMaxLength(100);
                entity.Property(e => e.PostalCode).HasMaxLength(20);
                entity.Property(e => e.Country).IsRequired().HasMaxLength(2);
                entity.Property(e => e.Phone).HasMaxLength(20);
                entity.Property(e => e.Email).HasMaxLength(100);
                entity.Property(e => e.Website).HasMaxLength(200);
                entity.Property(e => e.Currency).IsRequired().HasMaxLength(10);
                entity.Property(e => e.TimeZone).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Language).HasMaxLength(10);
                entity.HasIndex(e => e.TaxId).IsUnique();
            });

            // Configure User entity
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Password).IsRequired().HasMaxLength(255);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
                
                entity.HasOne(e => e.Company)
                    .WithMany(c => c.Users)
                    .HasForeignKey(e => e.CompanyId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Seed data
            modelBuilder.Entity<User>().HasData(
                new User
                {
                    Id = 1,
                    Username = "admin",
                    Email = "admin@siptech.com",
                    Password = "admin123", // In production, this should be hashed
                    CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsActive = true
                }
            );
        }
    }
}