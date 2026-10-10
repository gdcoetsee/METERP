namespace METERP.Domain;

/// <summary>
/// Customer's own order number on a job (Access CustomerOrderNo).
/// Blank is stored as null. It never changes quote or invoice totals.
/// </summary>
public static class JobCustomerOrder
{
    public const int MaxLength = 100;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
            throw new InvalidOperationException(
                $"Customer order number cannot exceed {MaxLength} characters.");

        return trimmed;
    }
}
