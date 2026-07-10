namespace METERP.Application.Services;

/// <summary>
/// Payroll summaries derived from linked JobLabor entries (contractor crew costing — not full SARS).
/// </summary>
public interface IPayrollService
{
    Task<IReadOnlyList<PayrollEmployeeSummary>> GetMonthlySummariesAsync(
        DateTime? monthUtc = null,
        decimal? deductionPercent = null,
        decimal? fixedDeductions = null,
        CancellationToken ct = default);
}

public sealed record PayrollEmployeeSummary(
    Guid EmployeeId,
    string EmployeeNumber,
    string Name,
    string? JobTitle,
    decimal DefaultHourlyRate,
    decimal Hours,
    decimal GrossPay,
    decimal Deductions,
    decimal NetPay,
    int LaborEntryCount,
    bool IsActive,
    decimal MandatoryHoursPerMonth);
