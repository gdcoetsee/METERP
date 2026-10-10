using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class JobStillToInvoiceTests
{
    [Fact]
    public async Task GetStillToInvoiceAsync_Quoted1000Billed400_Is600_AndCreditNoteDoesNotCount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var service = new JobService(db, tenantProvider: Tenant(tenantId));
        var customer = await SeedCustomer(db, tenantId, "Billed Co");

        var open = await SeedJob(db, tenantId, customer.Id, "J-OPEN", 1000m, JobStatus.InProgress);
        AddInvoice(db, tenantId, customer.Id, open.Id, "INV-400", 400m, InvoiceDocumentType.Standard, InvoiceStatus.Sent);
        AddInvoice(db, tenantId, customer.Id, open.Id, "CN-400", 400m, InvoiceDocumentType.CreditNote, InvoiceStatus.Sent);
        AddInvoice(db, tenantId, customer.Id, open.Id, "INV-DRAFT", 100m, InvoiceDocumentType.Standard, InvoiceStatus.Draft);
        AddInvoice(db, tenantId, customer.Id, open.Id, "INV-PRO", 80m, InvoiceDocumentType.Proforma, InvoiceStatus.Sent);
        AddInvoice(db, tenantId, customer.Id, open.Id, "INV-CAN", 70m, InvoiceDocumentType.Standard, InvoiceStatus.Cancelled);
        var deleted = AddInvoice(db, tenantId, customer.Id, open.Id, "INV-DEL", 500m, InvoiceDocumentType.Standard, InvoiceStatus.Paid);
        deleted.IsDeleted = true;

        var closed = await SeedJob(db, tenantId, customer.Id, "J-CLOSED", 1000m, JobStatus.Closed);
        AddInvoice(db, tenantId, customer.Id, closed.Id, "INV-CLOSED", 400m, InvoiceDocumentType.Partial, InvoiceStatus.PartiallyPaid);

        var cancelled = await SeedJob(db, tenantId, customer.Id, "J-CANCEL", 250m, JobStatus.Cancelled);

        var over = await SeedJob(db, tenantId, customer.Id, "J-OVER", 1000m, JobStatus.Completed);
        AddInvoice(db, tenantId, customer.Id, over.Id, "INV-OVER", 1500m, InvoiceDocumentType.Final, InvoiceStatus.Paid);

        await db.SaveChangesAsync();

        var result = await service.GetStillToInvoiceAsync(new[] { open.Id, closed.Id, cancelled.Id, over.Id });

        Assert.Equal(600m, result[open.Id]);
        Assert.Equal(600m, result[closed.Id]);
        Assert.Equal(250m, result[cancelled.Id]);
        Assert.Equal(0m, result[over.Id]);
    }

    [Fact]
    public async Task GetStillToInvoiceAsync_Empty_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var service = new JobService(db, tenantProvider: Tenant(tenantId));

        var result = await service.GetStillToInvoiceAsync(Array.Empty<Guid>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStillToInvoiceAsync_OmitsOtherTenant_DeletedJob_AndForeignInvoice()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        await using var dbA = CreateDb(tenantA, dbName);
        var customerA = await SeedCustomer(dbA, tenantA, "Tenant A");
        var jobA = await SeedJob(dbA, tenantA, customerA.Id, "J-A", 1000m, JobStatus.InProgress);
        AddInvoice(dbA, tenantA, customerA.Id, jobA.Id, "INV-A", 400m, InvoiceDocumentType.Standard, InvoiceStatus.Sent);
        var deletedJob = await SeedJob(dbA, tenantA, customerA.Id, "J-GONE", 800m, JobStatus.Closed);
        deletedJob.IsDeleted = true;
        await dbA.SaveChangesAsync();

        await using var dbB = CreateDb(tenantB, dbName);
        var customerB = await SeedCustomer(dbB, tenantB, "Tenant B");
        var jobB = await SeedJob(dbB, tenantB, customerB.Id, "J-B", 5000m, JobStatus.InProgress);
        AddInvoice(dbB, tenantB, customerB.Id, jobA.Id, "INV-LEAK", 9000m, InvoiceDocumentType.Standard, InvoiceStatus.Sent);
        AddInvoice(dbB, tenantB, customerB.Id, jobB.Id, "INV-B", 1000m, InvoiceDocumentType.Standard, InvoiceStatus.Sent);
        await dbB.SaveChangesAsync();

        var result = await new JobService(dbA, tenantProvider: Tenant(tenantA))
            .GetStillToInvoiceAsync(new[] { jobA.Id, jobB.Id, deletedJob.Id });

        Assert.True(result.TryGetValue(jobA.Id, out var stillA));
        Assert.Equal(600m, stillA);
        Assert.False(result.ContainsKey(jobB.Id));
        Assert.False(result.ContainsKey(deletedJob.Id));
    }

    private static Invoice AddInvoice(
        AppDbContext db,
        Guid tenantId,
        Guid customerId,
        Guid jobId,
        string number,
        decimal total,
        InvoiceDocumentType type,
        InvoiceStatus status)
    {
        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobId = jobId,
            InvoiceNumber = number,
            DocumentType = type,
            Status = status,
            Subtotal = total,
            Tax = 0m,
            Total = total,
            TaxRate = 0.15m
        };
        db.Add(invoice);
        return invoice;
    }

    private static async Task<Customer> SeedCustomer(AppDbContext db, Guid tenantId, string name)
    {
        var customer = new Customer { TenantId = tenantId, Name = name };
        db.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static async Task<Job> SeedJob(
        AppDbContext db,
        Guid tenantId,
        Guid customerId,
        string number,
        decimal quoted,
        JobStatus status)
    {
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = number,
            Title = number,
            QuotedTotal = quoted,
            Status = status
        };
        db.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static ITenantProvider Tenant(Guid tenantId)
    {
        var tenant = new Mock<ITenantProvider>();
        tenant.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        return tenant.Object;
    }

    private static AppDbContext CreateDb(Guid tenantId, string databaseName)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserName).Returns("tester");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new AppDbContext(options, Tenant(tenantId), currentUser.Object);
    }
}
