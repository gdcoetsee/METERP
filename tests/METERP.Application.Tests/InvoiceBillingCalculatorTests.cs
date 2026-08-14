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

    [Theory]
    [InlineData(InvoiceDocumentType.Standard, InvoiceStatus.Sent, true)]
    [InlineData(InvoiceDocumentType.Deposit, InvoiceStatus.Paid, true)]
    [InlineData(InvoiceDocumentType.Partial, InvoiceStatus.PartiallyPaid, true)]
    [InlineData(InvoiceDocumentType.Final, InvoiceStatus.Overdue, true)]
    [InlineData(InvoiceDocumentType.Proforma, InvoiceStatus.Sent, false)]
    [InlineData(InvoiceDocumentType.CreditNote, InvoiceStatus.Sent, false)]
    [InlineData(InvoiceDocumentType.Standard, InvoiceStatus.Draft, false)]
    [InlineData(InvoiceDocumentType.Standard, InvoiceStatus.Cancelled, false)]
    public void CountsTowardJobBilled_ExcludesNonRevenueDocs(InvoiceDocumentType type, InvoiceStatus status, bool expected)
    {
        Assert.Equal(expected, InvoiceBillingCalculator.CountsTowardJobBilled(type, status));
    }

    [Fact]
    public void RequiresUnbilledCloseAcknowledgement_WhenMoreThanTenPercentAndAtLeast100()
    {
        Assert.True(InvoiceBillingCalculator.RequiresUnbilledCloseAcknowledgement(5000m, 0m));
        Assert.True(InvoiceBillingCalculator.RequiresUnbilledCloseAcknowledgement(5000m, 4000m)); // 1000 leftover
        Assert.False(InvoiceBillingCalculator.RequiresUnbilledCloseAcknowledgement(5000m, 4600m)); // 8% leftover
        Assert.False(InvoiceBillingCalculator.RequiresUnbilledCloseAcknowledgement(5000m, 5000m));
        Assert.False(InvoiceBillingCalculator.RequiresUnbilledCloseAcknowledgement(80m, 0m)); // under R100
    }
}