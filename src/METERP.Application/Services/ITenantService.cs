using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Application service contract for basic tenant management (Phase 1 foundation).
/// </summary>
public interface ITenantService
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken ct = default);
    Task<IReadOnlyList<Tenant>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<Guid> CreateAsync(string name, string subdomain, CancellationToken ct = default);
    Task UpdateAsync(Tenant tenant, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Commercial usage tracking stubs. Increments are best-effort and tenant-isolated.
    /// </summary>
    Task IncrementJobCountAsync(Guid tenantId, CancellationToken ct = default);
    Task IncrementAiCallCountAsync(Guid tenantId, CancellationToken ct = default);
    Task IncrementQuoteCountAsync(Guid tenantId, CancellationToken ct = default);
    Task IncrementInvoiceCountAsync(Guid tenantId, decimal revenueAmount = 0, CancellationToken ct = default);
    /// <summary>
    /// Replaces stored lifetime and current-month counters with COUNT/SUM of this tenant's documents.
    /// Returns the updated tenant (detached). AI call counters are left as stored — there is no document to recount.
    /// </summary>
    Task<Tenant?> RefreshUsageCountersAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Quotes, jobs, and invoices created since the current UTC month start.
    /// AI calls are the stored period counter (there is no document row to recount).
    /// Null when the tenant does not exist.
    /// </summary>
    Task<TenantPeriodUsage?> GetLivePeriodUsageAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Live monthly usage for the quota strip. Not the lifetime document totals.</summary>
public sealed record TenantPeriodUsage(int Quotes, int Jobs, int Invoices, int AiCalls);
