using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>Multi-supplier RFQ lite for requisitions awaiting procurement.</summary>
public interface IProcurementQuoteService
{
    Task<IReadOnlyList<ProcurementSupplierQuote>> GetForRequisitionAsync(Guid requisitionId, CancellationToken ct = default);

    Task<Guid> AddQuoteAsync(
        Guid requisitionId,
        Guid supplierId,
        decimal quotedTotal,
        string? notes = null,
        CancellationToken ct = default);

    Task<bool> SelectQuoteAsync(Guid quoteId, Guid selectedByUserId, CancellationToken ct = default);

    /// <summary>Creates PO from the selected supplier quote for the requisition.</summary>
    Task<Guid> CreatePoFromSelectedQuoteAsync(Guid requisitionId, CancellationToken ct = default);
}
