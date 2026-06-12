using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobLaborEmployeeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                table: "JobLabors",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobLabors_EmployeeId",
                table: "JobLabors",
                column: "EmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_JobLabors_Employees_EmployeeId",
                table: "JobLabors",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobLabors_Employees_EmployeeId",
                table: "JobLabors");

            migrationBuilder.DropIndex(
                name: "IX_JobLabors_EmployeeId",
                table: "JobLabors");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "JobLabors");
        }
    }
}
