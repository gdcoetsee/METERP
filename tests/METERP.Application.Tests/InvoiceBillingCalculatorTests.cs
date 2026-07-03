using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class InvoiceBillingCalculatorTests
{
    [Theory]
    [InlineData(10000, 10, 1000)]
    [InlineData(0, 10, 0)]
    [InlineData(5000, 0, 0)]
    public void CalculateRetentionAmount_ReturnsExpected(decimal subtotal, decimal percent, decimal expected)
    {
        Assert.Equal(expected, InvoiceBillingCalculator.CalculateRetentionAmount(subtotal, percent));
    }

    [Theory]
    [InlineData(11500, 5000, 6500)]
    [InlineData(1000, 1200, 0)]
    public void CalculateBalanceDue_ClampsAtZero(decimal total, decimal paid, decimal expected)
    {
        Assert.Equal(expected, InvoiceBillingCalculator.CalculateBalanceDue(total, paid));
    }

    [Fact]
    public void DerivePaymentStatus_MarksPaidWhenFullyPaid()
    {
        var status = InvoiceBillingCalculator.DerivePaymentStatus(
            1000, 1000, InvoiceStatus.Sent, DateTime.UtcNow.AddDays(30), DateTime.UtcNow);
        Assert.Equal(InvoiceStatus.Paid, status);
    }

    [Fact]
    public void DerivePaymentStatus_MarksPartiallyPaid()
    {
        var status = InvoiceBillingCalculator.DerivePaymentStatus(
            1000, 400, InvoiceStatus.Sent, DateTime.UtcNow.AddDays(30), DateTime.UtcNow);
        Assert.Equal(InvoiceStatus.PartiallyPaid, status);
    }

    [Fact]
    public void DerivePaymentStatus_MarksOverdueWhenPastDue()
    {
        var status = InvoiceBillingCalculator.DerivePaymentStatus(
            1000, 0, InvoiceStatus.Sent, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow);
        Assert.Equal(InvoiceStatus.Overdue, status);
    }

    [Theory]
    [InlineData(0, "Current")]
    [InlineData(15, "1-30")]
    [InlineData(45, "31-60")]
    [InlineData(120, "90+")]
    public void GetAgingBucket_MapsDays(int days, string bucket)
    {
        Assert.Equal(bucket, InvoiceBillingCalculator.GetAgingBucket(days));
    }
}