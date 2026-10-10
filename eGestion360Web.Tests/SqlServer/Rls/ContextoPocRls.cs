using Microsoft.EntityFrameworkCore;

namespace eGestion360Web.Tests.SqlServer.Rls
{
    public sealed class ClientePoc
    {
        public int IdCliente { get; set; }
        public int IdTenant { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    public sealed class FacturaPoc
    {
        public int IdFactura { get; set; }
        public int IdTenant { get; set; }
        public int IdCliente { get; set; }
        public string Numero { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }

    /// <summary>Tasa de la tabla mixta: <see cref="IdTenant"/> nulo es una tasa oficial, visible para todos.</summary>
    public sealed class TasaPoc
    {
        public int IdTasa { get; set; }
        public int? IdTenant { get; set; }
        public string Moneda { get; set; } = string.Empty;
        public decimal Valor { get; set; }
    }

    /// <summary>
    /// Contexto de EF del laboratorio de RLS. Lleva el filtro global por tenant que tendrá la aplicación en F1 (primera
    /// barrera); la segunda barrera es la política de la base. Cada instancia trabaja para un solo
    /// <see cref="AmbitoDeConexion"/>, como un contexto por petición.
    /// </summary>
    public sealed class ContextoPocRls : DbContext
    {
        private readonly int? _idTenant;

        private ContextoPocRls(DbContextOptions<ContextoPocRls> options, AmbitoDeConexion ambito) : base(options)
        {
            _idTenant = ambito.IdTenant;
        }

        /// <summary>Contexto con los dos interceptores (contexto de sesión y tenant al guardar).</summary>
        public static ContextoPocRls Crear(string cadena, AmbitoDeConexion ambito)
        {
            var opciones = new DbContextOptionsBuilder<ContextoPocRls>()
                .UseSqlServer(cadena)
                .AddInterceptors(new InterceptorAmbitoDeConexion(ambito), new InterceptorTenantAlGuardar(ambito))
                .Options;
            return new ContextoPocRls(opciones, ambito);
        }

        public DbSet<ClientePoc> Clientes => Set<ClientePoc>();
        public DbSet<FacturaPoc> Facturas => Set<FacturaPoc>();
        public DbSet<TasaPoc> Tasas => Set<TasaPoc>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ClientePoc>(e =>
            {
                e.ToTable("poc_clientes");
                e.HasKey(c => c.IdCliente);
                e.HasAlternateKey(c => new { c.IdTenant, c.IdCliente });
                e.Property(c => c.IdCliente).HasColumnName("id_cliente");
                e.Property(c => c.IdTenant).HasColumnName("id_tenant");
                e.Property(c => c.Nombre).HasColumnName("nombre").HasMaxLength(100);
                e.HasQueryFilter(c => c.IdTenant == _idTenant);
            });

            modelBuilder.Entity<FacturaPoc>(e =>
            {
                e.ToTable("poc_facturas");
                e.HasKey(f => f.IdFactura);
                e.Property(f => f.IdFactura).HasColumnName("id_factura");
                e.Property(f => f.IdTenant).HasColumnName("id_tenant");
                e.Property(f => f.IdCliente).HasColumnName("id_cliente");
                e.Property(f => f.Numero).HasColumnName("numero").HasMaxLength(40);
                e.Property(f => f.Total).HasColumnName("total").HasPrecision(18, 2);
                e.HasOne<ClientePoc>().WithMany()
                    .HasForeignKey(f => new { f.IdTenant, f.IdCliente })
                    .HasPrincipalKey(c => new { c.IdTenant, c.IdCliente });
                e.HasQueryFilter(f => f.IdTenant == _idTenant);
            });

            modelBuilder.Entity<TasaPoc>(e =>
            {
                e.ToTable("poc_tasas");
                e.HasKey(t => t.IdTasa);
                e.Property(t => t.IdTasa).HasColumnName("id_tasa");
                e.Property(t => t.IdTenant).HasColumnName("id_tenant");
                e.Property(t => t.Moneda).HasColumnName("moneda").HasMaxLength(3).IsFixedLength();
                e.Property(t => t.Valor).HasColumnName("valor").HasPrecision(18, 6);
                e.HasQueryFilter(t => t.IdTenant == null || t.IdTenant == _idTenant);
            });
        }
    }
}
