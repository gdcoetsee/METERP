namespace METERP.Domain;

/// <summary>
/// Pure expiry alert threshold logic for company docs and certifications.
/// </summary>
public static class ComplianceExpiryCalculator
{
    public static readonly int[] AlertThresholdsDays = [30, 14, 7];

    public static int? GetDaysUntilExpiry(DateTime? expiryDate, DateTime asOfUtc)
    {
        if (expiryDate is null)
            return null;

        return (expiryDate.Value.Date - asOfUtc.Date).Days;
    }

    /// <summary>
    /// Returns the tightest threshold (30, 14, 7) that should fire now, or null if none.
    /// </summary>
    public static int? GetAlertThresholdToSend(int daysRemaining, int? lastAlertDaysRemaining)
    {
        foreach (var threshold in AlertThresholdsDays)
        {
            if (daysRemaining > threshold)
                continue;

            if (lastAlertDaysRemaining is null || lastAlertDaysRemaining > threshold)
                return threshold;
        }

        return null;
    }
}