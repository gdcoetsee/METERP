using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantCommercialAndFeatureFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LineTotal",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "LineTotal",
                table: "QuoteLines");

            migrationBuilder.DropColumn(
                name: "LineTotal",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "LineTotal",
                table: "InvoiceLines");

            migrationBuilder.AddColumn<string>(
                name: "EnabledFeatures",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityUtc",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalAiCalls",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalInvoicesIssued",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalJobsCreated",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalQuotesCreated",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalRevenueBilled",
                table: "Tenants",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnabledFeatures",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastActivityUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TotalAiCalls",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TotalInvoicesIssued",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TotalJobsCreated",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TotalQuotesCreated",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TotalRevenueBilled",
                table: "Tenants");

            migrationBuilder.AddColumn<decimal>(
                name: "LineTotal",
                table: "SalesOrderLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LineTotal",
                table: "QuoteLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LineTotal",
                table: "PurchaseOrderLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LineTotal",
                table: "InvoiceLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
