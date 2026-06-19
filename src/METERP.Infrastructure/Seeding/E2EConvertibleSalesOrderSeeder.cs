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

        var existing = await salesOrderService.GetAllAsync(pageSize: 500, ct: ct);
        foreach (var stale in existing.Where(so =>
                     so.Notes != null
                     && so.Notes.Contains(DemoNotesMarker, StringComparison.OrdinalIgnoreCase)
                     && so.Status == SalesOrderStatus.Confirmed))
        {
            await salesOrderService.DeleteAsync(stale.Id, ct);
        }

        var customer = (await customerService.GetAllAsync(ct: ct))
            .FirstOrDefault(c => c.Name.Contains("Hospital", StringComparison.OrdinalIgnoreCase))
            ?? (await customerService.GetAllAsync(ct: ct)).FirstOrDefault();
        if (customer == null)
            return null;

        var quoteId = await quoteService.CreateAsync(new Quote
        {
            CustomerId = customer.Id,
            QuoteDate = DateTime.UtcNow,
            ValidUntil = DateTime.UtcNow.AddDays(30),
            Status = QuoteStatus.Accepted,
            TaxRate = 0.15m,
            Notes = DemoNotesMarker
        }, ct);

        var soId = await salesOrderService.CreateAsync(new SalesOrder
        {
            QuoteId = quoteId,
            CustomerId = customer.Id,
            SoDate = DateTime.UtcNow,
            DeliveryDate = DateTime.UtcNow.AddDays(7),
            Status = SalesOrderStatus.Confirmed,
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

        return (await salesOrderService.GetByIdAsync(soId, ct))?.SoNumber;
    }
}