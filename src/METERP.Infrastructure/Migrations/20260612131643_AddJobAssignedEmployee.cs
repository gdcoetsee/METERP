using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAssignedEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedEmployeeId",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_AssignedEmployeeId",
                table: "Jobs",
                column: "AssignedEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Employees_AssignedEmployeeId",
                table: "Jobs",
                column: "AssignedEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Employees_AssignedEmployeeId",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_AssignedEmployeeId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AssignedEmployeeId",
                table: "Jobs");
        }
    }
}
