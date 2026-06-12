using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class SalesOrderServiceTests
{
    private (AppDbContext Db, SalesOrderService Service) CreateServices(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var jobService = new JobService(db);
        var service = new SalesOrderService(db, jobService);
        return (db, service);
    }

    private static async Task<(Guid CustomerId, Guid QuoteId)> SeedCustomerAndQuoteAsync(AppDbContext db, Guid tenantId)
    {
        var customerId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
        db.Set<Quote>().Add(new Quote { Id = quoteId, TenantId = tenantId, CustomerId = customerId, QuoteNumber = "Q-TEST" });
        await db.SaveChangesAsync();
        return (customerId, quoteId);
    }

    [Fact]
    public async Task CreateAsync_AssignsSoNumber_AndRecalculatesTotals()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);

            var so = new SalesOrder
            {
                QuoteId = quoteId,
                CustomerId = customerId,
                TaxRate = 0.15m,
                Lines =
                {
                    new SalesOrderLine { Description = "Panel install", Quantity = 1, UnitPrice = 10000m }
                }
            };

            var id = await service.CreateAsync(so);
            var loaded = await service.GetByIdAsync(id);

            Assert.NotNull(loaded);
            Assert.StartsWith("SO-", loaded.SoNumber);
            Assert.Equal(10000m, loaded.Subtotal);
            Assert.Equal(1500m, loaded.Tax);
            Assert.Equal(11500m, loaded.Total);
        }
    }

    [Fact]
    public async Task AddLineAsync_RecalculatesParentTotals()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            var soId = await service.CreateAsync(new SalesOrder { QuoteId = quoteId, CustomerId = customerId, TaxRate = 0.15m });

            await service.AddLineAsync(new SalesOrderLine
            {
                SalesOrderId = soId,
                Description = "Travel allowance",
                Quantity = 1,
                UnitPrice = 850m,
                LineType = "Other"
            });

            var loaded = await service.GetByIdAsync(soId);
            Assert.Equal(850m, loaded!.Subtotal);
            Assert.Equal(127.5m, loaded.Tax);
        }
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesSoAndLines()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            var so = new SalesOrder
            {
                QuoteId = quoteId,
                CustomerId = customerId,
                Lines = { new SalesOrderLine { Description = "Line 1", Quantity = 1, UnitPrice = 500m } }
            };
            var soId = await service.CreateAsync(so);
            var lineId = so.Lines.First().Id;

            await service.DeleteAsync(soId);

            Assert.Null(await service.GetByIdAsync(soId));
            var deletedLine = await db.Set<SalesOrderLine>().IgnoreQueryFilters().FirstAsync(l => l.Id == lineId);
            Assert.True(deletedLine.IsDeleted);
        }
    }

    [Fact]
    public async Task ConvertToJobAsync_CreatesJobLinkedToSalesOrder()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            var soId = await service.CreateAsync(new SalesOrder
            {
                QuoteId = quoteId,
                CustomerId = customerId,
                Status = SalesOrderStatus.Confirmed,
                TaxRate = 0.15m,
                Total = 5750m,
                Subtotal = 5000m,
                Lines = { new SalesOrderLine { Description = "Work package", Quantity = 1, UnitPrice = 5000m } }
            });

            var job = await service.ConvertToJobAsync(soId);

            Assert.NotNull(job);
            Assert.Equal(soId, job.SalesOrderId);
            Assert.Equal(customerId, job.CustomerId);
            Assert.Equal(5750m, job.QuotedTotal);
            Assert.StartsWith("J-", job.JobNumber);

            var loadedSo = await service.GetByIdAsync(soId);
            Assert.Equal(SalesOrderStatus.InProgress, loadedSo!.Status);
        }
    }

    [Fact]
    public async Task ConvertToJobAsync_ThrowsWhenSalesOrderNotFound()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ConvertToJobAsync(Guid.NewGuid()));
        }
    }
}