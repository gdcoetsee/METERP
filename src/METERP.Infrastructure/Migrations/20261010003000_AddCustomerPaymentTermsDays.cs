using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using METERP.Infrastructure.Persistence;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <summary>
    /// Adds customer payment terms. Existing customers get 30 days, matching the previous invoice due-date rule.
    /// Invoice totals are not rewritten.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261010003000_AddCustomerPaymentTermsDays")]
    public partial class AddCustomerPaymentTermsDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentTermsDays",
                table: "Customers",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """UPDATE "Customers" SET "PaymentTermsDays" = 30 WHERE "PaymentTermsDays" IS NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentTermsDays",
                table: "Customers");
        }
    }
}
