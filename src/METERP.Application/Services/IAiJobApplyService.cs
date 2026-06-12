using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Applies AI copilot text to draft jobs with explicit travel costs for contractor workflows.
/// </summary>
public interface IAiJobApplyService
{
    Task<Job> CreateJobFromAiTextAsync(string aiText, Guid? customerId = null, CancellationToken ct = default);
}