using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class JobReportService : IJobReportService
{
    private readonly AppDbContext _dbContext;

    public JobReportService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<JobProfitabilitySummary> GetJobProfitabilitySummaryAsync(CancellationToken ct = default)
    {
        var jobs = await _dbContext.Set<Domain.Job>()
            .AsNoTracking()
            .Include(j => j.ActualCosts)
            .Include(j => j.Labors)
            .Where(j => j.QuotedTotal > 0)
            .ToListAsync(ct);

        var rows = jobs
            .Select(j => new JobProfitabilityRow(
                j.Id,
                j.JobNumber,
                j.Title,
                j.QuotedTotal,
                j.GetActualTotal(),
                j.GetVariance(),
                j.GetMarginPercent()))
            .Where(r => r.ActualTotal > 0)
            .ToList();

        if (rows.Count == 0)
        {
            return new JobProfitabilitySummary(0m, 0, null);
        }

        var averageMargin = Math.Round(rows.Average(r => r.MarginPercent), 1);
        var top = rows.OrderByDescending(r => r.MarginPercent).ThenBy(r => r.Title).First();

        return new JobProfitabilitySummary(averageMargin, rows.Count, top);
    }
}