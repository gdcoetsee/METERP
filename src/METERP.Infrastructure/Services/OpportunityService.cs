using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class OpportunityService : IOpportunityService
{
    private static readonly OpportunityStage[] StageOrder =
    {
        OpportunityStage.Lead,
        OpportunityStage.Qualified,
        OpportunityStage.Proposal,
        OpportunityStage.Negotiation,
        OpportunityStage.ClosedWon,
        OpportunityStage.ClosedLost
    };

    private readonly AppDbContext _dbContext;
    private readonly IAuditService? _auditService;
    private readonly ITenantCacheService? _cache;

    public OpportunityService(AppDbContext dbContext, IAuditService? auditService = null, ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _cache = cache;
    }

    public async Task<Opportunity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Opportunity>()
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task<IReadOnlyList<Opportunity>> GetAllAsync(
        string? search = null,
        OpportunityStage? stage = null,
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Opportunities,
                $"p{page}:s{pageSize}:st{(stage.HasValue ? (int)stage.Value : -1)}",
                () => LoadOpportunitiesAsync(search, stage, page, pageSize, ct),
                ct: ct);
        }

        return await LoadOpportunitiesAsync(search, stage, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<Opportunity>> LoadOpportunitiesAsync(
        string? search,
        OpportunityStage? stage,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = _dbContext.Set<Opportunity>()
            .AsNoTracking()
            .Include(o => o.Customer)
            .AsQueryable();

        if (stage.HasValue)
            query = query.Where(o => o.Stage == stage.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(o =>
                o.Title.ToLower().Contains(term) ||
                (o.CustomerName != null && o.CustomerName.ToLower().Contains(term)) ||
                (o.Customer != null && o.Customer.Name.ToLower().Contains(term)) ||
                (o.Notes != null && o.Notes.ToLower().Contains(term)));
        }

        return await query
            .OrderByDescending(o => o.ExpectedClose)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Opportunity opportunity, CancellationToken ct = default)
    {
        if (opportunity.CustomerId.HasValue && opportunity.CustomerId != Guid.Empty)
        {
            var customer = await _dbContext.Set<Customer>()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == opportunity.CustomerId, ct);
            if (customer != null)
                opportunity.CustomerName ??= customer.Name;
        }

        _dbContext.Set<Opportunity>().Add(opportunity);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "CREATE",
                "Opportunity",
                opportunity.Title,
                $"Stage {opportunity.Stage}, value R {opportunity.Value:N0}",
                ct);
        }

        return opportunity.Id;
    }

    public async Task UpdateAsync(Opportunity opportunity, CancellationToken ct = default)
    {
        _dbContext.Set<Opportunity>().Update(opportunity);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "UPDATE",
                "Opportunity",
                opportunity.Title,
                $"Stage {opportunity.Stage}",
                ct);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var opp = await _dbContext.Set<Opportunity>().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opp == null) return;

        opp.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync("DELETE", "Opportunity", opp.Title, "Soft deleted", ct);
        }
    }

    public async Task AdvanceStageAsync(Guid id, CancellationToken ct = default)
    {
        var opp = await _dbContext.Set<Opportunity>().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (opp == null) return;

        var idx = Array.IndexOf(StageOrder, opp.Stage);
        if (idx >= 0 && idx < StageOrder.Length - 1 && opp.Stage != OpportunityStage.ClosedLost)
            opp.Stage = StageOrder[idx + 1];

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "UPDATE",
                "Opportunity",
                opp.Title,
                $"Advanced to {opp.Stage}",
                ct);
        }
    }

    public string BuildAiScopeText(Opportunity opportunity)
    {
        var customer = opportunity.Customer?.Name ?? opportunity.CustomerName ?? "Customer TBD";
        return $"{opportunity.Title} for {customer} - expected close {opportunity.ExpectedClose:yyyy-MM-dd}, value R {opportunity.Value:N0}. Include explicit travel costs for site work.";
    }

    public async Task MarkConvertedToQuoteAsync(Guid opportunityId, Guid quoteId, CancellationToken ct = default)
    {
        var opp = await _dbContext.Set<Opportunity>().FirstOrDefaultAsync(o => o.Id == opportunityId, ct);
        if (opp == null) return;

        opp.QuoteId = quoteId;
        if (opp.Stage is OpportunityStage.Lead or OpportunityStage.Qualified or OpportunityStage.Proposal or OpportunityStage.Negotiation)
            opp.Stage = OpportunityStage.Proposal;

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "CONVERT",
                "Opportunity",
                opp.Title,
                $"Linked to quote {quoteId}",
                ct);
        }
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory(TenantCacheCategories.Opportunities);
}