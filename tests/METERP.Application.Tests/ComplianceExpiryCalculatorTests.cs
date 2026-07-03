using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class ComplianceExpiryCalculatorTests
{
    [Theory]
    [InlineData(29, null, 30)]
    [InlineData(14, 30, 14)]
    [InlineData(7, 14, 7)]
    [InlineData(5, 7, null)]
    public void GetAlertThresholdToSend_FiresAtConfiguredSteps(int daysRemaining, int? lastAlert, int? expected)
    {
        Assert.Equal(expected, ComplianceExpiryCalculator.GetAlertThresholdToSend(daysRemaining, lastAlert));
    }
}