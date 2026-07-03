namespace METERP.Domain;

/// <summary>
/// Pure leave accrual math for tests and services.
/// </summary>
public static class LeaveAccrualCalculator
{
    public static decimal CalculateAccruedDays(decimal annualEntitlementDays, DateTime? hireDate, DateTime asOfUtc)
    {
        if (annualEntitlementDays <= 0 || hireDate is null)
            return 0;

        var hire = hireDate.Value.Date;
        var asOf = asOfUtc.Date;
        if (asOf < hire)
            return 0;

        var months =
            (asOf.Year - hire.Year) * 12 +
            (asOf.Month - hire.Month) +
            (asOf.Day >= hire.Day ? 0 : -1);

        if (months < 0)
            return 0;

        return Math.Round(annualEntitlementDays / 12m * months, 2);
    }

    public static decimal CalculateBusinessDays(DateTime start, DateTime end)
    {
        if (end < start)
            return 0;

        decimal days = 0;
        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                days++;
        }

        return days;
    }
}