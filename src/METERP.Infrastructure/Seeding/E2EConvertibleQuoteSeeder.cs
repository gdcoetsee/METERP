using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;

// QuotaExceededException thrown when demo tenant hits monthly quote cap after many E2E runs.

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Idempotent Sent quote with travel line for convert-to-job E2E.
/// Soft-deletes all prior convertible-demo quotes and creates a fresh one.
/// </summary>
public static class E2EConvertibleQuoteSeeder
{
    public const string DemoNotesMarker = "E2E convertible quote";

    public static async Task<string?> EnsureSentConvertibleQuoteAsync(
        IQuoteService quoteService,
        ICustomerService customerService,
        ITenantProvider tenantProvider,
        Guid tenantId,
        CancellationToken ct = default)
    {
        tenantProvider.SetTenantId(tenantId);

        var demoQuotes = (await quoteService.GetAllAsync(pageSize: 500, ct: ct))
            .Where(q => q.Notes != null
                        && q.Notes.Contains(DemoNotesMarker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var candidate in demoQuotes.Where(q => q.Status is QuoteStatus.Draft or QuoteStatus.Sent))
        {
            var full = await quoteService.GetByIdAsync(candidate.Id, ct);
            if (full?.Lines.Any(l => !l.IsDeleted) == true)
                return full.QuoteNumber;
        }

        foreach (var stale in demoQuotes.Where(q => q.Status is QuoteStatus.Draft or QuoteStatus.Rejected or QuoteStatus.Expired))
        {
            try
            {
                await quoteService.DeleteAsync(stale.Id, ct);
            }
            catch (InvalidOperationException)
            {
                // Linked/converted quotes stay — never crash host or E2E reset.
            }
        }

        var customer = (await customerService.GetAllAsync(ct: ct))
            .FirstOrDefault(c => c.Name.Contains("Hospital", StringComparison.OrdinalIgnoreCase))
            ?? (await customerService.GetAllAsync(ct: ct)).FirstOrDefault();
        if (customer == null)
            return null;

        try
        {
            var quoteId = await quoteService.CreateAsync(new Quote
            {
                CustomerId = customer.Id,
                QuoteDate = DateTime.UtcNow,
                ValidUntil = DateTime.UtcNow.AddDays(29),
                Status = QuoteStatus.Sent,
                TaxRate = 0.15m,
                Notes = DemoNotesMarker,
                Lines =
                {
                    new QuoteLine
                    {
                        Description = "Panel upgrade labour (8 hours)",
                        Quantity = 8,
                        UnitPrice = 195m,
                        LineType = "Labour",
                        Unit = "hr"
                    },
                    new QuoteLine
                    {
                        Description = "Travel & site transport (explicit contractor cost)",
                        Quantity = 1,
                        UnitPrice = 620m,
                        LineType = "Travel",
                        Unit = "lot"
                    }
                }
            }, ct);

            return (await quoteService.GetByIdAsync(quoteId, ct))?.QuoteNumber;
        }
        catch (QuotaExceededException)
        {
            var existing = (await quoteService.GetAllAsync("E2E convertible", pageSize: 5, ct: ct))
                .FirstOrDefault(q => q.Status is QuoteStatus.Draft or QuoteStatus.Sent);
            return existing?.QuoteNumber;
        }
    }
}