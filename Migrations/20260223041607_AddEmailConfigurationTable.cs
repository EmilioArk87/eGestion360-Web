using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eGestion360Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailConfigurationTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SmtpHost = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SmtpPort = table.Column<int>(type: "int", nullable: false),
                    UseSsl = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastTestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TestEmailsSent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfigurations_IsActive_IsDefault",
                table: "EmailConfigurations",
                columns: new[] { "IsActive", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfigurations_ProfileName",
                table: "EmailConfigurations",
                column: "ProfileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfigurations_Provider",
                table: "EmailConfigurations",
                column: "Provider");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailConfigurations");
        }
    }
}
