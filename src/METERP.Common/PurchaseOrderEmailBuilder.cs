using System.Globalization;
using System.Text;

namespace METERP.Common;

public static class PurchaseOrderEmailBuilder
{
    public static string BuildHtml(
        string poNumber,
        string supplierName,
        decimal total,
        DateTime? expectedDate,
        IEnumerable<(string Description, decimal Quantity, string Unit, decimal UnitPrice)> lines)
    {
        var sb = new StringBuilder();
        sb.Append("<h2>Purchase Order — ").Append(Escape(poNumber)).Append("</h2>");
        sb.Append("<p>Supplier: <strong>").Append(Escape(supplierName)).Append("</strong></p>");
        sb.Append("<p>Total: <strong>R ").Append(total.ToString("N2", CultureInfo.InvariantCulture)).Append("</strong></p>");
        if (expectedDate.HasValue)
            sb.Append("<p>Expected delivery: ").Append(expectedDate.Value.ToString("yyyy-MM-dd")).Append("</p>");
        sb.Append("<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\"><thead><tr><th>Description</th><th>Qty</th><th>Unit</th><th>Price</th></tr></thead><tbody>");
        foreach (var line in lines)
        {
            sb.Append("<tr><td>").Append(Escape(line.Description))
                .Append("</td><td>").Append(line.Quantity.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(Escape(line.Unit))
                .Append("</td><td>R ").Append(line.UnitPrice.ToString("N2", CultureInfo.InvariantCulture))
                .Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
        sb.Append("<p><em>Sent via METERP — paperless procurement.</em></p>");
        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}