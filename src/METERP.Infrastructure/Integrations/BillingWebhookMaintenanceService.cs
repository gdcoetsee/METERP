using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Integrations;

public class BillingWebhookMaintenanceService : IBillingWebhookMaintenanceService
{
    private readonly AppDbContext _dbContext;

    public BillingWebhookMaintenanceService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<int> PurgeProcessedEventsOlderThanAsync(TimeSpan retention, CancellationToken ct = default)
    {
        if (retention <= TimeSpan.Zero)
            return 0;

        var cutoff = DateTime.UtcNow - retention;
        var stale = await _dbContext.ProcessedStripeWebhookEvents
            .Where(e => e.ProcessedAtUtc < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0)
            return 0;

        _dbContext.ProcessedStripeWebhookEvents.RemoveRange(stale);
        await _dbContext.SaveChangesAsync(ct);
        return stale.Count;
    }
}