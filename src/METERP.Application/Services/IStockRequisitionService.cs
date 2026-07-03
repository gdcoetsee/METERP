using METERP.Domain;

namespace METERP.Application.Services;

public interface IStockRequisitionService
{
    Task<StockRequisition?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<StockRequisition>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<IReadOnlyList<StockRequisition>> GetPendingApprovalsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StockRequisition>> GetByJobIdAsync(Guid jobId, CancellationToken ct = default);

    Task<Guid> SubmitAsync(StockRequisition requisition, CancellationToken ct = default);

    Task<bool> ApproveManagerAsync(Guid requisitionId, Guid approverUserId, CancellationToken ct = default);

    Task<bool> ApproveExecutiveAsync(Guid requisitionId, Guid approverUserId, CancellationToken ct = default);

    Task<bool> RejectAsync(Guid requisitionId, Guid approverUserId, string reason, CancellationToken ct = default);

    Task<bool> IssueAsync(Guid requisitionId, Guid issuedByUserId, CancellationToken ct = default);

    Task<bool> FulfillAfterPoReceiptAsync(Guid purchaseOrderId, CancellationToken ct = default);
}