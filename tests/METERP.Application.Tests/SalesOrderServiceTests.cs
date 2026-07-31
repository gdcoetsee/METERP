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
    public async Task ConvertToJobAsync_WithTravelLine_PreservesQuoteLinkAndTotals()
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
                Lines =
                {
                    new SalesOrderLine { Description = "Work package", Quantity = 1, UnitPrice = 5000m },
                    new SalesOrderLine { Description = "Mobilization travel", Quantity = 1, UnitPrice = 750m, LineType = "Travel" }
                }
            });

            var job = await service.ConvertToJobAsync(soId);

            var loadedSo = await service.GetByIdAsync(soId);

            Assert.Equal(quoteId, job.QuoteId);
            Assert.Equal(soId, job.SalesOrderId);
            Assert.Equal(loadedSo!.Total, job.QuotedTotal);
            Assert.Contains(loadedSo.Lines, l => l.LineType == "Travel" && l.UnitPrice == 750m);
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

    [Fact]
    public async Task ConvertToJobAsync_ThrowsWhenCustomerDeleted()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            var soId = await service.CreateAsync(new SalesOrder
            {
                CustomerId = customerId,
                QuoteId = quoteId,
                SoDate = DateTime.UtcNow.Date,
                TaxRate = 0.15m,
                Lines =
                [
                    new SalesOrderLine { Description = "Work", Quantity = 1, UnitPrice = 100m }
                ]
            });

            var customer = await db.Set<Customer>().FirstAsync(c => c.Id == customerId);
            customer.IsDeleted = true;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ConvertToJobAsync(soId));
            Assert.Contains("customer", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenInProgress()
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
                Status = SalesOrderStatus.Draft,
                Lines = { new SalesOrderLine { Description = "X", Quantity = 1, UnitPrice = 10m } }
            });
            await service.UpdateStatusAsync(soId, SalesOrderStatus.Confirmed);
            await service.ConvertToJobAsync(soId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(soId));
        }
    }

    [Fact]
    public async Task UpdateStatusAsync_ThrowsWhenConfirmingEmptySo()
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
                TaxRate = 0.15m
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateStatusAsync(soId, SalesOrderStatus.Confirmed));
            Assert.Contains("no lines", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ConvertToJobAsync_ThrowsWhenNoLines()
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
                TaxRate = 0.15m
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ConvertToJobAsync(soId));
            Assert.Contains("no lines", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ConvertToJobAsync_ThrowsWhenAlreadyConverted()
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
                Lines = { new SalesOrderLine { Description = "Pkg", Quantity = 1, UnitPrice = 1000m } }
            });

            Assert.NotEqual(Guid.Empty, soId);
            Assert.NotNull(await service.GetByIdAsync(soId));

            await service.ConvertToJobAsync(soId);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ConvertToJobAsync(soId));
            Assert.Contains("already converted", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNotesTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new SalesOrder
                {
                    CustomerId = customerId,
                    QuoteId = quoteId,
                    SoDate = DateTime.UtcNow.Date,
                    TaxRate = 0.15m,
                    Notes = new string('N', 2001)
                }));
            Assert.Contains("2000 characters", ex.Message);
        }
    }

    [Fact]
    public async Task AddLineAsync_ThrowsWhenLineTypeTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            var soId = await service.CreateAsync(new SalesOrder
            {
                CustomerId = customerId,
                QuoteId = quoteId,
                SoDate = DateTime.UtcNow.Date,
                TaxRate = 0.15m
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddLineAsync(new SalesOrderLine
                {
                    SalesOrderId = soId,
                    Description = "Work",
                    Quantity = 1,
                    UnitPrice = 100m,
                    LineType = new string('T', 51)
                }));
            Assert.Contains("50 characters", ex.Message);
        }
    }

    [Fact]
    public async Task AddLineAsync_ThrowsWhenUnitTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            var soId = await service.CreateAsync(new SalesOrder
            {
                CustomerId = customerId,
                QuoteId = quoteId,
                SoDate = DateTime.UtcNow.Date,
                TaxRate = 0.15m
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddLineAsync(new SalesOrderLine
                {
                    SalesOrderId = soId,
                    Description = "Work",
                    Quantity = 1,
                    UnitPrice = 100m,
                    Unit = new string('U', 21)
                }));
            Assert.Contains("20 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenTaxRateOutOfRange()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new SalesOrder
                {
                    QuoteId = quoteId,
                    CustomerId = customerId,
                    TaxRate = 1.5m
                }));
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenSoNumberTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new SalesOrder
                {
                    CustomerId = customerId,
                    QuoteId = quoteId,
                    SoNumber = new string('S', 51),
                    SoDate = DateTime.UtcNow.Date,
                    TaxRate = 0.15m
                }));
            Assert.Contains("50 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenSoNumberDuplicate()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            await service.CreateAsync(new SalesOrder
            {
                QuoteId = quoteId,
                CustomerId = customerId,
                SoNumber = "SO-DUP-1",
                TaxRate = 0.15m
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new SalesOrder
                {
                    QuoteId = quoteId,
                    CustomerId = customerId,
                    SoNumber = "SO-DUP-1",
                    TaxRate = 0.15m
                }));
            Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenQuoteMissing()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new SalesOrder
                {
                    QuoteId = Guid.NewGuid(),
                    CustomerId = customerId,
                    TaxRate = 0.15m
                }));
            Assert.Contains("quote", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenQuoteCustomerMismatch()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var customerA = Guid.NewGuid();
            var customerB = Guid.NewGuid();
            var quoteId = Guid.NewGuid();
            db.Set<Customer>().AddRange(
                new Customer { Id = customerA, TenantId = tenantId, Name = "A" },
                new Customer { Id = customerB, TenantId = tenantId, Name = "B" });
            db.Set<Quote>().Add(new Quote
            {
                Id = quoteId,
                TenantId = tenantId,
                CustomerId = customerA,
                QuoteNumber = "Q-A",
                Status = QuoteStatus.Accepted
            });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new SalesOrder
                {
                    QuoteId = quoteId,
                    CustomerId = customerB,
                    TaxRate = 0.15m
                }));
            Assert.Contains("customer", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenQuoteRejected()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var customerId = Guid.NewGuid();
            var quoteId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            db.Set<Quote>().Add(new Quote
            {
                Id = quoteId,
                TenantId = tenantId,
                CustomerId = customerId,
                QuoteNumber = "Q-REJ",
                Status = QuoteStatus.Rejected
            });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new SalesOrder
                {
                    QuoteId = quoteId,
                    CustomerId = customerId,
                    TaxRate = 0.15m
                }));
            Assert.Contains("rejected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UpdateAsync_PreservesQuoteId()
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
                TaxRate = 0.15m,
                Notes = "before"
            });

            var so = await service.GetByIdAsync(soId);
            so!.Notes = "after";
            so.QuoteId = Guid.NewGuid();
            await service.UpdateAsync(so);

            var reloaded = await service.GetByIdAsync(soId);
            Assert.Equal(quoteId, reloaded!.QuoteId);
            Assert.Equal("after", reloaded.Notes);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenDeliveryDateBeforeSoDate()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new SalesOrder
                {
                    QuoteId = quoteId,
                    CustomerId = customerId,
                    TaxRate = 0.15m,
                    SoDate = DateTime.UtcNow.Date,
                    DeliveryDate = DateTime.UtcNow.Date.AddDays(-3)
                }));
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenDeliveryDateTooFarFuture()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new SalesOrder
                {
                    QuoteId = quoteId,
                    CustomerId = customerId,
                    TaxRate = 0.15m,
                    SoDate = DateTime.UtcNow.Date,
                    DeliveryDate = DateTime.UtcNow.Date.AddYears(3)
                }));
            Assert.Contains("2 years", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AddLineAsync_ThrowsWhenQuantityNotPositive()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            var soId = await service.CreateAsync(new SalesOrder { QuoteId = quoteId, CustomerId = customerId, TaxRate = 0.15m });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddLineAsync(new SalesOrderLine
                {
                    SalesOrderId = soId,
                    Description = "Bad qty",
                    Quantity = 0,
                    UnitPrice = 100m
                }));
        }
    }

    [Fact]
    public async Task AddLineAsync_ThrowsWhenDescriptionMissing()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateServices(tenantId);
        using (db)
        {
            var (customerId, quoteId) = await SeedCustomerAndQuoteAsync(db, tenantId);
            var soId = await service.CreateAsync(new SalesOrder { QuoteId = quoteId, CustomerId = customerId, TaxRate = 0.15m });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddLineAsync(new SalesOrderLine
                {
                    SalesOrderId = soId,
                    Description = "  ",
                    Quantity = 1,
                    UnitPrice = 100m
                }));
        }
    }
}