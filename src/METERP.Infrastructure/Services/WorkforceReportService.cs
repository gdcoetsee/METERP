using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class WorkforceReportService : IWorkforceReportService
{
    private readonly AppDbContext _dbContext;

    public WorkforceReportService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<TechnicianUtilizationSummary>> GetTechnicianUtilizationAsync(
        DateTime? monthUtc = null,
        decimal monthlyCapacityHours = 160m,
        CancellationToken ct = default)
    {
        if (monthlyCapacityHours <= 0)
            monthlyCapacityHours = 160m;

        var anchor = monthUtc ?? DateTime.UtcNow;
        var periodStart = new DateTime(anchor.Year, anchor.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);

        var employees = await _dbContext.Set<Domain.Employee>()
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        var laborEntries = await _dbContext.Set<Domain.JobLabor>()
            .AsNoTracking()
            .Where(l => !l.IsDeleted
                && l.EmployeeId != null
                && l.WorkDate >= periodStart
                && l.WorkDate < periodEnd)
            .ToListAsync(ct);

        var hoursByEmployee = laborEntries
            .GroupBy(l => l.EmployeeId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Hours));

        var summaries = new List<TechnicianUtilizationSummary>();
        foreach (var employee in employees)
        {
            hoursByEmployee.TryGetValue(employee.Id, out var hours);
            var utilization = monthlyCapacityHours > 0
                ? Math.Round(hours / monthlyCapacityHours * 100m, 1)
                : 0m;

            summaries.Add(new TechnicianUtilizationSummary(
                employee.Id,
                $"{employee.FirstName} {employee.LastName}".Trim(),
                hours,
                monthlyCapacityHours,
                utilization));
        }

        return summaries;
    }
}