namespace METERP.Application.Services;

/// <summary>
/// Workforce analytics derived from linked JobLabor (utilization, capacity).
/// </summary>
public interface IWorkforceReportService
{
    Task<IReadOnlyList<TechnicianUtilizationSummary>> GetTechnicianUtilizationAsync(
        DateTime? monthUtc = null,
        decimal monthlyCapacityHours = 160m,
        CancellationToken ct = default);
}

public sealed record TechnicianUtilizationSummary(
    Guid EmployeeId,
    string Name,
    decimal HoursLogged,
    decimal CapacityHours,
    decimal UtilizationPercent);