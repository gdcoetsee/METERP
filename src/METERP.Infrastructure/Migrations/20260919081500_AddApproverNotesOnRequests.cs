using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using METERP.Infrastructure.Persistence;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260919081500_AddApproverNotesOnRequests")]
    public partial class AddApproverNotesOnRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApproverNote",
                table: "StockRequisitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApproverNote",
                table: "LeaveRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApproverNote",
                table: "FieldReports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ApproverNote", table: "StockRequisitions");
            migrationBuilder.DropColumn(name: "ApproverNote", table: "LeaveRequests");
            migrationBuilder.DropColumn(name: "ApproverNote", table: "FieldReports");
        }
    }
}
