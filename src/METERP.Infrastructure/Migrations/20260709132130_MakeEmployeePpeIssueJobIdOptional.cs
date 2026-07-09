using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeEmployeePpeIssueJobIdOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeePpeIssues_Jobs_JobId",
                table: "EmployeePpeIssues");

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "EmployeePpeIssues",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeePpeIssues_Jobs_JobId",
                table: "EmployeePpeIssues",
                column: "JobId",
                principalTable: "Jobs",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeePpeIssues_Jobs_JobId",
                table: "EmployeePpeIssues");

            migrationBuilder.AlterColumn<Guid>(
                name: "JobId",
                table: "EmployeePpeIssues",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeePpeIssues_Jobs_JobId",
                table: "EmployeePpeIssues",
                column: "JobId",
                principalTable: "Jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
