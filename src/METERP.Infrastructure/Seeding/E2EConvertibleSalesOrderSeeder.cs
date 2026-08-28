using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Idempotent Confirmed sales order with travel line for SO→job E2E.
/// Soft-deletes prior convertible-demo SOs and creates a fresh one.
/// </summary>
public static class E2EConvertibleSalesOrderSeeder
{
    public const string DemoNotesMarker = "E2E convertible sales order";

    public static async Task<string?> EnsureConfirmedConvertibleSalesOrderAsync(
        ISalesOrderService salesOrderService,
        IQuoteService quoteService,
        ICustomerService customerService,
        ITenantProvider tenantProvider,
        Guid tenantId,
        CancellationToken ct = default)
    {
        tenantProvider.SetTenantId(tenantId);

        var existing = (await salesOrderService.GetAllAsync(pageSize: 500, ct: ct))
            .Where(so => so.Notes != null
                         && so.Notes.Contains(DemoNotesMarker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var unconvertedIds = (await salesOrderService.GetUnconvertedConfirmedAsync(50, ct))
            .Select(r => r.Id)
            .ToHashSet();
        foreach (var candidate in existing.Where(so => unconvertedIds.Contains(so.Id)))
        {
            var full = await salesOrderService.GetByIdAsync(candidate.Id, ct);
            if (full?.Lines.Any(l => !l.IsDeleted) == true)
                return full.SoNumber;
        }

        foreach (var stale in existing.Where(so => so.Status is SalesOrderStatus.Draft or SalesOrderStatus.Cancelled))
        {
            try
            {
                await salesOrderService.DeleteAsync(stale.Id, ct);
            }
            catch (InvalidOperationException)
            {
                // Linked orders stay — never crash host or E2E reset.
            }
        }

        var customers = await customerService.GetAllAsync(pageSize: 200, ct: ct);
        var customer = customers.FirstOrDefault(c =>
                           c.Name.Contains("Hospital", StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrWhiteSpace(c.Email))
                       ?? customers.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Email));
        if (customer == null)
            return null;

        Guid quoteId;
        try
        {
            quoteId = await quoteService.CreateAsync(new Quote
            {
                CustomerId = customer.Id,
                QuoteDate = DateTime.UtcNow,
                ValidUntil = DateTime.UtcNow.AddDays(30),
                Status = QuoteStatus.Accepted,
                TaxRate = 0.15m,
                Notes = DemoNotesMarker
            }, ct);
        }
        catch (QuotaExceededException)
        {
            var fallback = existing.FirstOrDefault(so => so.Status == SalesOrderStatus.Confirmed);
            return fallback?.SoNumber;
        }

        var soId = await salesOrderService.CreateAsync(new SalesOrder
        {
            QuoteId = quoteId,
            CustomerId = customer.Id,
            SoDate = DateTime.UtcNow,
            DeliveryDate = DateTime.UtcNow.AddDays(7),
            Status = SalesOrderStatus.Draft,
            TaxRate = 0.15m,
            Notes = DemoNotesMarker
        }, ct);

        await salesOrderService.AddLineAsync(new SalesOrderLine
        {
            SalesOrderId = soId,
            Description = "Switchgear install package",
            Quantity = 1,
            UnitPrice = 4800m,
            LineType = "Labour",
            Unit = "lot"
        }, ct);

        await salesOrderService.AddLineAsync(new SalesOrderLine
        {
            SalesOrderId = soId,
            Description = "Travel & mobilization (explicit contractor cost)",
            Quantity = 1,
            UnitPrice = 720m,
            LineType = "Travel",
            Unit = "lot"
        }, ct);

        await salesOrderService.UpdateStatusAsync(soId, SalesOrderStatus.Confirmed, ct);
        return (await salesOrderService.GetByIdAsync(soId, ct))?.SoNumber;
    }
}