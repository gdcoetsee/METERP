using METERP.Application.Models;
using METERP.Domain;
using METERP.Web.Reports;
using Xunit;

namespace METERP.Web.Tests;

public class AiReportGeneratorTests
{
    [Fact]
    public void GenerateSingleResponseReport_ProducesNonEmptyPdf()
    {
        var bytes = AiReportGenerator.GenerateSingleResponseReport("Test query", "Test response");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);
        Assert.Equal(0x25, bytes[0]); // PDF magic %
    }

    [Fact]
    public void GenerateQuotePdf_WithCustomBranding_ProducesNonEmptyPdf()
    {
        var quote = new Quote
        {
            QuoteNumber = "Q-TEST-001",
            QuoteDate = new DateTime(2026, 7, 3),
            ValidUntil = new DateTime(2026, 8, 3),
            Subtotal = 1000m,
            Tax = 150m,
            Total = 1150m,
            Customer = new Customer { Name = "Acme Corp" }
        };

        var branding = new TenantBranding("Acme Electrical", "#ff6600", null);
        var bytes = AiReportGenerator.GenerateQuotePdf(quote, branding: branding);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 200);
    }

    [Fact]
    public void GenerateJobCloseoutPdf_WithTravelCosts_ProducesNonEmptyPdf()
    {
        var job = new Job
        {
            JobNumber = "J-TEST-001",
            Title = "Panel upgrade",
            Status = JobStatus.Completed,
            QuotedTotal = 5000m,
            ActualCost = 3200m,
            ActualCosts = new List<JobCost>
            {
                new() { CostType = "Travel", Amount = 450m, Description = "Site travel" }
            },
            Labors = new List<JobLabor>
            {
                new() { Hours = 8, HourlyRate = 200, Technician = "Tech A" }
            }
        };

        var bytes = AiReportGenerator.GenerateJobCloseoutPdf(job, aiNotes: "On budget.", branding: TenantBranding.Default);
        Assert.True(bytes.Length > 200);
    }
}