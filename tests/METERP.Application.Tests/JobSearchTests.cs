using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class JobSearchTests
{
    private const string AccessNotes = "TRFid=FT16010 | JobCardNo=JC9";

    [Fact]
    public async Task GetAllAsync_NotesTrfAndJobCard_AreReturned_AndCountMatches()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var service = new JobService(db);
        var customer = await SeedCustomer(db, tenantId, "Mine Co");

        var noted = await SeedJob(db, tenantId, customer.Id, "J-1001", "Transformer rewind", notes: AccessNotes);
        await SeedJob(db, tenantId, customer.Id, "J-1004", "Yard tidy");

        var byTrf = await service.GetAllAsync("FT16010", pageSize: 50);
        var byCard = await service.GetAllAsync("JC9", pageSize: 50);
        var byTrfLower = await service.GetAllAsync("ft16010", pageSize: 50);

        Assert.Equal(noted.Id, Assert.Single(byTrf).Id);
        Assert.Equal(noted.Id, Assert.Single(byCard).Id);
        Assert.Equal(noted.Id, Assert.Single(byTrfLower).Id);
        Assert.Equal(1, await service.CountAsync("FT16010"));
        Assert.Equal(1, await service.CountAsync("JC9"));
        Assert.Equal(1, await service.CountAsync("ft16010"));
    }

    [Fact]
    public async Task GetAllAsync_DescriptionJobCard_AndCustomerOrder_AreReturned()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var service = new JobService(db);
        var customer = await SeedCustomer(db, tenantId, "Works Co");

        var described = await SeedJob(
            db, tenantId, customer.Id, "J-1002", "Cable pull",
            description: "Type: Install | JobCardNo: JC-ONLY");
        var ordered = await SeedJob(
            db, tenantId, customer.Id, "J-1003", "Panel swap",
            customerOrderNo: "PO-7781");

        var byCard = await service.GetAllAsync("JC-ONLY", pageSize: 50);
        var byOrder = await service.GetAllAsync("po-7781", pageSize: 50);

        Assert.Equal(described.Id, Assert.Single(byCard).Id);
        Assert.Equal(ordered.Id, Assert.Single(byOrder).Id);
        Assert.Equal(1, await service.CountAsync("JC-ONLY"));
        Assert.Equal(1, await service.CountAsync("PO-7781"));
    }

    [Fact]
    public async Task GetAllAsync_DoesNotReturnAnotherTenant_OrDeletedJob()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");

        await using var dbA = CreateDb(tenantA, dbName);
        var customerA = await SeedCustomer(dbA, tenantA, "Tenant A");
        var own = await SeedJob(dbA, tenantA, customerA.Id, "J-A", "Own rewind", notes: AccessNotes);
        var deleted = await SeedJob(dbA, tenantA, customerA.Id, "J-GONE", "Deleted rewind", notes: AccessNotes);
        deleted.IsDeleted = true;
        await dbA.SaveChangesAsync();

        await using var dbB = CreateDb(tenantB, dbName);
        var customerB = await SeedCustomer(dbB, tenantB, "Tenant B");
        await SeedJob(dbB, tenantB, customerB.Id, "J-B", "Other rewind", notes: AccessNotes);

        var serviceA = new JobService(dbA);
        var found = await serviceA.GetAllAsync("FT16010", pageSize: 50);

        Assert.Equal(own.Id, Assert.Single(found).Id);
        Assert.Equal(1, await serviceA.CountAsync("FT16010"));
        Assert.Equal(1, await serviceA.CountAsync("JC9"));

        var serviceB = new JobService(dbB);
        var other = await serviceB.GetAllAsync("JC9", pageSize: 50);
        Assert.Equal("J-B", Assert.Single(other).JobNumber);
    }

    [Fact]
    public async Task GetAllAsync_BlankSearch_IsUnchanged()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var service = new JobService(db);
        var customer = await SeedCustomer(db, tenantId, "Blank Co");

        await SeedJob(db, tenantId, customer.Id, "J-1001", "Transformer rewind", notes: AccessNotes, created: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await SeedJob(db, tenantId, customer.Id, "J-1004", "Yard tidy", created: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        var unfiltered = Numbers(await service.GetAllAsync(pageSize: 50));
        Assert.Equal(new[] { "J-1004", "J-1001" }, unfiltered);
        Assert.Equal(unfiltered, Numbers(await service.GetAllAsync("", pageSize: 50)));
        Assert.Equal(unfiltered, Numbers(await service.GetAllAsync("   ", pageSize: 50)));
        Assert.Equal(unfiltered, Numbers(await service.GetAllAsync("\t", pageSize: 50)));
        Assert.Equal(2, await service.CountAsync(null));
        Assert.Equal(2, await service.CountAsync(""));
        Assert.Equal(2, await service.CountAsync("   "));
    }

    private static string[] Numbers(IReadOnlyList<Job> jobs) => jobs.Select(j => j.JobNumber).ToArray();

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
        string title,
        string? notes = null,
        string? description = null,
        string? customerOrderNo = null,
        DateTime? created = null)
    {
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = number,
            Title = title,
            Notes = notes,
            Description = description,
            CustomerOrderNo = customerOrderNo,
            QuotedTotal = 1000m,
            Status = JobStatus.InProgress,
            CreatedDate = created ?? DateTime.UtcNow
        };
        db.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static AppDbContext CreateDb(Guid tenantId, string databaseName)
    {
        var tenant = new Mock<ITenantProvider>();
        tenant.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserName).Returns("tester");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new AppDbContext(options, tenant.Object, currentUser.Object);
    }
}
