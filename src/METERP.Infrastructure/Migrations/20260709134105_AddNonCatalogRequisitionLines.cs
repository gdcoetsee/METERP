using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNonCatalogRequisitionLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockRequisitionLines_InventoryItems_InventoryItemId",
                table: "StockRequisitionLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "InventoryItemId",
                table: "StockRequisitionLines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "StockRequisitionLines",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedUnitCost",
                table: "StockRequisitionLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "StockRequisitionLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StockRequisitionLines_InventoryItems_InventoryItemId",
                table: "StockRequisitionLines",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockRequisitionLines_InventoryItems_InventoryItemId",
                table: "StockRequisitionLines");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "StockRequisitionLines");

            migrationBuilder.DropColumn(
                name: "EstimatedUnitCost",
                table: "StockRequisitionLines");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "StockRequisitionLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "InventoryItemId",
                table: "StockRequisitionLines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StockRequisitionLines_InventoryItems_InventoryItemId",
                table: "StockRequisitionLines",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
