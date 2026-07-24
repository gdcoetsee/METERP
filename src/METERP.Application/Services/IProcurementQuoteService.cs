using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>Input for a line-level supplier RFQ price.</summary>
public sealed record ProcurementQuoteLineInput(
    Guid? StockRequisitionLineId,
    string Description,
    decimal Quantity,
    decimal UnitPrice);

/// <summary>Multi-supplier RFQ for requisitions awaiting procurement (header and optional line prices).</summary>
public interface IProcurementQuoteService
{
    Task<IReadOnlyList<ProcurementSupplierQuote>> GetForRequisitionAsync(Guid requisitionId, CancellationToken ct = default);

    /// <summary>
    /// Add a supplier quote. When <paramref name="lines"/> is provided and non-empty,
    /// <paramref name="quotedTotal"/> is derived from line totals (sum of qty × unit price).
    /// </summary>
    Task<Guid> AddQuoteAsync(
        Guid requisitionId,
        Guid supplierId,
        decimal quotedTotal,
        string? notes = null,
        IReadOnlyList<ProcurementQuoteLineInput>? lines = null,
        CancellationToken ct = default);

    Task<bool> SelectQuoteAsync(Guid quoteId, Guid selectedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Creates PO from the selected supplier quote for the requisition.
    /// Applies line unit prices from the quote when line detail is present.
    /// </summary>
    Task<Guid> CreatePoFromSelectedQuoteAsync(Guid requisitionId, CancellationToken ct = default);
}
