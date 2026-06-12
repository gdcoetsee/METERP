using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class CashflowReportServiceTests
{
    private (AppDbContext Db, CashflowReportService Service, Guid TenantId) CreateHarness()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(u => u.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        return (db, new CashflowReportService(db), tenantId);
    }

    [Fact]
    public async Task GetCashflowForecastAsync_ComputesNetFromReceivablesPipelineAndOpenPos()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            var customerId = Guid.NewGuid();

            db.Set<Invoice>().Add(new Invoice
            {
                TenantId = tenantId,
                CustomerId = customerId,
                InvoiceNumber = "INV-1",
                Status = InvoiceStatus.Sent,
                Total = 120_000m
            });
            db.Set<Quote>().Add(new Quote
            {
                TenantId = tenantId,
                CustomerId = customerId,
                QuoteNumber = "Q-1",
                Status = QuoteStatus.Accepted,
                Total = 80_000m
            });
            db.Set<PurchaseOrder>().Add(new PurchaseOrder
            {
                TenantId = tenantId,
                SupplierId = Guid.NewGuid(),
                PoNumber = "PO-1",
                Status = PurchaseOrderStatus.Sent,
                Total = 55_000m
            });
            await db.SaveChangesAsync();

            var summary = await service.GetCashflowForecastAsync();

            Assert.Equal(120_000m, summary.ReceivableInflow);
            Assert.Equal(1, summary.ReceivableInvoiceCount);
            Assert.Equal(80_000m, summary.PipelineInflow);
            Assert.Equal(1, summary.PipelineQuoteCount);
            Assert.Equal(55_000m, summary.CommittedOutflow);
            Assert.Equal(145_000m, summary.NetForecastInflow);
            Assert.Equal(78.4m, summary.InflowSharePercent);
        }
    }

    [Fact]
    public async Task GetCashflowForecastAsync_ExcludesPaidInvoicesAndReceivedPos()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            var customerId = Guid.NewGuid();

            db.Set<Invoice>().AddRange(
                new Invoice
                {
                    TenantId = tenantId,
                    CustomerId = customerId,
                    Status = InvoiceStatus.Paid,
                    Total = 50_000m
                },
                new Invoice
                {
                    TenantId = tenantId,
                    CustomerId = customerId,
                    Status = InvoiceStatus.Draft,
                    Total = 20_000m
                });
            db.Set<PurchaseOrder>().Add(new PurchaseOrder
            {
                TenantId = tenantId,
                SupplierId = Guid.NewGuid(),
                Status = PurchaseOrderStatus.Received,
                Total = 30_000m
            });
            await db.SaveChangesAsync();

            var summary = await service.GetCashflowForecastAsync();

            Assert.Equal(0m, summary.ReceivableInflow);
            Assert.Equal(0m, summary.CommittedOutflow);
            Assert.Equal(0m, summary.NetForecastInflow);
        }
    }
}