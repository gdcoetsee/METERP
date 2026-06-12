namespace METERP.Application.Services;

/// <summary>
/// Payroll summaries derived from linked JobLabor entries (contractor crew costing).
/// </summary>
public interface IPayrollService
{
    Task<IReadOnlyList<PayrollEmployeeSummary>> GetMonthlySummariesAsync(DateTime? monthUtc = null, CancellationToken ct = default);
}

public sealed record PayrollEmployeeSummary(
    Guid EmployeeId,
    string Name,
    string? JobTitle,
    decimal DefaultHourlyRate,
    decimal Hours,
    decimal GrossPay,
    int LaborEntryCount,
    bool IsActive);