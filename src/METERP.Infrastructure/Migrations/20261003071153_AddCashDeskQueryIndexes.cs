using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashDeskQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Opportunities_Employees_OwnerEmployeeId",
                table: "Opportunities");

            migrationBuilder.AlterColumn<string>(
                name: "ApproverNote",
                table: "StockRequisitions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ExecutiveDecisionNote",
                table: "Quotes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApproverNote",
                table: "LeaveRequests",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApproverNote",
                table: "FieldReports",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantNotifications_TenantId_IsDeleted_IsRead",
                table: "TenantNotifications",
                columns: new[] { "TenantId", "IsDeleted", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_TenantId_IsDeleted_ApprovalStatus",
                table: "Quotes",
                columns: new[] { "TenantId", "IsDeleted", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_TenantId_IsDeleted_CreatedDate",
                table: "Quotes",
                columns: new[] { "TenantId", "IsDeleted", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_TenantId_IsDeleted_Status",
                table: "Quotes",
                columns: new[] { "TenantId", "IsDeleted", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_TenantId_IsDeleted_CreatedDate",
                table: "Jobs",
                columns: new[] { "TenantId", "IsDeleted", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_TenantId_IsDeleted_SignOffStatus",
                table: "Jobs",
                columns: new[] { "TenantId", "IsDeleted", "SignOffStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_TenantId_IsDeleted_Status",
                table: "Jobs",
                columns: new[] { "TenantId", "IsDeleted", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_IsDeleted_CreatedDate",
                table: "Invoices",
                columns: new[] { "TenantId", "IsDeleted", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_IsDeleted_Status_DueDate",
                table: "Invoices",
                columns: new[] { "TenantId", "IsDeleted", "Status", "DueDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Opportunities_Employees_OwnerEmployeeId",
                table: "Opportunities",
                column: "OwnerEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Opportunities_Employees_OwnerEmployeeId",
                table: "Opportunities");

            migrationBuilder.DropIndex(
                name: "IX_TenantNotifications_TenantId_IsDeleted_IsRead",
                table: "TenantNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Quotes_TenantId_IsDeleted_ApprovalStatus",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_Quotes_TenantId_IsDeleted_CreatedDate",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_Quotes_TenantId_IsDeleted_Status",
                table: "Quotes");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_TenantId_IsDeleted_CreatedDate",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_TenantId_IsDeleted_SignOffStatus",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_TenantId_IsDeleted_Status",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_TenantId_IsDeleted_CreatedDate",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_TenantId_IsDeleted_Status_DueDate",
                table: "Invoices");

            migrationBuilder.AlterColumn<string>(
                name: "ApproverNote",
                table: "StockRequisitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ExecutiveDecisionNote",
                table: "Quotes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApproverNote",
                table: "LeaveRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApproverNote",
                table: "FieldReports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Opportunities_Employees_OwnerEmployeeId",
                table: "Opportunities",
                column: "OwnerEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
