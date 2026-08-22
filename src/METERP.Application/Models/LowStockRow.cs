namespace METERP.Application.Models;

public sealed record LowStockRow(
    Guid Id,
    string Sku,
    string Name,
    decimal OnHand,
    decimal ReorderLevel,
    string Href);
