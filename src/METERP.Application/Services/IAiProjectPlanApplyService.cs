using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Applies AI copilot text to a paired quote + job project plan skeleton.
/// </summary>
public interface IAiProjectPlanApplyService
{
    Task<AiProjectPlanResult> CreateProjectPlanFromAiTextAsync(
        string aiText,
        Guid customerId,
        decimal taxRate = 0.15m,
        CancellationToken ct = default);
}

public record AiProjectPlanResult(Quote Quote, Job Job);