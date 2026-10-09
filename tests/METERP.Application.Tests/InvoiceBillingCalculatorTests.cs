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
    public void SplitVatInclusive_115At15Percent_Is100And15()
    {
        var split = InvoiceBillingCalculator.SplitVatInclusive(115m, 0.15m);
        Assert.Equal(100m, split.Subtotal);
        Assert.Equal(15m, split.Tax);
    }

    [Fact]
    public void SplitVatInclusive_230At15Percent_MatchesAccessImport()
    {
        var split = InvoiceBillingCalculator.SplitVatInclusive(230m);
        Assert.Equal(200m, split.Subtotal);
        Assert.Equal(30m, split.Tax);
    }

    [Fact]
    public void SplitVatInclusive_ZeroRateOrZeroTotal_KeepsTheGross()
    {
        var noRate = InvoiceBillingCalculator.SplitVatInclusive(115m, 0m);
        Assert.Equal(115m, noRate.Subtotal);
        Assert.Equal(0m, noRate.Tax);

        var zero = InvoiceBillingCalculator.SplitVatInclusive(0m, 0.15m);
        Assert.Equal(0m, zero.Subtotal);
        Assert.Equal(0m, zero.Tax);
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

    [Fact]
    public void DerivePaymentStatus_RestoresSentWhenAPaidInvoiceIsUnpaidAndNotOverdue()
    {
        var status = InvoiceBillingCalculator.DerivePaymentStatus(
            1000, 0, InvoiceStatus.Paid, DateTime.UtcNow.AddDays(10), DateTime.UtcNow);
        Assert.Equal(InvoiceStatus.Sent, status);
    }

    [Fact]
    public void DerivePaymentStatus_RestoresOverdueWhenAPaidInvoiceIsUnpaidAndPastDue()
    {
        var status = InvoiceBillingCalculator.DerivePaymentStatus(
            1000, 0, InvoiceStatus.Paid, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow);
        Assert.Equal(InvoiceStatus.Overdue, status);
    }

    [Fact]
    public void DerivePaymentStatus_RestoresPartiallyPaidWhenSomeReceiptRemains()
    {
        var status = InvoiceBillingCalculator.DerivePaymentStatus(
            1000, 250, InvoiceStatus.Paid, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow);
        Assert.Equal(InvoiceStatus.PartiallyPaid, status);
    }

    [Fact]
    public void ShouldClearDepositReceived_OnlyWhenThisDepositIsOpenAndNoOtherCountingDepositRemains()
    {
        Assert.True(InvoiceBillingCalculator.ShouldClearDepositReceived(
            true, InvoiceDocumentType.Deposit, 3000m, 0m, false));
        Assert.False(InvoiceBillingCalculator.ShouldClearDepositReceived(
            true, InvoiceDocumentType.Deposit, 3000m, 0m, true));
        Assert.False(InvoiceBillingCalculator.ShouldClearDepositReceived(
            true, InvoiceDocumentType.Deposit, 3000m, 3000m, false));
        Assert.False(InvoiceBillingCalculator.ShouldClearDepositReceived(
            true, InvoiceDocumentType.Standard, 3000m, 0m, false));
        Assert.False(InvoiceBillingCalculator.ShouldClearDepositReceived(
            false, InvoiceDocumentType.Deposit, 3000m, 0m, false));
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
    public void ShowDepositCollectionBanner_SuppressesWhenBilledCoversThresholdOrCompletedWithBilled()
    {
        // FT16010 shape: in progress, quoted 61840, 30% deposit, nothing linked — CTA stays.
        Assert.True(InvoiceBillingCalculator.ShowDepositCollectionBanner(
            JobStatus.InProgress, 30m, false, 61840m, 0m));

        var threshold = InvoiceBillingCalculator.CalculateDepositThreshold(61840m, 30m);
        Assert.Equal(18552m, threshold);
        Assert.False(InvoiceBillingCalculator.ShowDepositCollectionBanner(
            JobStatus.InProgress, 30m, false, 61840m, threshold));

        Assert.False(InvoiceBillingCalculator.ShowDepositCollectionBanner(
            JobStatus.Completed, 30m, false, 61840m, 1m));
        Assert.False(InvoiceBillingCalculator.ShowDepositCollectionBanner(
            JobStatus.Closed, 30m, false, 61840m, 100m));
        // Helm: completed/closed stay off the mobilisation CTA even with nothing billed.
        Assert.False(InvoiceBillingCalculator.ShowDepositCollectionBanner(
            JobStatus.Completed, 30m, false, 61840m, 0m));
        Assert.False(InvoiceBillingCalculator.ShowDepositCollectionBanner(
            JobStatus.InProgress, 30m, true, 61840m, 0m));
    }

    [Fact]
    public void ShouldSoftSyncDepositReceived_WhenDepositInvoiceOrBilledCoversThreshold()
    {
        Assert.True(InvoiceBillingCalculator.ShouldSoftSyncDepositReceived(false, 5000m, 30m, 100m, true));
        Assert.True(InvoiceBillingCalculator.ShouldSoftSyncDepositReceived(false, 5000m, 30m, 1500m, false));
        Assert.False(InvoiceBillingCalculator.ShouldSoftSyncDepositReceived(false, 5000m, 30m, 1499.99m, false));
        Assert.False(InvoiceBillingCalculator.ShouldSoftSyncDepositReceived(true, 5000m, 30m, 0m, true));
        Assert.False(InvoiceBillingCalculator.ShouldSoftSyncDepositReceived(false, 5000m, 0m, 5000m, true));
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