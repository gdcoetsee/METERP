using METERP.Domain;

namespace METERP.Application.Services;

public sealed record CustomerPortalDashboard(
    string CustomerName,
    int OpenQuoteCount,
    int OpenInvoiceCount,
    decimal BalanceDue,
    IReadOnlyList<Quote> Quotes,
    IReadOnlyList<Invoice> Invoices);

public interface ICustomerPortalService
{
    Task<CustomerPortalDashboard> GetDashboardAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>Customer accepts a sent quote. Office is notified to convert to a job.</summary>
    Task AcceptQuoteAsync(Guid customerId, Guid quoteId, CancellationToken ct = default);

    /// <summary>
    /// Customer reports an EFT/payment. Does not mark the invoice paid — office records the receipt.
    /// </summary>
    Task ReportPaymentAsync(
        Guid customerId,
        Guid invoiceId,
        decimal amount,
        string? reference,
        CancellationToken ct = default);
}
