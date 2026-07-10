using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobIsEmergency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEmergency",
                table: "Jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEmergency",
                table: "Jobs");
        }
    }
}
