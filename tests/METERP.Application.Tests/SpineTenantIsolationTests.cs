using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Core spine entities must not leak across tenants via global query filters.
/// </summary>
public class SpineTenantIsolationTests
{
    private static AppDbContext CreateContext(string dbName, Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    [Fact]
    public async Task QuoteService_GetByIdAsync_DoesNotReturnOtherTenantQuote()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid quoteBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Tenant B customer" };
            seedB.Set<Customer>().Add(customer);
            await seedB.SaveChangesAsync();

            var quote = new Quote
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                QuoteNumber = "Q-B-001",
                Status = QuoteStatus.Draft
            };
            seedB.Set<Quote>().Add(quote);
            await seedB.SaveChangesAsync();
            quoteBId = quote.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        Assert.Null(await new QuoteService(dbA).GetByIdAsync(quoteBId));
    }

    [Fact]
    public async Task JobService_GetByIdAsync_DoesNotReturnOtherTenantJob()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid jobBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Tenant B customer" };
            seedB.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                JobNumber = "J-B-001",
                Title = "Other tenant job",
                QuotedTotal = 1000m
            };
            seedB.Set<Job>().Add(job);
            await seedB.SaveChangesAsync();
            jobBId = job.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        Assert.Null(await new JobService(dbA).GetByIdAsync(jobBId));
    }

    [Fact]
    public async Task InvoiceService_GetByIdAsync_DoesNotReturnOtherTenantInvoice()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid invoiceBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Tenant B customer" };
            seedB.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                JobNumber = "J-B-INV",
                Title = "Billable job",
                QuotedTotal = 2000m
            };
            seedB.Set<Job>().Add(job);
            await seedB.SaveChangesAsync();

            var invoice = new Invoice
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "INV-B-001",
                Status = InvoiceStatus.Draft
            };
            seedB.Set<Invoice>().Add(invoice);
            await seedB.SaveChangesAsync();
            invoiceBId = invoice.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        Assert.Null(await new InvoiceService(dbA).GetByIdAsync(invoiceBId));
    }

    [Fact]
    public async Task StockRequisitionService_GetByIdAsync_DoesNotReturnOtherTenantRequisition()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid requisitionBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Tenant B customer" };
            seedB.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                JobNumber = "J-B-REQ",
                Title = "Stock job",
                QuotedTotal = 500m
            };
            seedB.Set<Job>().Add(job);
            await seedB.SaveChangesAsync();

            var requisition = new StockRequisition
            {
                TenantId = tenantB,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                RequisitionNumber = "REQ-B-001",
                Status = RequisitionStatus.PendingManager
            };
            seedB.Set<StockRequisition>().Add(requisition);
            await seedB.SaveChangesAsync();
            requisitionBId = requisition.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var requisitionService = new StockRequisitionService(dbA, new InventoryService(dbA));
        Assert.Null(await requisitionService.GetByIdAsync(requisitionBId));
    }

    [Fact]
    public async Task PurchaseOrderService_GetByIdAsync_DoesNotReturnOtherTenantPurchaseOrder()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid poBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var supplier = new Supplier { TenantId = tenantB, Name = "Tenant B supplier", IsActive = true };
            seedB.Set<Supplier>().Add(supplier);
            await seedB.SaveChangesAsync();

            var po = new PurchaseOrder
            {
                TenantId = tenantB,
                SupplierId = supplier.Id,
                PoNumber = "PO-B-001",
                Status = PurchaseOrderStatus.Draft
            };
            seedB.Set<PurchaseOrder>().Add(po);
            await seedB.SaveChangesAsync();
            poBId = po.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var poService = new PurchaseOrderService(dbA, new InventoryService(dbA));
        Assert.Null(await poService.GetByIdAsync(poBId));
    }

    [Fact]
    public async Task SalesOrderService_GetByIdAsync_DoesNotReturnOtherTenantSalesOrder()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid soBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Tenant B customer" };
            seedB.Set<Customer>().Add(customer);
            var quote = new Quote
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                QuoteNumber = "Q-B-SO",
                Status = QuoteStatus.Sent
            };
            seedB.Set<Quote>().Add(quote);
            await seedB.SaveChangesAsync();

            var so = new SalesOrder
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                QuoteId = quote.Id,
                SoNumber = "SO-B-001",
                Status = SalesOrderStatus.Confirmed
            };
            seedB.Set<SalesOrder>().Add(so);
            await seedB.SaveChangesAsync();
            soBId = so.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var soService = new SalesOrderService(dbA, new JobService(dbA));
        Assert.Null(await soService.GetByIdAsync(soBId));
    }

    [Fact]
    public async Task SalesOrderService_GetAllAsync_ReturnsOnlyCurrentTenantOrders()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "SO customer A" };
            seedA.Set<Customer>().Add(customer);
            var quote = new Quote
            {
                TenantId = tenantA,
                CustomerId = customer.Id,
                QuoteNumber = "Q-A-SO",
                Status = QuoteStatus.Sent
            };
            seedA.Set<Quote>().Add(quote);
            await seedA.SaveChangesAsync();
            seedA.Set<SalesOrder>().Add(new SalesOrder
            {
                TenantId = tenantA,
                CustomerId = customer.Id,
                QuoteId = quote.Id,
                SoNumber = "SO-A-001",
                Status = SalesOrderStatus.Confirmed
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "SO customer B" };
            seedB.Set<Customer>().Add(customer);
            var quote = new Quote
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                QuoteNumber = "Q-B-SO-ALL",
                Status = QuoteStatus.Sent
            };
            seedB.Set<Quote>().Add(quote);
            await seedB.SaveChangesAsync();
            seedB.Set<SalesOrder>().Add(new SalesOrder
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                QuoteId = quote.Id,
                SoNumber = "SO-B-001",
                Status = SalesOrderStatus.Draft
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var ordersA = await new SalesOrderService(dbA, new JobService(dbA)).GetAllAsync(pageSize: 50);
        Assert.Single(ordersA);
        Assert.Equal("SO-A-001", ordersA[0].SoNumber);

        await using var dbB = CreateContext(dbName, tenantB);
        var ordersB = await new SalesOrderService(dbB, new JobService(dbB)).GetAllAsync(pageSize: 50);
        Assert.Single(ordersB);
        Assert.Equal("SO-B-001", ordersB[0].SoNumber);
    }

    [Fact]
    public async Task QuoteService_GetAllAsync_ReturnsOnlyCurrentTenantQuotes()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "Quote customer A" };
            seedA.Set<Customer>().Add(customer);
            await seedA.SaveChangesAsync();
            seedA.Set<Quote>().Add(new Quote
            {
                TenantId = tenantA,
                CustomerId = customer.Id,
                QuoteNumber = "Q-A-ALL",
                Status = QuoteStatus.Sent
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Quote customer B" };
            seedB.Set<Customer>().Add(customer);
            await seedB.SaveChangesAsync();
            seedB.Set<Quote>().Add(new Quote
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                QuoteNumber = "Q-B-ALL",
                Status = QuoteStatus.Draft
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var quotesA = await new QuoteService(dbA).GetAllAsync(pageSize: 50);
        Assert.Single(quotesA);
        Assert.Equal("Q-A-ALL", quotesA[0].QuoteNumber);

        await using var dbB = CreateContext(dbName, tenantB);
        var quotesB = await new QuoteService(dbB).GetAllAsync(pageSize: 50);
        Assert.Single(quotesB);
        Assert.Equal("Q-B-ALL", quotesB[0].QuoteNumber);
    }

    [Fact]
    public async Task JobService_GetAllAsync_ReturnsOnlyCurrentTenantJobs()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "Job customer A" };
            seedA.Set<Customer>().Add(customer);
            await seedA.SaveChangesAsync();
            seedA.Set<Job>().Add(new Job
            {
                TenantId = tenantA,
                CustomerId = customer.Id,
                JobNumber = "J-A-ALL",
                Title = "Job A",
                QuotedTotal = 1200m
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Job customer B" };
            seedB.Set<Customer>().Add(customer);
            await seedB.SaveChangesAsync();
            seedB.Set<Job>().Add(new Job
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                JobNumber = "J-B-ALL",
                Title = "Job B",
                QuotedTotal = 2400m
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var jobsA = await new JobService(dbA).GetAllAsync(pageSize: 50);
        Assert.Single(jobsA);
        Assert.Equal("J-A-ALL", jobsA[0].JobNumber);

        await using var dbB = CreateContext(dbName, tenantB);
        var jobsB = await new JobService(dbB).GetAllAsync(pageSize: 50);
        Assert.Single(jobsB);
        Assert.Equal("J-B-ALL", jobsB[0].JobNumber);
    }

    [Fact]
    public async Task InvoiceService_CreateFromJobAsync_ThrowsWhenJobBelongsToOtherTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid jobBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Tenant B customer" };
            seedB.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                JobNumber = "J-B-INV-CREATE",
                Title = "Other tenant billable job",
                QuotedTotal = 3000m,
                SignOffStatus = JobSignOffStatus.SignedOff
            };
            seedB.Set<Job>().Add(job);
            await seedB.SaveChangesAsync();
            jobBId = job.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InvoiceService(dbA).CreateFromJobAsync(jobBId));

        Assert.Contains("Job not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvoiceService_GetAllAsync_ReturnsOnlyCurrentTenantInvoices()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "Invoice customer A" };
            seedA.Set<Customer>().Add(customer);
            await seedA.SaveChangesAsync();
            seedA.Set<Invoice>().Add(new Invoice
            {
                TenantId = tenantA,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-A-ALL",
                Status = InvoiceStatus.Sent,
                Total = 1500m
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Invoice customer B" };
            seedB.Set<Customer>().Add(customer);
            await seedB.SaveChangesAsync();
            seedB.Set<Invoice>().Add(new Invoice
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-B-ALL",
                Status = InvoiceStatus.Draft,
                Total = 900m
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var invoicesA = await new InvoiceService(dbA).GetAllAsync(pageSize: 50);
        Assert.Single(invoicesA);
        Assert.Equal("INV-A-ALL", invoicesA[0].InvoiceNumber);

        await using var dbB = CreateContext(dbName, tenantB);
        var invoicesB = await new InvoiceService(dbB).GetAllAsync(pageSize: 50);
        Assert.Single(invoicesB);
        Assert.Equal("INV-B-ALL", invoicesB[0].InvoiceNumber);
    }
}