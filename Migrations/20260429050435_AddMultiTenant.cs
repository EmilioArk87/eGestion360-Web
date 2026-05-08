using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace eGestion360Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "user",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaRolId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "empresa_roles",
                columns: table => new
                {
                    id_rol = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    id_empresa = table.Column<int>(type: "int", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    es_admin = table.Column<bool>(type: "bit", nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_empresa_roles", x => x.id_rol);
                    table.ForeignKey(
                        name: "FK_empresa_roles_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modulos",
                columns: table => new
                {
                    id_modulo = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    codigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    icono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    orden = table.Column<int>(type: "int", nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modulos", x => x.id_modulo);
                });

            migrationBuilder.CreateTable(
                name: "empresa_modulos",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    id_empresa = table.Column<int>(type: "int", nullable: false),
                    id_modulo = table.Column<int>(type: "int", nullable: false),
                    fecha_activacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    fecha_vencimiento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_empresa_modulos", x => x.id);
                    table.ForeignKey(
                        name: "FK_empresa_modulos_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id_empresa",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_empresa_modulos_modulos_id_modulo",
                        column: x => x.id_modulo,
                        principalTable: "modulos",
                        principalColumn: "id_modulo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "empresa_rol_permisos",
                columns: table => new
                {
                    id_rol = table.Column<int>(type: "int", nullable: false),
                    id_modulo = table.Column<int>(type: "int", nullable: false),
                    puede_ver = table.Column<bool>(type: "bit", nullable: false),
                    puede_crear = table.Column<bool>(type: "bit", nullable: false),
                    puede_editar = table.Column<bool>(type: "bit", nullable: false),
                    puede_eliminar = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_empresa_rol_permisos", x => new { x.id_rol, x.id_modulo });
                    table.ForeignKey(
                        name: "FK_empresa_rol_permisos_empresa_roles_id_rol",
                        column: x => x.id_rol,
                        principalTable: "empresa_roles",
                        principalColumn: "id_rol",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_empresa_rol_permisos_modulos_id_modulo",
                        column: x => x.id_modulo,
                        principalTable: "modulos",
                        principalColumn: "id_modulo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "EmpresaId", "EmpresaRolId" },
                values: new object[] { null, null });

            migrationBuilder.InsertData(
                table: "modulos",
                columns: new[] { "id_modulo", "activo", "codigo", "descripcion", "icono", "nombre", "orden" },
                values: new object[,]
                {
                    { 1, true, "flota", "Gestión de vehículos, operación, gastos y KPIs", "fa-truck", "Control de Flota", 1 },
                    { 2, true, "inventario", "Control de productos, stock y movimientos", "fa-boxes", "Inventario", 2 },
                    { 3, true, "ventas", "Registro y seguimiento de ventas realizadas", "fa-shopping-cart", "Ventas", 3 },
                    { 4, true, "reportes", "Generación de reportes y análisis de datos", "fa-chart-bar", "Reportes", 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmpresaId",
                table: "Users",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmpresaRolId",
                table: "Users",
                column: "EmpresaRolId");

            migrationBuilder.CreateIndex(
                name: "IX_empresa_modulos_id_empresa_id_modulo",
                table: "empresa_modulos",
                columns: new[] { "id_empresa", "id_modulo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_empresa_modulos_id_modulo",
                table: "empresa_modulos",
                column: "id_modulo");

            migrationBuilder.CreateIndex(
                name: "IX_empresa_rol_permisos_id_modulo",
                table: "empresa_rol_permisos",
                column: "id_modulo");

            migrationBuilder.CreateIndex(
                name: "IX_empresa_roles_id_empresa",
                table: "empresa_roles",
                column: "id_empresa");

            migrationBuilder.CreateIndex(
                name: "IX_modulos_codigo",
                table: "modulos",
                column: "codigo",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_empresa_roles_EmpresaRolId",
                table: "Users",
                column: "EmpresaRolId",
                principalTable: "empresa_roles",
                principalColumn: "id_rol",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_empresas_EmpresaId",
                table: "Users",
                column: "EmpresaId",
                principalTable: "empresas",
                principalColumn: "id_empresa",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_empresa_roles_EmpresaRolId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_empresas_EmpresaId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "empresa_modulos");

            migrationBuilder.DropTable(
                name: "empresa_rol_permisos");

            migrationBuilder.DropTable(
                name: "empresa_roles");

            migrationBuilder.DropTable(
                name: "modulos");

            migrationBuilder.DropIndex(
                name: "IX_Users_EmpresaId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_EmpresaRolId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmpresaRolId",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "user");
        }
    }
}
