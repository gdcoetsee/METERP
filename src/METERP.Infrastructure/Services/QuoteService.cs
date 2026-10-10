using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Models;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class QuoteService : IQuoteService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantService? _tenantService;
    private readonly ITenantProvider? _tenantProvider;
    private readonly IQuotaService? _quotaService;
    private readonly ITenantCacheService? _cache;
    private readonly IAuditService? _auditService;
    private readonly IDocumentSequenceService? _documentSequence;
    private readonly ITenantNotificationService? _notifications;
    private readonly IEmailSender? _email;

    public QuoteService(
        AppDbContext dbContext,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        ITenantCacheService? cache = null,
        IAuditService? auditService = null,
        IDocumentSequenceService? documentSequence = null,
        ITenantNotificationService? notifications = null,
        IEmailSender? email = null)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _quotaService = quotaService;
        _cache = cache;
        _auditService = auditService;
        _documentSequence = documentSequence;
        _notifications = notifications;
        _email = email;
    }

    public async Task<Quote?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .Include(q => q.Customer)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
    }

    public async Task<IReadOnlyList<Quote>> GetAllAsync(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default,
        QuoteBoardFilter filter = QuoteBoardFilter.All,
        DateTime? from = null,
        DateTime? to = null,
        QuoteStatus? status = null)
    {
        // A dated or status-filtered book must not reuse the unfiltered list cache.
        if (_cache != null && string.IsNullOrWhiteSpace(search) && !HasRegisterWindow(from, to, status))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Quotes,
                $"p{page}:s{pageSize}:f{(int)filter}",
                () => LoadQuotesAsync(search, page, pageSize, filter, from, to, status, ct),
                ct: ct);
        }

        return await LoadQuotesAsync(search, page, pageSize, filter, from, to, status, ct);
    }

    public async Task<int> CountAsync(
        string? search = null,
        QuoteBoardFilter filter = QuoteBoardFilter.All,
        CancellationToken ct = default,
        DateTime? from = null,
        DateTime? to = null,
        QuoteStatus? status = null)
    {
        return await QuotesForList(search, filter, from, to, status).CountAsync(ct);
    }

    public async Task<IReadOnlyList<QuoteRegisterRow>> GetRegisterAsync(
        DateTime? from = null,
        DateTime? to = null,
        QuoteStatus? status = null,
        CancellationToken ct = default)
    {
        var quotes = await ApplyRegisterWindow(_dbContext.Set<Quote>().AsNoTracking(), from, to, status)
            .Include(q => q.Customer)
            .OrderByDescending(q => q.QuoteDate)
            .ThenByDescending(q => q.QuoteNumber)
            .ToListAsync(ct);

        var withJobs = await GetQuoteIdsWithJobsAsync(quotes.Select(q => q.Id).ToList(), ct);
        return quotes.Select(q => new QuoteRegisterRow(
            q.Id,
            q.QuoteNumber,
            q.QuoteDate,
            q.Customer?.Name ?? "",
            q.Subtotal,
            q.Tax,
            q.Total,
            q.Status,
            withJobs.Contains(q.Id))).ToList();
    }

    public async Task<IReadOnlySet<Guid>> GetQuoteIdsWithJobsAsync(
        IReadOnlyCollection<Guid> quoteIds,
        CancellationToken ct = default)
    {
        if (quoteIds.Count == 0)
            return new HashSet<Guid>();

        var ids = quoteIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<Guid>();

        var found = await _dbContext.Set<Job>().AsNoTracking()
            .Where(j => j.QuoteId != null && ids.Contains(j.QuoteId.Value))
            .Select(j => j.QuoteId!.Value)
            .Distinct()
            .ToListAsync(ct);

        return found.ToHashSet();
    }

    private IQueryable<Quote> FilteredQuotes(string? search)
    {
        var query = _dbContext.Set<Quote>().AsNoTracking().AsQueryable();
        if (string.IsNullOrWhiteSpace(search))
            return query;

        var term = search.Trim().ToLower();
        return query.Where(q =>
            q.QuoteNumber.ToLower().Contains(term) ||
            (q.Notes != null && q.Notes.ToLower().Contains(term)) ||
            (q.Customer != null && q.Customer.Name.ToLower().Contains(term)));
    }

    private static IQueryable<Quote> ApplyQuoteListFilter(IQueryable<Quote> query, QuoteBoardFilter filter) =>
        filter switch
        {
            QuoteBoardFilter.Live => query.Where(q => q.Status != QuoteStatus.Expired),
            QuoteBoardFilter.Draft => query.Where(q => q.Status == QuoteStatus.Draft),
            QuoteBoardFilter.Accepted => query.Where(q => q.Status == QuoteStatus.Accepted),
            QuoteBoardFilter.Expired => query.Where(q => q.Status == QuoteStatus.Expired),
            _ => query
        };

    private static bool HasRegisterWindow(DateTime? from, DateTime? to, QuoteStatus? status) =>
        from.HasValue || to.HasValue || status.HasValue;

    private IQueryable<Quote> QuotesForList(
        string? search,
        QuoteBoardFilter filter,
        DateTime? from,
        DateTime? to,
        QuoteStatus? status) =>
        ApplyRegisterWindow(ApplyQuoteListFilter(FilteredQuotes(search), filter), from, to, status);

    /// <summary>
    /// Inclusive calendar days. A quote at 23:59 on the To date stays; the next midnight does not.
    /// Bounds are UTC midnights so Npgsql timestamptz accepts them.
    /// </summary>
    private static IQueryable<Quote> ApplyRegisterWindow(
        IQueryable<Quote> query,
        DateTime? from,
        DateTime? to,
        QuoteStatus? status)
    {
        if (from.HasValue)
        {
            var start = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(q => q.QuoteDate >= start);
        }

        if (to.HasValue)
        {
            var endExclusive = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(q => q.QuoteDate < endExclusive);
        }

        if (status.HasValue)
        {
            var selected = status.Value;
            query = query.Where(q => q.Status == selected);
        }

        return query;
    }

    private async Task<IReadOnlyList<Quote>> LoadQuotesAsync(
        string? search,
        int page,
        int pageSize,
        QuoteBoardFilter filter,
        DateTime? from,
        DateTime? to,
        QuoteStatus? status,
        CancellationToken ct)
    {
        if (page < 1)
            page = 1;
        if (pageSize <= 0)
            pageSize = 20;

        var results = await QuotesForList(search, filter, from, to, status)
            .Include(q => q.Lines)
            .Include(q => q.Customer)
            .OrderByDescending(q => q.QuoteDate)
            .ThenByDescending(q => q.QuoteNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        ListCacheGraphHelper.PrepareQuotesForCache(results);
        return results;
    }

    public async Task<Guid> CreateAsync(Quote quote, CancellationToken ct = default)
    {
        if (quote.CustomerId == Guid.Empty)
            throw new InvalidOperationException("Customer is required for a quote.");

        var customer = await _dbContext.Set<Customer>().FindAsync([quote.CustomerId], ct);
        if (customer == null || customer.IsDeleted)
            throw new InvalidOperationException("Customer not found.");

        if (quote.TaxRate < 0 || quote.TaxRate > 1m)
            throw new InvalidOperationException("Tax rate must be between 0 and 1 (e.g. 0.15 for 15%).");
        if (quote.GrossProfitPercent < 0 || quote.GrossProfitPercent >= 1m)
            throw new InvalidOperationException(
                "Gross profit percent must be between 0 and 1 exclusive of 100% (e.g. 0.25 for 25%).");

        if (quote.QuoteDate != default && quote.ValidUntil.Date < quote.QuoteDate.Date)
            throw new InvalidOperationException("Valid-until date cannot be before the quote date.");
        if (quote.ValidUntil != default && quote.ValidUntil.Date > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Valid-until date cannot be more than 2 years in the future.");
        if (!string.IsNullOrWhiteSpace(quote.Notes))
        {
            quote.Notes = quote.Notes.Trim();
            if (quote.Notes.Length > 2000)
                throw new InvalidOperationException("Quote notes cannot exceed 2000 characters.");
        }

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? quote.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Quote, ct);

        // Generate a simple quote number if not provided
        if (string.IsNullOrWhiteSpace(quote.QuoteNumber))
        {
            quote.QuoteNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("Quote", "Q", ct)
                : $"Q-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }
        else
        {
            quote.QuoteNumber = quote.QuoteNumber.Trim();
            if (quote.QuoteNumber.Length > 50)
                throw new InvalidOperationException("Quote number cannot exceed 50 characters.");
            var numberTaken = await _dbContext.Set<Quote>()
                .AnyAsync(q => q.QuoteNumber == quote.QuoteNumber, ct);
            if (numberTaken)
                throw new InvalidOperationException(
                    $"Quote number '{quote.QuoteNumber}' already exists.");
        }

        quote.RecalculateTotals();

        _dbContext.Set<Quote>().Add(quote);
        await _dbContext.SaveChangesAsync(ct);

        await TryIncrementQuoteCountAsync(quote.TenantId, ct);
        await InvalidateListCachesAsync(ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "CREATE",
                "Quote",
                quote.QuoteNumber,
                $"Customer {quote.CustomerId}, total R {quote.Total:N2}",
                ct);
        }

        return quote.Id;
    }

    public async Task<Quote> CreateQuoteFromOpportunityAsync(Guid opportunityId, CancellationToken ct = default)
    {
        var opp = await _dbContext.Set<Opportunity>()
            .FirstOrDefaultAsync(o => o.Id == opportunityId, ct)
            ?? throw new InvalidOperationException("Opportunity not found.");

        if (opp.Stage == OpportunityStage.ClosedLost)
            throw new InvalidOperationException("Cannot create a quote from a Closed Lost deal.");

        if (opp.QuoteId is { } linked && linked != Guid.Empty)
            throw new InvalidOperationException("This deal already has a quote.");

        if (opp.CustomerId is not { } customerId || customerId == Guid.Empty)
            throw new InvalidOperationException("Link a customer on this deal before creating a quote.");

        var mentionsTravel = TravelLineRules.MentionsTravel(opp.Title)
            || TravelLineRules.MentionsTravel(opp.Notes);
        var scopeDescription = mentionsTravel && TravelLineRules.MentionsTravel(opp.Title)
            ? "Scope of work"
            : TrimTo(string.IsNullOrWhiteSpace(opp.Title) ? "Scope of work" : opp.Title, 500);

        var quote = new Quote
        {
            CustomerId = customerId,
            QuoteDate = DateTime.UtcNow,
            ValidUntil = DateTime.UtcNow.AddDays(30),
            TaxRate = 0.15m,
            Status = QuoteStatus.Draft,
            Notes = BuildOpportunityQuoteNotes(opp, mentionsTravel),
            Lines =
            {
                new QuoteLine
                {
                    Description = scopeDescription,
                    Quantity = 1,
                    UnitPrice = opp.Value < 0 ? 0 : opp.Value,
                    Unit = "lot",
                    LineType = "Other"
                }
            }
        };

        if (mentionsTravel)
        {
            quote.Lines.Add(new QuoteLine
            {
                Description = "Travel",
                Quantity = 1,
                UnitPrice = 0,
                Unit = "lot",
                LineType = TravelLineRules.TravelType
            });
        }

        foreach (var line in quote.Lines)
        {
            line.LineType = TravelLineRules.EnsureExplicitType(line.LineType, line.Description);
            if (TravelLineRules.IsTravelLine(line.LineType, line.Description)
                && string.Equals(line.LineType, TravelLineRules.MaterialType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Travel must stay an explicit Travel line, not a materials lump.");
            }
        }

        var quoteId = await CreateAsync(quote, ct);

        opp.QuoteId = quoteId;
        if (opp.Stage is OpportunityStage.Lead or OpportunityStage.Qualified or OpportunityStage.Proposal or OpportunityStage.Negotiation)
            opp.Stage = OpportunityStage.Proposal;

        await _dbContext.SaveChangesAsync(ct);
        _cache?.InvalidateCategory(TenantCacheCategories.Opportunities);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "CONVERT",
                "Opportunity",
                opp.Title,
                $"Created quote {quote.QuoteNumber}" + (mentionsTravel ? " with an explicit travel line" : ""),
                ct);
        }

        return (await GetByIdAsync(quoteId, ct))!;
    }

    private static string? BuildOpportunityQuoteNotes(Opportunity opp, bool mentionsTravel)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(opp.Title))
            parts.Add(opp.Title.Trim());
        if (!string.IsNullOrWhiteSpace(opp.Notes))
            parts.Add(opp.Notes.Trim());
        if (mentionsTravel)
            parts.Add("Travel is an explicit line and is not included in materials.");
        if (parts.Count == 0)
            return null;
        var notes = string.Join(". ", parts);
        return notes.Length <= 2000 ? notes : notes[..2000];
    }

    private static string TrimTo(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    public async Task UpdateAsync(Quote quote, CancellationToken ct = default)
    {
        var existing = await _dbContext.Set<Quote>()
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == quote.Id, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        EnforceSendGate(existing, quote);

        Customer? sendTo = null;
        var becomingSent = quote.Status == QuoteStatus.Sent && existing.Status != QuoteStatus.Sent;
        if (becomingSent)
        {
            var hasLines = await _dbContext.Set<QuoteLine>()
                .AnyAsync(l => l.QuoteId == quote.Id && !l.IsDeleted, ct);
            if (!hasLines)
                throw new InvalidOperationException("Cannot send a quote with no lines.");

            sendTo = await _dbContext.Set<Customer>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == existing.CustomerId
                    && (existing.TenantId == Guid.Empty || c.TenantId == existing.TenantId), ct);
            if (sendTo == null || sendTo.IsDeleted)
                throw new InvalidOperationException(
                    "Cannot send quote — customer is missing or deleted.");
            if (string.IsNullOrWhiteSpace(sendTo.Email))
                throw new InvalidOperationException(
                    "Cannot send quote — customer has no email. Add an email so the customer can receive it.");
        }

        if (quote.TaxRate < 0 || quote.TaxRate > 1m)
            throw new InvalidOperationException("Tax rate must be between 0 and 1 (e.g. 0.15 for 15%).");
        if (quote.GrossProfitPercent < 0 || quote.GrossProfitPercent >= 1m)
            throw new InvalidOperationException(
                "Gross profit percent must be between 0 and 1 exclusive of 100% (e.g. 0.25 for 25%).");
        if (quote.QuoteDate != default && quote.ValidUntil.Date < quote.QuoteDate.Date)
            throw new InvalidOperationException("Valid-until date cannot be before the quote date.");
        if (quote.ValidUntil != default && quote.ValidUntil.Date > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Valid-until date cannot be more than 2 years in the future.");
        if (!string.IsNullOrWhiteSpace(quote.Notes))
        {
            quote.Notes = quote.Notes.Trim();
            if (quote.Notes.Length > 2000)
                throw new InvalidOperationException("Quote notes cannot exceed 2000 characters.");
        }

        if (quote.CustomerId == Guid.Empty)
            quote.CustomerId = existing.CustomerId;
        else if (quote.CustomerId != existing.CustomerId)
        {
            var customer = await _dbContext.Set<Customer>().FindAsync([quote.CustomerId], ct);
            if (customer == null || customer.IsDeleted)
                throw new InvalidOperationException("Customer not found.");
        }

        // Document number is assigned once; do not allow free-form renumbering.
        quote.QuoteNumber = existing.QuoteNumber;
        // Approval chain is controlled by submit/approve/reject methods.
        quote.ApprovalStatus = existing.ApprovalStatus;
        quote.SubmittedForApprovalAt = existing.SubmittedForApprovalAt;
        quote.SubmittedForApprovalByUserId = existing.SubmittedForApprovalByUserId;
        quote.ExecutiveApprovedAt = existing.ExecutiveApprovedAt;
        quote.ExecutiveApprovedByUserId = existing.ExecutiveApprovedByUserId;

        quote.RecalculateTotals();
        _dbContext.Set<Quote>().Update(quote);
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);

        var emailNote = "";
        if (becomingSent && sendTo != null)
            emailNote = await TryEmailQuoteSentAsync(quote, sendTo, ct);

        var becomingAccepted = quote.Status == QuoteStatus.Accepted && existing.Status != QuoteStatus.Accepted;
        if (becomingAccepted && _notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = quote.TenantId != Guid.Empty ? quote.TenantId : existing.TenantId,
                Title = $"Quote {quote.QuoteNumber} accepted — convert to job",
                Message = $"{quote.QuoteNumber} (R {quote.Total:N0}) is accepted. Convert it from Home so deposit and work can start.",
                Category = "sales",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = quote.Id,
                RelatedEntityType = nameof(Quote)
            }, ct);
        }

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "UPDATE",
                "Quote",
                quote.QuoteNumber,
                $"Status {quote.Status}, approval {quote.ApprovalStatus}, total R {quote.Total:N2}{emailNote}",
                ct);
        }
    }

    private async Task<string> TryEmailQuoteSentAsync(Quote quote, Customer customer, CancellationToken ct)
    {
        var email = customer.Email!.Trim();
        if (_email?.IsConfigured != true)
            return " (SMTP not configured — recorded sent in-system only)";

        var html = $"""
            <p>Please find quote <strong>{quote.QuoteNumber}</strong>.</p>
            <ul>
              <li><strong>Total:</strong> R {quote.Total:N2}</li>
              <li><strong>Valid until:</strong> {quote.ValidUntil:yyyy-MM-dd}</li>
            </ul>
            <p>Reply if you would like to proceed.</p>
            """;
        await _email.SendEmailAsync(email, $"Quote {quote.QuoteNumber}", html, ct);
        return $" (emailed {email})";
    }

    public async Task SubmitForExecutiveApprovalAsync(Guid quoteId, Guid submittedByUserId, CancellationToken ct = default)
    {
        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        if (quote.Status != QuoteStatus.Draft)
            throw new InvalidOperationException("Only draft quotes can be submitted for executive approval.");

        if (!quote.Lines.Any(l => !l.IsDeleted))
            throw new InvalidOperationException("Add at least one line before submitting for approval.");

        await EnsureQuoteCustomerPresentAsync(quote, ct);

        quote.ApprovalStatus = QuoteApprovalStatus.PendingExecutive;
        quote.SubmittedForApprovalByUserId = submittedByUserId;
        quote.SubmittedForApprovalAt = DateTime.UtcNow;
        quote.ExecutiveRejectionReason = null;
        quote.ExecutiveDecisionNote = null;

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "SUBMIT_APPROVAL",
                "Quote",
                quote.QuoteNumber,
                "Submitted for executive approval before client send",
                ct);
        }

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = quote.TenantId,
                Title = $"Quote {quote.QuoteNumber} needs executive approval",
                Message = $"{quote.QuoteNumber} (R {quote.Total:N0}) is waiting for executive approval before it can be sent to the customer.",
                Category = "sales",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = quote.Id,
                RelatedEntityType = nameof(Quote)
            }, ct);
        }
    }

    public async Task ExecutiveApproveAsync(Guid quoteId, Guid approverUserId, string? note = null, CancellationToken ct = default)
    {
        var quote = await _dbContext.Set<Quote>().FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        if (quote.ApprovalStatus != QuoteApprovalStatus.PendingExecutive)
            throw new InvalidOperationException("Quote is not pending executive approval.");

        // Customer may have been soft-deleted after submit — fail before marking approved.
        await EnsureQuoteCustomerPresentAsync(quote, ct);

        var decisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (decisionNote != null && decisionNote.Length > 500)
            throw new InvalidOperationException("Approval note cannot exceed 500 characters.");

        quote.ApprovalStatus = QuoteApprovalStatus.ExecutiveApproved;
        quote.ExecutiveApprovedByUserId = approverUserId;
        quote.ExecutiveApprovedAt = DateTime.UtcNow;
        quote.ExecutiveRejectionReason = null;
        quote.ExecutiveDecisionNote = decisionNote;

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "APPROVE",
                "Quote",
                quote.QuoteNumber,
                string.IsNullOrWhiteSpace(decisionNote)
                    ? "Executive approved for client send"
                    : $"Executive approved: {decisionNote}",
                ct);
        }

        if (_notifications != null)
        {
            var noteBit = string.IsNullOrWhiteSpace(decisionNote) ? "" : $" Note: {decisionNote}";
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = quote.TenantId,
                Title = $"Quote {quote.QuoteNumber} approved — send to customer",
                Message = $"{quote.QuoteNumber} is approved. Send it to the customer to start the cash cycle.{noteBit}",
                Category = "sales",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = quote.Id,
                RelatedEntityType = nameof(Quote)
            }, ct);
        }
    }

    public async Task ExecutiveRejectAsync(Guid quoteId, Guid approverUserId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Rejection reason is required.");
        reason = reason.Trim();
        if (reason.Length < 3)
            throw new InvalidOperationException("Rejection reason must be at least 3 characters.");
        if (reason.Length > 500)
            throw new InvalidOperationException("Rejection reason cannot exceed 500 characters.");

        var quote = await _dbContext.Set<Quote>().FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        if (quote.ApprovalStatus != QuoteApprovalStatus.PendingExecutive)
            throw new InvalidOperationException("Quote is not pending executive approval.");

        quote.ApprovalStatus = QuoteApprovalStatus.Rejected;
        quote.ExecutiveRejectionReason = reason;
        quote.ExecutiveDecisionNote = reason;
        quote.ExecutiveApprovedByUserId = approverUserId;
        quote.ExecutiveApprovedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "REJECT",
                "Quote",
                quote.QuoteNumber,
                $"Executive rejected: {reason}",
                ct);
        }

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = quote.TenantId,
                Title = $"Quote {quote.QuoteNumber} rejected",
                Message = $"{quote.QuoteNumber} was rejected: {reason}",
                Category = "sales",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = quote.Id,
                RelatedEntityType = nameof(Quote)
            }, ct);
        }
    }

    public async Task WithdrawFromApprovalAsync(Guid quoteId, Guid userId, string? reason = null, CancellationToken ct = default)
    {
        var quote = await _dbContext.Set<Quote>().FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        if (quote.ApprovalStatus != QuoteApprovalStatus.PendingExecutive)
            throw new InvalidOperationException("Only quotes pending executive approval can be withdrawn.");

        if (quote.Status != QuoteStatus.Draft)
            throw new InvalidOperationException("Only draft quotes can be withdrawn from approval.");

        if (!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length > 500)
            throw new InvalidOperationException("Withdrawal reason cannot exceed 500 characters.");

        quote.ApprovalStatus = QuoteApprovalStatus.None;
        quote.SubmittedForApprovalAt = null;
        quote.SubmittedForApprovalByUserId = null;
        quote.ExecutiveRejectionReason = string.IsNullOrWhiteSpace(reason)
            ? "Withdrawn by estimator"
            : reason.Trim();

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "WITHDRAW_APPROVAL",
                "Quote",
                quote.QuoteNumber,
                $"Withdrawn by {userId:N}: {quote.ExecutiveRejectionReason}",
                ct);
        }
    }

    public async Task<IReadOnlyList<Quote>> GetPendingExecutiveApprovalAsync(CancellationToken ct = default)
    {
        return await _dbContext.Set<Quote>()
            .AsNoTracking()
            .Include(q => q.Customer)
            .Where(q => q.ApprovalStatus == QuoteApprovalStatus.PendingExecutive)
            .OrderByDescending(q => q.SubmittedForApprovalAt)
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ConvertibleDocumentRow>> GetUnconvertedWonQuotesAsync(
        int take = 20,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 50);
        var quotes = await _dbContext.Set<Quote>()
            .AsNoTracking()
            .Include(q => q.Customer)
            .Where(q => (q.Status == QuoteStatus.Sent || q.Status == QuoteStatus.Accepted)
                && q.Lines.Any(l => !l.IsDeleted))
            .OrderByDescending(q => q.Total)
            .Take(take * 3)
            .ToListAsync(ct);

        if (quotes.Count == 0)
            return Array.Empty<ConvertibleDocumentRow>();

        var quoteIds = quotes.Select(q => q.Id).ToList();
        var converted = (await _dbContext.Set<Job>().AsNoTracking()
            .Where(j => j.QuoteId != null && quoteIds.Contains(j.QuoteId.Value))
            .Select(j => j.QuoteId!.Value)
            .ToListAsync(ct)).ToHashSet();

        return quotes
            .Where(q => !converted.Contains(q.Id))
            .Select(q => new ConvertibleDocumentRow(
                q.Id,
                "Quote",
                q.QuoteNumber,
                q.Customer?.Name ?? "—",
                q.Total,
                $"/quotes?open={q.Id:D}"))
            .Take(take)
            .ToList();
    }

    public async Task<IReadOnlyList<ConvertibleDocumentRow>> GetApprovedUnsentQueueAsync(
        int take = 20,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 50);
        var quotes = await _dbContext.Set<Quote>()
            .AsNoTracking()
            .Include(q => q.Customer)
            .Where(q =>
                q.Status == QuoteStatus.Draft
                && q.ApprovalStatus == QuoteApprovalStatus.ExecutiveApproved
                && q.Lines.Any())
            .OrderByDescending(q => q.Total)
            .ThenBy(q => q.QuoteNumber)
            .Take(take)
            .ToListAsync(ct);

        return quotes
            .Select(q => new ConvertibleDocumentRow(
                q.Id,
                "Quote",
                q.QuoteNumber,
                q.Customer?.Name ?? "—",
                q.Total,
                $"/quotes?open={q.Id:D}"))
            .ToList();
    }

    public async Task SendAsync(Guid quoteId, CancellationToken ct = default)
    {
        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        if (quote.Status == QuoteStatus.Sent)
            return;
        if (quote.Status != QuoteStatus.Draft)
            throw new InvalidOperationException($"Cannot send quote in status {quote.Status}.");

        quote.Status = QuoteStatus.Sent;
        await UpdateAsync(quote, ct);
    }

    private static void EnforceSendGate(Quote existing, Quote updated)
    {
        if (updated.Status == QuoteStatus.Sent && existing.Status != QuoteStatus.Sent
            && updated.ApprovalStatus != QuoteApprovalStatus.ExecutiveApproved)
        {
            throw new InvalidOperationException(
                "Executive approval is required before marking a quote as Sent. Submit for approval first.");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

        if (quote == null) return;

        if (quote.Status is not (QuoteStatus.Draft or QuoteStatus.Rejected or QuoteStatus.Expired))
            throw new InvalidOperationException(
                $"Cannot delete quote in status {quote.Status}. Only Draft, Rejected, or Expired quotes can be deleted.");

        var linkedJob = await _dbContext.Set<Job>().AsNoTracking()
            .AnyAsync(j => j.QuoteId == quote.Id, ct);
        if (linkedJob)
            throw new InvalidOperationException(
                $"Cannot delete quote {quote.QuoteNumber} — it is linked to a job.");

        foreach (var line in quote.Lines)
        {
            line.IsDeleted = true;
        }
        quote.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);

        if (_auditService != null)
            await _auditService.LogAsync("DELETE", "Quote", quote.QuoteNumber, "Soft deleted", ct);
    }

    public Task<Guid> AddLineAsync(QuoteLine line, CancellationToken ct = default)
        => AddLineCoreAsync(line, allowPending: false, ct);

    public Task<Guid> ExecutiveAddLineAsync(QuoteLine line, CancellationToken ct = default)
        => AddLineCoreAsync(line, allowPending: true, ct);

    private async Task<Guid> AddLineCoreAsync(QuoteLine line, bool allowPending, CancellationToken ct)
    {
        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == line.QuoteId, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        EnsureQuoteLinesEditable(quote, allowPending);

        ValidateLine(line);

        _dbContext.Set<QuoteLine>().Add(line);
        await _dbContext.SaveChangesAsync(ct);

        await _dbContext.Entry(quote).Collection(q => q.Lines).LoadAsync(ct);
        quote.RecalculateTotals();
        await _dbContext.SaveChangesAsync(ct);

        await InvalidateListCachesAsync(ct);
        return line.Id;
    }

    public Task ExecutiveReviseLineAsync(QuoteLine line, CancellationToken ct = default)
        => UpdateLineCoreAsync(line, allowPending: true, ct);

    public async Task UpdateLineAsync(QuoteLine line, CancellationToken ct = default)
        => await UpdateLineCoreAsync(line, allowPending: false, ct);

    private async Task UpdateLineCoreAsync(QuoteLine line, bool allowPending, CancellationToken ct)
    {
        var existing = await _dbContext.Set<QuoteLine>().FirstOrDefaultAsync(l => l.Id == line.Id, ct);
        if (existing == null) return;

        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == existing.QuoteId, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        EnsureQuoteLinesEditable(quote, allowPending);

        ValidateLine(line);

        existing.Description = line.Description;
        existing.LineType = line.LineType;
        existing.Quantity = line.Quantity;
        existing.Unit = line.Unit;
        existing.UnitCost = line.UnitCost;
        existing.GrossProfitPercent = line.GrossProfitPercent;
        existing.UnitPrice = line.UnitPrice;

        await _dbContext.SaveChangesAsync(ct);

        quote.RecalculateTotals();
        await _dbContext.SaveChangesAsync(ct);

        await InvalidateListCachesAsync(ct);
    }

    public Task DeleteLineAsync(Guid lineId, CancellationToken ct = default)
        => DeleteLineCoreAsync(lineId, allowPending: false, ct);

    public Task ExecutiveDeleteLineAsync(Guid lineId, CancellationToken ct = default)
        => DeleteLineCoreAsync(lineId, allowPending: true, ct);

    private async Task DeleteLineCoreAsync(Guid lineId, bool allowPending, CancellationToken ct)
    {
        var line = await _dbContext.Set<QuoteLine>().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line == null) return;

        var quoteId = line.QuoteId;
        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote == null) return;

        EnsureQuoteLinesEditable(quote, allowPending);

        line.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);

        quote.RecalculateTotals();
        await _dbContext.SaveChangesAsync(ct);

        await InvalidateListCachesAsync(ct);
    }

    private static void EnsureQuoteLinesEditable(Quote quote, bool allowPending = false)
    {
        if (quote.Status != QuoteStatus.Draft)
            throw new InvalidOperationException(
                $"Lines can only be changed on draft quotes (current status: {quote.Status}).");

        if (!allowPending && quote.ApprovalStatus == QuoteApprovalStatus.PendingExecutive)
            throw new InvalidOperationException(
                "Quote is pending executive approval — withdraw approval before editing lines.");
    }

    private static void ValidateLine(QuoteLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Description))
            throw new InvalidOperationException("Line description is required.");
        if (line.Quantity <= 0)
            throw new InvalidOperationException("Line quantity must be positive.");
        if (line.Quantity > 1_000_000m)
            throw new InvalidOperationException("Line quantity cannot exceed 1,000,000.");
        if (line.UnitPrice < 0)
            throw new InvalidOperationException("Line unit price cannot be negative.");
        if (line.UnitPrice > 10_000_000m)
            throw new InvalidOperationException("Line unit price cannot exceed 10,000,000.");
        if (line.UnitCost < 0)
            throw new InvalidOperationException("Line unit cost cannot be negative.");
        if (line.UnitCost > 10_000_000m)
            throw new InvalidOperationException("Line unit cost cannot exceed 10,000,000.");
        if (line.GrossProfitPercent < 0 || line.GrossProfitPercent >= 1m)
            throw new InvalidOperationException(
                "Line gross profit percent must be between 0 and 1 exclusive of 100%.");

        line.Description = line.Description.Trim();
        if (line.Description.Length > 500)
            throw new InvalidOperationException("Line description cannot exceed 500 characters.");
        if (!string.IsNullOrWhiteSpace(line.Unit))
        {
            line.Unit = line.Unit.Trim();
            if (line.Unit.Length > 20)
                throw new InvalidOperationException("Line unit cannot exceed 20 characters.");
        }
        line.LineType = TravelLineRules.EnsureExplicitType(line.LineType, line.Description);
        if (line.LineType.Length > 50)
            throw new InvalidOperationException("Line type cannot exceed 50 characters.");
    }

    public async Task<Job> ConvertToJobAsync(Guid quoteId, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider?.GetCurrentTenantId()
            ?? _dbContext.CurrentTenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Job, ct);

        // IgnoreQueryFilters: required Customer navigation + soft-delete filter would hide
        // quotes for deleted customers; we still need a clear rejection message.
        var quote = await _dbContext.Set<Quote>()
            .IgnoreQueryFilters()
            .Include(q => q.Lines)
            .Include(q => q.Customer)
            .FirstOrDefaultAsync(q =>
                q.Id == quoteId
                && !q.IsDeleted
                && (tenantId == Guid.Empty || q.TenantId == tenantId), ct);

        if (quote == null)
            throw new InvalidOperationException("Quote not found.");

        if (quote.Status is QuoteStatus.Rejected or QuoteStatus.Expired)
            throw new InvalidOperationException($"Cannot convert a {quote.Status} quote to a job.");

        if (!quote.Lines.Any(l => !l.IsDeleted))
            throw new InvalidOperationException("Cannot convert a quote with no lines to a job.");

        if (quote.Customer == null || quote.Customer.IsDeleted)
            throw new InvalidOperationException("Cannot convert a quote whose customer is missing or deleted.");

        var alreadyConverted = await _dbContext.Set<Job>()
            .AsNoTracking()
            .AnyAsync(j => j.QuoteId == quote.Id, ct);
        if (alreadyConverted)
            throw new InvalidOperationException(
                $"Quote {quote.QuoteNumber} has already been converted to a job.");

        if (quote.Status != QuoteStatus.Accepted)
        {
            quote.Status = QuoteStatus.Accepted;
        }

        var job = new Job
        {
            QuoteId = quote.Id,
            CustomerId = quote.CustomerId,
            JobNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("Job", "J", ct)
                : $"J-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            Title = $"Job from {quote.QuoteNumber}",
            Description = quote.Notes,
            QuotedTotal = quote.Total,
            ActualCost = 0,
            ScheduledStart = DateTime.UtcNow.AddDays(7),
            Status = JobStatus.Scheduled
        };

        if (quote.Customer != null)
        {
            job.Title = $"{quote.Customer.Name} - {quote.QuoteNumber}";
        }

        _dbContext.Set<Job>().Add(job);
        await _dbContext.SaveChangesAsync(ct);

        // Explicit travel from quote lines. Never file travel as Material.
        foreach (var line in quote.Lines.Where(l => !l.IsDeleted))
        {
            var promoted = TravelLineRules.EnsureExplicitType(line.LineType, line.Description);
            if (!string.Equals(line.LineType, promoted, StringComparison.Ordinal))
                line.LineType = promoted;

            if (!TravelLineRules.IsTravelLine(line.LineType, line.Description))
                continue;

            var costType = TravelLineRules.JobCostType(line.LineType, line.Description);
            if (string.Equals(costType, TravelLineRules.MaterialType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Travel cannot be folded into a materials cost.");

            _dbContext.Set<JobCost>().Add(new JobCost
            {
                JobId = job.Id,
                Description = string.IsNullOrWhiteSpace(line.Description) ? "Travel" : line.Description,
                Amount = line.LineTotal,
                CostType = costType,
                CostDate = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(ct);

        await InvalidateListCachesAsync(ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "CONVERT",
                "Quote",
                quote.QuoteNumber,
                $"Converted to job {job.JobNumber} with explicit travel costs",
                ct);
        }

        var counterTenantId = tenantId != Guid.Empty ? tenantId : quote.TenantId;
        await TryIncrementJobCountAsync(counterTenantId, ct);

        if (_notifications != null && job.NeedsDepositInvoice())
        {
            var amount = Math.Round(job.QuotedTotal * job.DepositPercent / 100m, 2);
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = job.TenantId != Guid.Empty ? job.TenantId : quote.TenantId,
                Title = $"Raise deposit for {job.JobNumber}",
                Message = $"{job.Title}: {job.DepositPercent:N0}% deposit (R {amount:N0}) is outstanding after converting {quote.QuoteNumber}.",
                Category = "collections",
                TargetRoles = "Admin,Executive,Finance",
                RelatedEntityId = job.Id,
                RelatedEntityType = nameof(Job)
            }, ct);
        }

        return (await GetByIdForJobAsync(job.Id, ct))!;
    }

    private async Task EnsureQuoteCustomerPresentAsync(Quote quote, CancellationToken ct)
    {
        var customer = await _dbContext.Set<Customer>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == quote.CustomerId
                && (quote.TenantId == Guid.Empty || c.TenantId == quote.TenantId), ct);
        if (customer == null || customer.IsDeleted)
            throw new InvalidOperationException(
                "Cannot process quote — customer is missing or deleted.");
    }

    private Task InvalidateListCachesAsync(CancellationToken ct) =>
        _cache == null
            ? Task.CompletedTask
            : TenantCacheInvalidation.OnQuoteMutatedAsync(_cache, ct);

    private async Task TryIncrementQuoteCountAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || _tenantService == null) return;
        try
        {
            await _tenantService.IncrementQuoteCountAsync(tenantId, ct);
        }
        catch
        {
            // Best-effort commercial tracking — must not break business operations.
        }
    }

    private async Task TryIncrementJobCountAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || _tenantService == null) return;
        try
        {
            await _tenantService.IncrementJobCountAsync(tenantId, ct);
        }
        catch
        {
            // Best-effort commercial tracking — must not break business operations.
        }
    }

    private async Task<Job?> GetByIdForJobAsync(Guid id, CancellationToken ct)
    {
        return await _dbContext.Set<Job>()
            .Include(j => j.ActualCosts)
            .Include(j => j.Customer)
            .Include(j => j.Quote)
                .ThenInclude(q => q!.Lines)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
    }
}
