using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class LeaveAccrualCalculatorTests
{
    [Fact]
    public void CalculateAccruedDays_ProRatesMonthlyFromHireDate()
    {
        var hire = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var asOf = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc);

        var accrued = LeaveAccrualCalculator.CalculateAccruedDays(15m, hire, asOf);

        Assert.Equal(3.75m, accrued);
    }

    [Fact]
    public void CalculateBusinessDays_ExcludesWeekends()
    {
        var start = new DateTime(2026, 7, 6); // Monday
        var end = new DateTime(2026, 7, 10);   // Friday

        Assert.Equal(5m, LeaveAccrualCalculator.CalculateBusinessDays(start, end));
    }
}