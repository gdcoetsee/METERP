using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSubscriptionTierAndQuotas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxAiCallsPerMonth",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxInvoicesPerMonth",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxJobsPerMonth",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxQuotesPerMonth",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PeriodAiCalls",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PeriodInvoicesIssued",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PeriodJobsCreated",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PeriodQuotesCreated",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Tier",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UsagePeriodStartUtc",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxAiCallsPerMonth",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MaxInvoicesPerMonth",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MaxJobsPerMonth",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MaxQuotesPerMonth",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PeriodAiCalls",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PeriodInvoicesIssued",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PeriodJobsCreated",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PeriodQuotesCreated",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "UsagePeriodStartUtc",
                table: "Tenants");
        }
    }
}
