using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class InvoiceLinkJobTests
{
    [Theory]
    [InlineData("Access CSV | TRFid=FT16010 | JobCardNo=JC1", "FT16010")]
    [InlineData("TRFid=", null)]
    [InlineData("no marker", null)]
    [InlineData(null, null)]
    public void TryReadTrfId_ReadsAccessNote(string? notes, string? expected)
    {
        Assert.Equal(expected, InvoiceLegacyRef.TryReadTrfId(notes));
    }

    [Fact]
    public async Task LinkJobAsync_SetsJobId_LeavesInvoiceTotalsAndClosedJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Lodge" };
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobNumber = "FT16010",
            Title = "Kruger",
            Status = JobStatus.Closed,
            QuotedTotal = 61840m,
            ClosedAt = DateTime.UtcNow
        };
        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            InvoiceNumber = "INV-ORPHAN",
            InvoiceDate = DateTime.UtcNow.Date,
            DueDate = DateTime.UtcNow.Date.AddDays(30),
            Status = InvoiceStatus.Paid,
            AmountPaid = 1500m,
            Subtotal = 1304.35m,
            Tax = 195.65m,
            Total = 1500m,
            Notes = "Access CSV | TRFid=FT16010 | JobCardNo="
        };
        db.AddRange(customer, job, invoice);
        await db.SaveChangesAsync();

        var service = new InvoiceService(db);
        await service.LinkJobAsync(invoice.Id, job.Id);

        var saved = await db.Set<Invoice>().IgnoreQueryFilters().FirstAsync(i => i.Id == invoice.Id);
        Assert.Equal(job.Id, saved.JobId);
        Assert.Equal(1500m, saved.Total);
        Assert.Equal(1304.35m, saved.Subtotal);
        Assert.Equal(InvoiceStatus.Paid, saved.Status);
        Assert.Equal(1500m, saved.AmountPaid);

        var jobAfter = await db.Set<Job>().IgnoreQueryFilters().FirstAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.Closed, jobAfter.Status);
        Assert.Equal(61840m, jobAfter.QuotedTotal);
        Assert.NotNull(jobAfter.ClosedAt);
    }

    [Fact]
    public async Task LinkJobAsync_RejectsCustomerMismatch_AndSecondLink()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var customerA = new Customer { TenantId = tenantId, Name = "A" };
        var customerB = new Customer { TenantId = tenantId, Name = "B" };
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerA.Id,
            JobNumber = "SD393",
            Title = "Other",
            Status = JobStatus.InProgress
        };
        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = customerB.Id,
            InvoiceNumber = "INV-MIS",
            InvoiceDate = DateTime.UtcNow.Date,
            DueDate = DateTime.UtcNow.Date.AddDays(7),
            Status = InvoiceStatus.Sent,
            Total = 80m
        };
        db.AddRange(customerA, customerB, job, invoice);
        await db.SaveChangesAsync();

        var service = new InvoiceService(db);
        var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkJobAsync(invoice.Id, job.Id));
        Assert.Contains("customer", mismatch.Message, StringComparison.OrdinalIgnoreCase);

        invoice.CustomerId = customerA.Id;
        await db.SaveChangesAsync();
        await service.LinkJobAsync(invoice.Id, job.Id);

        var other = new Job
        {
            TenantId = tenantId,
            CustomerId = customerA.Id,
            JobNumber = "PD0085",
            Title = "Second",
            Status = JobStatus.Completed
        };
        db.Add(other);
        await db.SaveChangesAsync();

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkJobAsync(invoice.Id, other.Id));
        Assert.Contains("already linked", again.Message, StringComparison.OrdinalIgnoreCase);

        var jobAfter = await db.Set<Job>().IgnoreQueryFilters().FirstAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.InProgress, jobAfter.Status);
    }

    [Fact]
    public async Task GetAllAsync_UnlinkedOnly_SkipsLinkedInvoices()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Acme" };
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobNumber = "J-1",
            Title = "Linked",
            Status = JobStatus.InProgress
        };
        db.AddRange(
            customer,
            job,
            new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "INV-LINKED",
                InvoiceDate = DateTime.UtcNow.Date,
                DueDate = DateTime.UtcNow.Date.AddDays(10),
                Total = 10m
            },
            new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-FREE",
                InvoiceDate = DateTime.UtcNow.Date,
                DueDate = DateTime.UtcNow.Date.AddDays(10),
                Total = 20m,
                Notes = "Access CSV | TRFid=FT9 | JobCardNo="
            });
        await db.SaveChangesAsync();

        var service = new InvoiceService(db);
        var unlinked = await service.GetAllAsync(unlinkedOnly: true);
        var only = Assert.Single(unlinked);
        Assert.Equal("INV-FREE", only.InvoiceNumber);
        Assert.Null(only.JobId);
        Assert.Equal("FT9", InvoiceLegacyRef.TryReadTrfId(only.Notes));
    }

    private static AppDbContext CreateContext(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(u => u.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }
}
