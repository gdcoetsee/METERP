using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using METERP.Infrastructure.Persistence;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260925120000_AddOpportunityDealFields")]
    public partial class AddOpportunityDealFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Opportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Opportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Opportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DealType",
                table: "Opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAt",
                table: "Opportunities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LossReason",
                table: "Opportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextFollowUp",
                table: "Opportunities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerEmployeeId",
                table: "Opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProbabilityPercent",
                table: "Opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_OwnerEmployeeId",
                table: "Opportunities",
                column: "OwnerEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Opportunities_Employees_OwnerEmployeeId",
                table: "Opportunities",
                column: "OwnerEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Opportunities_Employees_OwnerEmployeeId",
                table: "Opportunities");

            migrationBuilder.DropIndex(
                name: "IX_Opportunities_OwnerEmployeeId",
                table: "Opportunities");

            migrationBuilder.DropColumn(name: "ContactEmail", table: "Opportunities");
            migrationBuilder.DropColumn(name: "ContactName", table: "Opportunities");
            migrationBuilder.DropColumn(name: "ContactPhone", table: "Opportunities");
            migrationBuilder.DropColumn(name: "DealType", table: "Opportunities");
            migrationBuilder.DropColumn(name: "LastActivityAt", table: "Opportunities");
            migrationBuilder.DropColumn(name: "LossReason", table: "Opportunities");
            migrationBuilder.DropColumn(name: "NextFollowUp", table: "Opportunities");
            migrationBuilder.DropColumn(name: "OwnerEmployeeId", table: "Opportunities");
            migrationBuilder.DropColumn(name: "Priority", table: "Opportunities");
            migrationBuilder.DropColumn(name: "ProbabilityPercent", table: "Opportunities");
            migrationBuilder.DropColumn(name: "Source", table: "Opportunities");
        }
    }
}
