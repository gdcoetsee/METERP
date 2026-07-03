using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class StockAvailabilityCalculatorTests
{
    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 3, 7)]
    [InlineData(5, 8, 0)]
    [InlineData(0, 0, 0)]
    public void GetAvailableQuantity_ReturnsOnHandMinusReserved(decimal onHand, decimal reserved, decimal expected)
    {
        Assert.Equal(expected, StockAvailabilityCalculator.GetAvailableQuantity(onHand, reserved));
    }

    [Theory]
    [InlineData(5, 10, 5)]
    [InlineData(15, 10, 10)]
    [InlineData(5, 0, 0)]
    [InlineData(0, 5, 0)]
    public void CalculateReservation_CapsAtAvailable(decimal requested, decimal available, decimal expected)
    {
        Assert.Equal(expected, StockAvailabilityCalculator.CalculateReservation(requested, available));
    }
}