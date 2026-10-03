using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using METERP.Infrastructure.Persistence;

#nullable disable

namespace METERP.Infrastructure.Migrations
{
    /// <summary>
    /// One live job number per tenant. Soft-deleted rows are excluded so a retired TRFid can be reused.
    /// Hand-authored so this migration does not pull unrelated model drift into the live MET database.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260930200000_AddJobNumberTenantUniqueIndex")]
    public partial class AddJobNumberTenantUniqueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Jobs_TenantId_JobNumber",
                table: "Jobs",
                columns: new[] { "TenantId", "JobNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_TenantId_JobNumber",
                table: "Jobs");
        }
    }
}
