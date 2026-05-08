using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace eGestion360Web.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'Role')
                BEGIN
                    ALTER TABLE [Users] ADD [Role] nvarchar(20) NOT NULL DEFAULT N'user'
                END");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'monedas')
                BEGIN
                    CREATE TABLE [monedas] (
                        [codigo_iso] nvarchar(3) NOT NULL,
                        [nombre] nvarchar(100) NOT NULL,
                        [simbolo] nvarchar(10) NOT NULL,
                        [activo] bit NOT NULL,
                        CONSTRAINT [PK_monedas] PRIMARY KEY ([codigo_iso])
                    )
                END");

            // Asignar rol 'user' a todos los usuarios, luego promover a 'admin' al usuario admin
            migrationBuilder.Sql("UPDATE [Users] SET [Role] = 'user' WHERE [Role] = '' OR [Role] IS NULL");
            migrationBuilder.Sql("UPDATE [Users] SET [Role] = 'admin' WHERE Username = 'admin'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.tables WHERE name = 'monedas')
                    DROP TABLE [monedas]");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'Role')
                    ALTER TABLE [Users] DROP COLUMN [Role]");
        }
    }
}
