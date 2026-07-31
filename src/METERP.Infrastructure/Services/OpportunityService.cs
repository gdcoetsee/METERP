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
        if (string.IsNullOrWhiteSpace(opportunity.Title))
            throw new InvalidOperationException("Opportunity title is required.");

        if (opportunity.Value < 0)
            throw new InvalidOperationException("Opportunity value cannot be negative.");
        if (opportunity.Value > 100_000_000m)
            throw new InvalidOperationException("Opportunity value cannot exceed 100,000,000.");

        opportunity.Title = opportunity.Title.Trim();
        if (opportunity.ExpectedClose == default)
            opportunity.ExpectedClose = DateTime.UtcNow.Date.AddDays(30);
        else
            opportunity.ExpectedClose = opportunity.ExpectedClose.Date;

        if (opportunity.ExpectedClose > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Expected close date cannot be more than 2 years in the future.");
        if (opportunity.ExpectedClose < DateTime.UtcNow.Date.AddYears(-1))
            throw new InvalidOperationException("Expected close date cannot be more than 1 year in the past.");

        if (opportunity.CustomerId.HasValue && opportunity.CustomerId != Guid.Empty)
        {
            var customer = await _dbContext.Set<Customer>()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == opportunity.CustomerId, ct);
            if (customer == null)
                throw new InvalidOperationException("Customer not found.");
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
        if (string.IsNullOrWhiteSpace(opportunity.Title))
            throw new InvalidOperationException("Opportunity title is required.");
        if (opportunity.Value < 0)
            throw new InvalidOperationException("Opportunity value cannot be negative.");
        if (opportunity.Value > 100_000_000m)
            throw new InvalidOperationException("Opportunity value cannot exceed 100,000,000.");

        opportunity.Title = opportunity.Title.Trim();
        if (opportunity.ExpectedClose != default)
        {
            opportunity.ExpectedClose = opportunity.ExpectedClose.Date;
            if (opportunity.ExpectedClose > DateTime.UtcNow.Date.AddYears(2))
                throw new InvalidOperationException("Expected close date cannot be more than 2 years in the future.");
            if (opportunity.ExpectedClose < DateTime.UtcNow.Date.AddYears(-1))
                throw new InvalidOperationException("Expected close date cannot be more than 1 year in the past.");
        }

        var existing = await _dbContext.Set<Opportunity>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == opportunity.Id, ct);
        if (existing == null)
            throw new InvalidOperationException("Opportunity not found.");

        if (existing.Stage is OpportunityStage.ClosedWon or OpportunityStage.ClosedLost
            && opportunity.Stage != existing.Stage)
            throw new InvalidOperationException(
                $"Closed opportunities cannot change stage from {existing.Stage}.");

        if (opportunity.Stage == OpportunityStage.ClosedWon
            && existing.Stage != OpportunityStage.ClosedWon)
        {
            var hasCustomer = (opportunity.CustomerId is { } cid && cid != Guid.Empty)
                || !string.IsNullOrWhiteSpace(opportunity.CustomerName);
            if (!hasCustomer)
                throw new InvalidOperationException(
                    "Customer is required to mark an opportunity Closed Won.");
            if (opportunity.Value <= 0)
                throw new InvalidOperationException(
                    "Opportunity value must be greater than zero to mark Closed Won.");
        }

        if (opportunity.CustomerId.HasValue && opportunity.CustomerId != Guid.Empty
            && opportunity.CustomerId != existing.CustomerId)
        {
            var customer = await _dbContext.Set<Customer>()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == opportunity.CustomerId, ct);
            if (customer == null)
                throw new InvalidOperationException("Customer not found.");
            opportunity.CustomerName ??= customer.Name;
        }

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

        if (opp.QuoteId.HasValue)
            throw new InvalidOperationException(
                "Cannot delete an opportunity linked to a quote. Unlink or keep for CRM history.");

        if (opp.Stage is OpportunityStage.ClosedWon)
            throw new InvalidOperationException("Cannot delete a Closed Won opportunity.");

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
        if (opp == null)
            throw new InvalidOperationException("Opportunity not found.");

        if (opp.Stage is OpportunityStage.ClosedWon or OpportunityStage.ClosedLost)
            throw new InvalidOperationException($"Opportunity is already {opp.Stage} and cannot be advanced.");

        var idx = Array.IndexOf(StageOrder, opp.Stage);
        if (idx < 0)
            throw new InvalidOperationException("Unknown opportunity stage.");

        var next = StageOrder[idx + 1];
        // Advance is for pipeline movement only — not auto Closed Won/Lost.
        if (next is OpportunityStage.ClosedWon or OpportunityStage.ClosedLost)
            throw new InvalidOperationException(
                "Opportunity is already at Negotiation. Mark Closed Won via edit or convert to quote.");

        opp.Stage = next;

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
        if (quoteId == Guid.Empty)
            throw new InvalidOperationException("Quote is required to convert an opportunity.");

        var opp = await _dbContext.Set<Opportunity>().FirstOrDefaultAsync(o => o.Id == opportunityId, ct);
        if (opp == null)
            throw new InvalidOperationException("Opportunity not found.");

        if (opp.Stage == OpportunityStage.ClosedLost)
            throw new InvalidOperationException("Cannot convert a Closed Lost opportunity to a quote.");

        if (opp.QuoteId.HasValue && opp.QuoteId.Value != quoteId)
            throw new InvalidOperationException(
                "Opportunity is already linked to a different quote.");

        // Idempotent when already linked to the same quote.
        if (opp.QuoteId == quoteId)
            return;

        var quote = await _dbContext.Set<Quote>().AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        if (opp.CustomerId is { } oppCustomerId && oppCustomerId != Guid.Empty
            && quote.CustomerId != oppCustomerId)
            throw new InvalidOperationException(
                "Quote customer must match the opportunity customer.");

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