using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteGrossProfitAndLineUnitCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GrossProfitPercent",
                table: "Quotes",
                type: "numeric",
                nullable: false,
                defaultValue: 0.25m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "QuoteLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GrossProfitPercent",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "QuoteLines");
        }
    }
}
