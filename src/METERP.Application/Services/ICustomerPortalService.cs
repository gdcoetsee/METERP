using METERP.Domain;

namespace METERP.Application.Services;

public static class CustomerPortalMessages
{
    public const string Unlinked = "Portal access not linked to a customer. Contact MET office.";

    /// <summary>Locked or disabled portal identity. Do not describe it as an unlinked customer.</summary>
    public const string Unavailable = "This portal login is not available. Contact MET office.";
}

public sealed record CustomerPortalDashboard(
    string CustomerName,
    int OpenQuoteCount,
    int OpenInvoiceCount,
    decimal BalanceDue,
    IReadOnlyList<Quote> Quotes,
    IReadOnlyList<Invoice> Invoices,
    bool IsUnlinked = false);

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
