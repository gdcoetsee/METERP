namespace METERP.Application.Models;

public sealed record ConvertibleDocumentRow(
    Guid Id,
    string Kind,
    string Number,
    string CustomerName,
    decimal Total,
    string Href);
