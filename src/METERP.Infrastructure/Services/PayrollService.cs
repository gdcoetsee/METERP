using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class PayrollService : IPayrollService
{
    private readonly AppDbContext _dbContext;

    public PayrollService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<PayrollEmployeeSummary>> GetMonthlySummariesAsync(
        DateTime? monthUtc = null,
        CancellationToken ct = default)
    {
        var anchor = monthUtc ?? DateTime.UtcNow;
        var periodStart = new DateTime(anchor.Year, anchor.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);

        var employees = await _dbContext.Set<Domain.Employee>()
            .AsNoTracking()
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

        var laborByEmployee = laborEntries
            .GroupBy(l => l.EmployeeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var summaries = new List<PayrollEmployeeSummary>();
        foreach (var employee in employees)
        {
            laborByEmployee.TryGetValue(employee.Id, out var entries);
            entries ??= new List<Domain.JobLabor>();

            var hours = entries.Sum(l => l.Hours);
            var gross = entries.Sum(l => l.TotalCost);

            summaries.Add(new PayrollEmployeeSummary(
                employee.Id,
                $"{employee.FirstName} {employee.LastName}".Trim(),
                employee.JobTitle,
                employee.DefaultHourlyRate,
                hours,
                gross,
                entries.Count,
                employee.IsActive));
        }

        return summaries;
    }
}