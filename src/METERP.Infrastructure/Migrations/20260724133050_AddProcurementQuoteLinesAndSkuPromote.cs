using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcurementQuoteLinesAndSkuPromote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcurementSupplierQuoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcurementSupplierQuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockRequisitionLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcurementSupplierQuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierQuoteLines_ProcurementSupplierQuotes_Pro~",
                        column: x => x.ProcurementSupplierQuoteId,
                        principalTable: "ProcurementSupplierQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcurementSupplierQuoteLines_StockRequisitionLines_StockRe~",
                        column: x => x.StockRequisitionLineId,
                        principalTable: "StockRequisitionLines",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierQuoteLines_ProcurementSupplierQuoteId",
                table: "ProcurementSupplierQuoteLines",
                column: "ProcurementSupplierQuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcurementSupplierQuoteLines_StockRequisitionLineId",
                table: "ProcurementSupplierQuoteLines",
                column: "StockRequisitionLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcurementSupplierQuoteLines");
        }
    }
}
