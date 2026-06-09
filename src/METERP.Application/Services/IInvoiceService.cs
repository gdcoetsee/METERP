using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Service for customer invoicing, completing the Quote -> Job -> Invoice flow.
/// </summary>
public interface IInvoiceService
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(Invoice invoice, CancellationToken ct = default);
    Task UpdateAsync(Invoice invoice, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // Line management (consistent with Quotes)
    Task<Guid> AddLineAsync(InvoiceLine line, CancellationToken ct = default);
    Task UpdateLineAsync(InvoiceLine line, CancellationToken ct = default);
    Task DeleteLineAsync(Guid lineId, CancellationToken ct = default);

    /// <summary>
    /// Creates an invoice from a completed job. Snapshots totals and (if available) line items from the originating quote.
    /// </summary>
    Task<Invoice> CreateFromJobAsync(Guid jobId, CancellationToken ct = default);

    Task UpdateStatusAsync(Guid invoiceId, InvoiceStatus newStatus, CancellationToken ct = default);
}
