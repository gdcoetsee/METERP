using METERP.Application.Models;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class ExecutiveDashboardServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_AggregatesPendingQueuesAndReadyToInvoice()
    {
        var quotes = new Mock<IQuoteService>();
        quotes.Setup(s => s.GetPendingExecutiveApprovalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Quote> { new() { QuoteNumber = "Q-1" } });

        var requisitions = new Mock<IStockRequisitionService>();
        requisitions.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockRequisition>());

        var leave = new Mock<ILeaveService>();
        leave.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LeaveRequest> { new() });

        var field = new Mock<IFieldReportService>();
        field.Setup(s => s.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FieldReport>());

        var notifications = new Mock<ITenantNotificationService>();
        notifications.Setup(s => s.GetUnreadCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var jobs = new Mock<IJobService>();
        jobs.Setup(s => s.GetAllAsync(null, 1, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Job>
            {
                new()
                {
                    JobNumber = "J-1",
                    QuotedTotal = 10000m,
                    SignOffStatus = JobSignOffStatus.SignedOff,
                    Status = JobStatus.Completed
                }
            });

        var invoices = new Mock<IInvoiceService>();
        invoices.Setup(s => s.GetAgedDebtorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgedDebtorRow>
            {
                new(Guid.NewGuid(), "INV-1", "Acme", DateTime.UtcNow.AddDays(-10), 5000m, 0m, 5000m, 10, "1-30")
            });

        var inventory = new Mock<IInventoryService>();
        inventory.Setup(s => s.GetAllItemsAsync(
                It.IsAny<string?>(),
                true,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InventoryItem> { new() { Sku = "LOW-1", Name = "Cable" } });

        var service = new ExecutiveDashboardService(
            quotes.Object,
            requisitions.Object,
            leave.Object,
            field.Object,
            notifications.Object,
            jobs.Object,
            invoices.Object,
            inventory.Object);

        var summary = await service.GetSummaryAsync();

        Assert.Equal(2, summary.PendingApprovals);
        Assert.Equal(1, summary.PendingQuotes);
        Assert.Equal(1, summary.PendingLeave);
        Assert.Equal(3, summary.UnreadNotifications);
        Assert.Equal(1, summary.ReadyToInvoiceJobs);
        Assert.Equal(10000m, summary.ReadyToInvoiceValue);
        Assert.Equal(5000m, summary.AgedDebtorsTotal);
        Assert.Equal(1, summary.LowStockItems);
    }
}