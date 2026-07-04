using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Basic implementation of ITenantService using EF Core.
/// In a full system this would include validation, subdomain uniqueness checks,
/// provisioning logic, etc.
/// </summary>
public class TenantService : ITenantService
{
    private readonly AppDbContext _dbContext;
    private readonly IServiceScopeFactory _scopeFactory;

    public TenantService(AppDbContext dbContext, IServiceScopeFactory scopeFactory)
    {
        _dbContext = dbContext;
        _scopeFactory = scopeFactory;
    }

    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        // Use IgnoreQueryFilters because tenant listing/management may be cross-tenant or admin
        return await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
    }

    public async Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken ct = default)
    {
        return await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain && !t.IsDeleted, ct);
    }

    public async Task<IReadOnlyList<Tenant>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term) ||
                t.Subdomain.ToLower().Contains(term));
        }

        return await query
            .OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(string name, string subdomain, CancellationToken ct = default)
    {
        var tenant = new Tenant
        {
            Name = name.Trim(),
            Subdomain = subdomain.Trim().ToLowerInvariant(),
            IsActive = true,
            Tier = SubscriptionTier.Starter,
            UsagePeriodStartUtc = QuotaService.GetCurrentPeriodStartUtc()
        };
        TenantQuotaDefaults.ApplyTierDefaults(tenant);

        // Note: TenantId will be set by DbContext SaveChanges, but for the very first tenant(s)
        // we may want to allow empty or use a special value. For foundation we let it be Guid.Empty
        // or the current one. In practice the first tenant creation often happens outside a tenant context.

        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync(ct);

        return tenant.Id;
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken ct = default)
    {
        var existing = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenant.Id, ct);

        if (existing == null) return;

        existing.Name = tenant.Name?.Trim() ?? string.Empty;
        existing.Subdomain = tenant.Subdomain?.Trim().ToLowerInvariant() ?? string.Empty;
        existing.IsActive = tenant.IsActive;
        existing.Tier = tenant.Tier;
        existing.EnabledFeatures = tenant.EnabledFeatures ?? string.Empty;
        existing.MaxQuotesPerMonth = tenant.MaxQuotesPerMonth;
        existing.MaxJobsPerMonth = tenant.MaxJobsPerMonth;
        existing.MaxInvoicesPerMonth = tenant.MaxInvoicesPerMonth;
        existing.MaxAiCallsPerMonth = tenant.MaxAiCallsPerMonth;
        existing.InvoiceWebhookUrl = string.IsNullOrWhiteSpace(tenant.InvoiceWebhookUrl)
            ? null
            : tenant.InvoiceWebhookUrl.Trim();
        existing.NotificationEmail = string.IsNullOrWhiteSpace(tenant.NotificationEmail)
            ? null
            : tenant.NotificationEmail.Trim();
        existing.StripeCustomerId = string.IsNullOrWhiteSpace(tenant.StripeCustomerId)
            ? null
            : tenant.StripeCustomerId.Trim();
        existing.SubscriptionStatus = string.IsNullOrWhiteSpace(tenant.SubscriptionStatus)
            ? null
            : tenant.SubscriptionStatus.Trim();
        existing.AiProvider = string.IsNullOrWhiteSpace(tenant.AiProvider) ? null : tenant.AiProvider.Trim();
        existing.AiApiKeyEncrypted = string.IsNullOrWhiteSpace(tenant.AiApiKeyEncrypted)
            ? null
            : tenant.AiApiKeyEncrypted;
        existing.AiBaseUrl = string.IsNullOrWhiteSpace(tenant.AiBaseUrl) ? null : tenant.AiBaseUrl.TrimEnd('/');
        existing.AiModel = string.IsNullOrWhiteSpace(tenant.AiModel) ? null : tenant.AiModel.Trim();
        existing.AiUseTenantKey = tenant.AiUseTenantKey;

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tenant == null) return;

        tenant.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
    }

    public Task IncrementJobCountAsync(Guid tenantId, CancellationToken ct = default) =>
        IncrementCounterAsync(tenantId, ct, tenant =>
        {
            tenant.TotalJobsCreated++;
            tenant.PeriodJobsCreated++;
        });

    public Task IncrementAiCallCountAsync(Guid tenantId, CancellationToken ct = default) =>
        IncrementCounterAsync(tenantId, ct, tenant =>
        {
            tenant.TotalAiCalls++;
            tenant.PeriodAiCalls++;
        });

    public Task IncrementQuoteCountAsync(Guid tenantId, CancellationToken ct = default) =>
        IncrementCounterAsync(tenantId, ct, tenant =>
        {
            tenant.TotalQuotesCreated++;
            tenant.PeriodQuotesCreated++;
        });

    public Task IncrementInvoiceCountAsync(Guid tenantId, decimal revenueAmount = 0, CancellationToken ct = default) =>
        IncrementCounterAsync(tenantId, ct, tenant =>
        {
            tenant.TotalInvoicesIssued++;
            tenant.PeriodInvoicesIssued++;
            tenant.TotalRevenueBilled += revenueAmount;
        });

    /// <summary>
    /// Usage counters run in an isolated scope so Blazor Server circuits never contend
    /// with the caller's active DbContext (e.g. invoice create + navigate overlap).
    /// </summary>
    private async Task IncrementCounterAsync(
        Guid tenantId,
        CancellationToken ct,
        Action<Tenant> applyIncrement)
    {
        if (tenantId == Guid.Empty) return;

        const int maxAttempts = 8;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var tenant = await db.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, ct);

                if (tenant == null) return;

                await QuotaService.EnsureCurrentPeriodAsync(tenant, db, ct);
                applyIncrement(tenant);
                tenant.LastActivityUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(15 * (attempt + 1), ct);
            }
        }
    }
}
