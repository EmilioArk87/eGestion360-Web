using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eGestion360Web.Migrations
{
    /// <inheritdoc />
    public partial class RenameUsersTableToUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Users",
                table: "Users");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "usuarios");

            migrationBuilder.RenameIndex(
                name: "IX_Users_Username",
                table: "usuarios",
                newName: "IX_usuarios_Username");

            migrationBuilder.RenameIndex(
                name: "IX_Users_Email",
                table: "usuarios",
                newName: "IX_usuarios_Email");

            migrationBuilder.AddPrimaryKey(
                name: "PK_usuarios",
                table: "usuarios",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_usuarios",
                table: "usuarios");

            migrationBuilder.RenameTable(
                name: "usuarios",
                newName: "Users");

            migrationBuilder.RenameIndex(
                name: "IX_usuarios_Username",
                table: "Users",
                newName: "IX_Users_Username");

            migrationBuilder.RenameIndex(
                name: "IX_usuarios_Email",
                table: "Users",
                newName: "IX_Users_Email");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Users",
                table: "Users",
                column: "Id");
        }
    }
}
