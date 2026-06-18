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

        var demoQuotes = await quoteService.GetAllAsync(pageSize: 500, ct: ct);
        foreach (var stale in demoQuotes.Where(q =>
                     q.Notes != null
                     && q.Notes.Contains(DemoNotesMarker, StringComparison.OrdinalIgnoreCase)
                     && q.Status is QuoteStatus.Draft or QuoteStatus.Sent))
        {
            await quoteService.DeleteAsync(stale.Id, ct);
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
                Notes = DemoNotesMarker
            }, ct);

            await quoteService.AddLineAsync(new QuoteLine
            {
                QuoteId = quoteId,
                Description = "Panel upgrade labour (8 hours)",
                Quantity = 8,
                UnitPrice = 195m,
                LineType = "Labour",
                Unit = "hr"
            }, ct);

            await quoteService.AddLineAsync(new QuoteLine
            {
                QuoteId = quoteId,
                Description = "Travel & site transport (explicit contractor cost)",
                Quantity = 1,
                UnitPrice = 620m,
                LineType = "Travel",
                Unit = "lot"
            }, ct);

            return (await quoteService.GetByIdAsync(quoteId, ct))?.QuoteNumber;
        }
        catch (QuotaExceededException)
        {
            // Re-use an existing convertible quote when the demo tenant is at its monthly cap.
            var existing = (await quoteService.GetAllAsync("E2E convertible", pageSize: 5, ct: ct))
                .FirstOrDefault(q => q.Status is QuoteStatus.Draft or QuoteStatus.Sent);
            return existing?.QuoteNumber;
        }
    }
}