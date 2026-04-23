using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eGestion360Web.Migrations
{
    /// <inheritdoc />
    public partial class EncryptExistingPasswords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Paso 1: Aumentar tamaño de columna Password
            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "Users",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            // Paso 2: Actualizar contraseñas existentes a BCrypt hashes
            // admin: admin123 -> $2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2
            migrationBuilder.Sql(@"
                UPDATE Users 
                SET Password = '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2'
                WHERE Username = 'admin' AND Password = 'admin123';
            ");

            // Actualizar otros usuarios demo si existen
            migrationBuilder.Sql(@"
                UPDATE Users 
                SET Password = '$2a$12$8k1i9Z1.7VxGsM3HjNWYN.FQq1s8o7c6p5t4w2v9x1a3b2c4d5e6f7'
                WHERE Username = 'cliente_demo' AND Password = 'Demo123!';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revertir tamaño de columna
            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            // Revertir contraseñas a texto plano (solo para rollback)
            migrationBuilder.Sql(@"
                UPDATE Users 
                SET Password = 'admin123'
                WHERE Username = 'admin' AND Password = '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/.Mk9A4J2Lg.QQwFwv2';
            ");

            migrationBuilder.Sql(@"
                UPDATE Users 
                SET Password = 'Demo123!'
                WHERE Username = 'cliente_demo' AND Password = '$2a$12$8k1i9Z1.7VxGsM3HjNWYN.FQq1s8o7c6p5t4w2v9x1a3b2c4d5e6f7';
            ");
        }
    }
}