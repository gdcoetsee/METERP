using METERP.Domain;

namespace METERP.Application.Models;

/// <summary>
/// One page of goods-receipt vouchers. The register must not render the full GRV history.
/// </summary>
public sealed class GrvPage
{
    public IReadOnlyList<GoodsReceiptVoucher> Items { get; init; } = Array.Empty<GoodsReceiptVoucher>();

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public int TotalCount { get; init; }

    public int PageCount =>
        TotalCount <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize));
}
