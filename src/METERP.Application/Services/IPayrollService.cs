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

    /// <summary>Single-employee payslip summary for the period (null if employee not found).</summary>
    Task<PayrollEmployeeSummary?> GetEmployeeSummaryAsync(
        Guid employeeId,
        DateTime? monthUtc = null,
        decimal? deductionPercent = null,
        decimal? fixedDeductions = null,
        CancellationToken ct = default);

    /// <summary>
    /// Read-only Monday–Sunday hours grid for one employee.
    /// Null when the employee is missing or belongs to another tenant.
    /// Closed-job labour stays on the grid; it is history.
    /// </summary>
    Task<WeeklyTimesheet?> GetWeeklyTimesheetAsync(
        Guid employeeId,
        DateTime? weekContainingUtc = null,
        CancellationToken ct = default);

    /// <summary>CSV export of monthly payroll summaries (contractor payslip v1).</summary>
    Task<string> ExportMonthlyCsvAsync(
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

/// <summary>One employee's posted hours for a Monday–Sunday week. Totals are the sum of the cells.</summary>
public sealed record WeeklyTimesheet(
    Guid EmployeeId,
    string EmployeeNumber,
    string Name,
    DateTime WeekStartUtc,
    IReadOnlyList<WeeklyTimesheetDay> Days,
    IReadOnlyList<WeeklyTimesheetRow> Rows,
    decimal TotalHours);

/// <summary>One calendar day column. <see cref="Label"/> is Mon … Sun.</summary>
public sealed record WeeklyTimesheetDay(DateTime Date, string Label, decimal Hours);

/// <summary>
/// Hours for one job across the seven days, aligned with <see cref="WeeklyTimesheet.Days"/>.
/// <see cref="JobClosed"/> is true when the job is already closed; the hours still count.
/// </summary>
public sealed record WeeklyTimesheetRow(
    Guid JobId,
    string JobNumber,
    string JobTitle,
    bool JobClosed,
    IReadOnlyList<decimal> HoursByDay,
    decimal TotalHours);
