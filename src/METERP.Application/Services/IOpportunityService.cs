using METERP.Domain;

namespace METERP.Application.Services;

public interface IOpportunityService
{
    Task<Opportunity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Opportunity>> GetAllAsync(
        string? search = null,
        OpportunityStage? stage = null,
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default);

    Task<Guid> CreateAsync(Opportunity opportunity, CancellationToken ct = default);

    Task UpdateAsync(Opportunity opportunity, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task AdvanceStageAsync(Guid id, CancellationToken ct = default);

    /// <summary>Builds AI Copilot scope text for quote conversion handoff.</summary>
    string BuildAiScopeText(Opportunity opportunity);

    Task MarkConvertedToQuoteAsync(Guid opportunityId, Guid quoteId, CancellationToken ct = default);
}