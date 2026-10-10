namespace METERP.Domain;

/// <summary>
/// Office payment terms. Null, zero, or a missing value means 30 days.
/// Values outside 0–180 are rejected when the customer is saved.
/// </summary>
public static class CustomerPaymentTerms
{
    public const int DefaultDays = 30;
    public const int MinDays = 0;
    public const int MaxDays = 180;

    public static int ResolveDays(int? termsDays) =>
        termsDays is null or 0 ? DefaultDays : termsDays.Value;

    public static void EnsureAllowed(int? termsDays)
    {
        if (termsDays is < MinDays or > MaxDays)
            throw new InvalidOperationException(
                $"Payment terms must be between {MinDays} and {MaxDays} days.");
    }

    public static DateTime DueDateFrom(DateTime invoiceDate, int? termsDays) =>
        invoiceDate.AddDays(ResolveDays(termsDays));
}
