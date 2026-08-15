namespace METERP.Application.Models;

public sealed record ConvertibleDocumentRow(
    Guid Id,
    string Kind,
    string Number,
    string CustomerName,
    decimal Total,
    string Href)
{
    public bool CanConvertDirectly =>
        string.Equals(Kind, "Quote", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Kind, "Sales order", StringComparison.OrdinalIgnoreCase);
}
