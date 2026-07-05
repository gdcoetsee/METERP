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
}