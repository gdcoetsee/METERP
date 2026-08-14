using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Models;
using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;
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
        quotes.Setup(s => s.GetUnconvertedWonQuotesAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConvertibleDocumentRow>());

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
        jobs.Setup(s => s.GetReadyToInvoiceQueueAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReadyToInvoiceJobRow>
            {
                new(Guid.NewGuid(), "J-1", "Ready job", "Acme", 10000m, 0m, 10000m)
            });
        jobs.Setup(s => s.GetDepositDueQueueAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReadyToInvoiceJobRow>
            {
                new(Guid.NewGuid(), "J-DEP", "Mobilise", "Acme", 10000m, 0m, 3000m, "Deposit")
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

        var salesOrders = new Mock<ISalesOrderService>();
        salesOrders.Setup(s => s.GetUnconvertedConfirmedAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConvertibleDocumentRow>());

        var service = new ExecutiveDashboardService(
            quotes.Object,
            requisitions.Object,
            leave.Object,
            field.Object,
            notifications.Object,
            jobs.Object,
            invoices.Object,
            inventory.Object,
            salesOrders.Object);

        var summary = await service.GetSummaryAsync();

        Assert.Equal(2, summary.PendingApprovals);
        Assert.Equal(1, summary.PendingQuotes);
        Assert.Equal(1, summary.PendingLeave);
        Assert.Equal(3, summary.UnreadNotifications);
        Assert.Equal(1, summary.ReadyToInvoiceJobs);
        Assert.Equal(10000m, summary.ReadyToInvoiceValue);
        Assert.Equal(1, summary.DepositDueJobs);
        Assert.Equal(3000m, summary.DepositDueValue);
        Assert.Equal(5000m, summary.AgedDebtorsTotal);
        Assert.Equal(1, summary.LowStockItems);
    }

    [Fact]
    public async Task GetSummaryAsync_ReflectsReadyToInvoice_AfterJobSignOffInvalidatesCache()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);

        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 120);
        var provider = services.BuildServiceProvider();
        var cache = new TenantDistributedCacheService(
            provider.GetRequiredService<IDistributedCache>(),
            tenantProvider.Object,
            provider.GetRequiredService<IOptions<CacheOptions>>());

        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Exec Co" });
        await db.SaveChangesAsync();

        var jobService = new JobService(db, cache: cache);
        var jobId = await jobService.CreateAsync(new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = "J-EXEC-001",
            Title = "Sign-off job",
            QuotedTotal = 15000m,
            Status = JobStatus.Completed
        });

        var dashboard = CreateDashboardWithRealJobs(jobService);

        Assert.Equal(0, (await dashboard.GetSummaryAsync()).ReadyToInvoiceJobs);

        await jobService.SignOffAsync(jobId, Guid.NewGuid());

        var summary = await dashboard.GetSummaryAsync();
        Assert.Equal(1, summary.ReadyToInvoiceJobs);
        Assert.Equal(15000m, summary.ReadyToInvoiceValue);
    }

    private static ExecutiveDashboardService CreateDashboardWithRealJobs(IJobService jobService)
    {
        var quotes = new Mock<IQuoteService>();
        quotes.Setup(s => s.GetPendingExecutiveApprovalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Quote>());
        quotes.Setup(s => s.GetUnconvertedWonQuotesAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConvertibleDocumentRow>());

        var requisitions = new Mock<IStockRequisitionService>();
        requisitions.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockRequisition>());

        var leave = new Mock<ILeaveService>();
        leave.Setup(s => s.GetPendingApprovalsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequest>());

        var field = new Mock<IFieldReportService>();
        field.Setup(s => s.GetPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FieldReport>());

        var notifications = new Mock<ITenantNotificationService>();
        notifications.Setup(s => s.GetUnreadCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var invoices = new Mock<IInvoiceService>();
        invoices.Setup(s => s.GetAgedDebtorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AgedDebtorRow>());

        var inventory = new Mock<IInventoryService>();
        inventory.Setup(s => s.GetAllItemsAsync(
                It.IsAny<string?>(),
                true,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InventoryItem>());

        var salesOrders = new Mock<ISalesOrderService>();
        salesOrders.Setup(s => s.GetUnconvertedConfirmedAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConvertibleDocumentRow>());

        return new ExecutiveDashboardService(
            quotes.Object,
            requisitions.Object,
            leave.Object,
            field.Object,
            notifications.Object,
            jobService,
            invoices.Object,
            inventory.Object,
            salesOrders.Object);
    }
}