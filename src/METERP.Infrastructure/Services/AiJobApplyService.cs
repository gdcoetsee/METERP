using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Creates draft jobs from AI copilot text with explicit travel costing.
/// </summary>
public class AiJobApplyService : IAiJobApplyService
{
    private const string FallbackTravelDescription = "Travel & site transport (AI estimate)";
    private const decimal FallbackTravelAmount = 650m;

    private readonly IJobService _jobService;
    private readonly ITenantProvider? _tenantProvider;
    private readonly ITenantService? _tenantService;

    public AiJobApplyService(
        IJobService jobService,
        ITenantProvider? tenantProvider = null,
        ITenantService? tenantService = null)
    {
        _jobService = jobService;
        _tenantProvider = tenantProvider;
        _tenantService = tenantService;
    }

    public async Task<Job> CreateJobFromAiTextAsync(string aiText, Guid? customerId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aiText))
            throw new ArgumentException("AI text is required.", nameof(aiText));

        await AiCopilotAccessGuard.EnsureAiApplyAllowedAsync(_tenantProvider, _tenantService, ct);

        var newJob = new Job
        {
            Title = "AI Generated Job",
            Description = "Created from AI Copilot response",
            Notes = "AI Generated: " + aiText,
            Status = JobStatus.Scheduled,
            QuotedTotal = 0,
            CustomerId = customerId ?? Guid.Empty
        };

        var createdId = await _jobService.CreateAsync(newJob, ct);

        await _jobService.AddCostAsync(new JobCost
        {
            JobId = createdId,
            Description = FallbackTravelDescription,
            Amount = FallbackTravelAmount,
            CostType = "Travel",
            CostDate = DateTime.UtcNow
        }, ct);

        return (await _jobService.GetByIdAsync(createdId, ct))!;
    }
}