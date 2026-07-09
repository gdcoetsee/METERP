using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobExecutiveCloseFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloseNotes",
                table: "Jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastReopenReason",
                table: "Jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReopenedAt",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastReopenedByUserId",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            // Retire terminal Invoiced semantics: jobs with legacy Invoiced status remain open for costs (Completed).
            migrationBuilder.Sql(
                """UPDATE "Jobs" SET "Status" = 3 WHERE "Status" = 4 AND NOT "IsDeleted";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseNotes",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "LastReopenReason",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "LastReopenedAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "LastReopenedByUserId",
                table: "Jobs");
        }
    }
}
