using METERP.Domain;

namespace METERP.Application.Models;

/// <summary>
/// One open purchase-order line still to receive.
/// Outstanding is ordered minus received, and never goes below zero.
/// </summary>
public sealed record PurchaseOrderOutstandingRow(
    Guid PurchaseOrderId,
    Guid LineId,
    string PoNumber,
    string SupplierName,
    string Description,
    PurchaseOrderStatus Status,
    decimal Ordered,
    decimal Received,
    decimal Outstanding);
