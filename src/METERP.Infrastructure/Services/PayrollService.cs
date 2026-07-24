using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class PayrollService : IPayrollService
{
    /// <summary>Default simple deduction: 1% of gross + R0 fixed (configurable per call).</summary>
    public const decimal DefaultDeductionPercent = 1m;

    public const decimal DefaultFixedDeductions = 0m;

    private readonly AppDbContext _dbContext;

    public PayrollService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<PayrollEmployeeSummary>> GetMonthlySummariesAsync(
        DateTime? monthUtc = null,
        decimal? deductionPercent = null,
        decimal? fixedDeductions = null,
        CancellationToken ct = default)
    {
        var (pct, fixedAmt, periodStart, periodEnd) = NormalizePeriod(monthUtc, deductionPercent, fixedDeductions);

        var employees = await _dbContext.Set<Domain.Employee>()
            .AsNoTracking()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        var laborByEmployee = await LoadLaborByEmployeeAsync(periodStart, periodEnd, ct);

        var summaries = new List<PayrollEmployeeSummary>();
        foreach (var employee in employees)
        {
            laborByEmployee.TryGetValue(employee.Id, out var entries);
            entries ??= new List<Domain.JobLabor>();
            summaries.Add(BuildSummary(employee, entries, pct, fixedAmt));
        }

        return summaries;
    }

    public async Task<PayrollEmployeeSummary?> GetEmployeeSummaryAsync(
        Guid employeeId,
        DateTime? monthUtc = null,
        decimal? deductionPercent = null,
        decimal? fixedDeductions = null,
        CancellationToken ct = default)
    {
        var employee = await _dbContext.Set<Domain.Employee>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee == null) return null;

        var (pct, fixedAmt, periodStart, periodEnd) = NormalizePeriod(monthUtc, deductionPercent, fixedDeductions);
        var entries = await _dbContext.Set<Domain.JobLabor>()
            .AsNoTracking()
            .Where(l => !l.IsDeleted
                && l.EmployeeId == employeeId
                && l.WorkDate >= periodStart
                && l.WorkDate < periodEnd)
            .ToListAsync(ct);

        return BuildSummary(employee, entries, pct, fixedAmt);
    }

    public async Task<string> ExportMonthlyCsvAsync(
        DateTime? monthUtc = null,
        decimal? deductionPercent = null,
        decimal? fixedDeductions = null,
        CancellationToken ct = default)
    {
        var summaries = await GetMonthlySummariesAsync(monthUtc, deductionPercent, fixedDeductions, ct);
        var anchor = monthUtc ?? DateTime.UtcNow;
        var periodLabel = new DateTime(anchor.Year, anchor.Month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine("Period,EmployeeNumber,Name,JobTitle,Active,Hours,MandatoryHours,GrossPay,Deductions,NetPay,LaborEntries");
        foreach (var s in summaries)
        {
            sb.Append(Csv(periodLabel)).Append(',')
                .Append(Csv(s.EmployeeNumber)).Append(',')
                .Append(Csv(s.Name)).Append(',')
                .Append(Csv(s.JobTitle)).Append(',')
                .Append(s.IsActive ? "Y" : "N").Append(',')
                .Append(s.Hours.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.MandatoryHoursPerMonth.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.GrossPay.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Deductions.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.NetPay.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.LaborEntryCount)
                .AppendLine();
        }

        return sb.ToString();
    }

    private async Task<Dictionary<Guid, List<Domain.JobLabor>>> LoadLaborByEmployeeAsync(
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken ct)
    {
        var laborEntries = await _dbContext.Set<Domain.JobLabor>()
            .AsNoTracking()
            .Where(l => !l.IsDeleted
                && l.EmployeeId != null
                && l.WorkDate >= periodStart
                && l.WorkDate < periodEnd)
            .ToListAsync(ct);

        return laborEntries
            .GroupBy(l => l.EmployeeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static (decimal Pct, decimal FixedAmt, DateTime PeriodStart, DateTime PeriodEnd) NormalizePeriod(
        DateTime? monthUtc,
        decimal? deductionPercent,
        decimal? fixedDeductions)
    {
        var pct = deductionPercent ?? DefaultDeductionPercent;
        var fixedAmt = fixedDeductions ?? DefaultFixedDeductions;
        if (pct < 0) pct = 0;
        if (fixedAmt < 0) fixedAmt = 0;
        if (pct > 100) pct = 100;

        var anchor = monthUtc ?? DateTime.UtcNow;
        var periodStart = new DateTime(anchor.Year, anchor.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);
        return (pct, fixedAmt, periodStart, periodEnd);
    }

    private static PayrollEmployeeSummary BuildSummary(
        Domain.Employee employee,
        IReadOnlyList<Domain.JobLabor> entries,
        decimal pct,
        decimal fixedAmt)
    {
        var hours = entries.Sum(l => l.Hours);
        var gross = entries.Sum(l => l.TotalCost);
        var deductions = Math.Round(gross * (pct / 100m), 2) + fixedAmt;
        if (deductions > gross)
            deductions = gross;
        var net = Math.Max(0m, gross - deductions);

        return new PayrollEmployeeSummary(
            employee.Id,
            employee.EmployeeNumber,
            $"{employee.FirstName} {employee.LastName}".Trim(),
            employee.JobTitle,
            employee.DefaultHourlyRate,
            hours,
            gross,
            deductions,
            net,
            entries.Count,
            employee.IsActive,
            employee.MandatoryHoursPerMonth > 0 ? employee.MandatoryHoursPerMonth : 160m);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
