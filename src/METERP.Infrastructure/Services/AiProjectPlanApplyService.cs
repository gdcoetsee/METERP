using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Creates linked quote + job shells from AI project plan text.
/// </summary>
public class AiProjectPlanApplyService : IAiProjectPlanApplyService
{
    private readonly IQuoteService _quoteService;
    private readonly IJobService _jobService;
    private readonly ITenantProvider? _tenantProvider;
    private readonly ITenantService? _tenantService;

    public AiProjectPlanApplyService(
        IQuoteService quoteService,
        IJobService jobService,
        ITenantProvider? tenantProvider = null,
        ITenantService? tenantService = null)
    {
        _quoteService = quoteService;
        _jobService = jobService;
        _tenantProvider = tenantProvider;
        _tenantService = tenantService;
    }

    public async Task<AiProjectPlanResult> CreateProjectPlanFromAiTextAsync(
        string aiText,
        Guid customerId,
        decimal taxRate = 0.15m,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aiText))
            throw new ArgumentException("AI text is required.", nameof(aiText));

        if (customerId == Guid.Empty)
            throw new ArgumentException("A customer is required.", nameof(customerId));

        await AiCopilotAccessGuard.EnsureAiApplyAllowedAsync(_tenantProvider, _tenantService, ct);

        var newQuote = new Quote
        {
            QuoteDate = DateTime.UtcNow,
            ValidUntil = DateTime.UtcNow.AddDays(30),
            TaxRate = taxRate,
            Notes = "AI Project Plan - Quote: " + aiText,
            CustomerId = customerId
        };

        var quoteId = await _quoteService.CreateAsync(newQuote, ct);
        var quote = (await _quoteService.GetByIdAsync(quoteId, ct))!;

        var newJob = new Job
        {
            Title = "AI Project Plan - Job",
            Description = "Linked to AI generated plan",
            Notes = "AI Generated Project Plan: " + aiText + "\n\nLinked Quote created.",
            Status = JobStatus.Scheduled,
            QuotedTotal = 0,
            CustomerId = customerId
        };

        var jobId = await _jobService.CreateAsync(newJob, ct);
        var job = (await _jobService.GetByIdAsync(jobId, ct))!;

        return new AiProjectPlanResult(quote, job);
    }
}