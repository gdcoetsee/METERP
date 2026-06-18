using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteLineGrossProfitPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GrossProfitPercent",
                table: "QuoteLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0.25m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GrossProfitPercent",
                table: "QuoteLines");
        }
    }
}
