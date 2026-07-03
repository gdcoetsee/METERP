using METERP.Application.Models;

namespace METERP.Application.Services;

public interface IExecutiveDashboardService
{
    Task<ExecutiveDashboardSummary> GetSummaryAsync(CancellationToken ct = default);
}