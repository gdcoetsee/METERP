using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiApiKeyEncrypted",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiBaseUrl",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiModel",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiProvider",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiUseTenantKey",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiApiKeyEncrypted",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AiBaseUrl",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AiModel",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AiProvider",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AiUseTenantKey",
                table: "Tenants");
        }
    }
}
