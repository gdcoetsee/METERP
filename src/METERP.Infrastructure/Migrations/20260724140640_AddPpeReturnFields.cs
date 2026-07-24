using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPpeReturnFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QuantityReturned",
                table: "EmployeePpeIssues",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAt",
                table: "EmployeePpeIssues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnedByUserId",
                table: "EmployeePpeIssues",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuantityReturned",
                table: "EmployeePpeIssues");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "EmployeePpeIssues");

            migrationBuilder.DropColumn(
                name: "ReturnedByUserId",
                table: "EmployeePpeIssues");
        }
    }
}
