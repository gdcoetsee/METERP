using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Seeding;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class FieldAssignmentTests
{
    [Fact]
    public void IsTrfIdJobNumber_AcceptsFtAndSdDigitsOnly()
    {
        Assert.True(FieldAssignmentSeeder.IsTrfIdJobNumber("FT16010"));
        Assert.True(FieldAssignmentSeeder.IsTrfIdJobNumber("SD393"));
        Assert.False(FieldAssignmentSeeder.IsTrfIdJobNumber("FTNB14116"));
        Assert.False(FieldAssignmentSeeder.IsTrfIdJobNumber("FT STOCK CORRECTIONS"));
        Assert.False(FieldAssignmentSeeder.IsTrfIdJobNumber("FT16265/1"));
        Assert.False(FieldAssignmentSeeder.IsTrfIdJobNumber("PD0085"));
    }

    [Fact]
    public async Task RunAsync_AssignsHandfulOfInProgressTrfIds_AndDoesNotRepeat()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var userId = await SeedTechUserAsync(db, tenantId);

        var other = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = "EMP-OTHER",
            FirstName = "Other",
            LastName = "Lead",
            IsActive = true
        };
        db.Add(other);

        AddJob(db, tenantId, "FT154444", JobStatus.InProgress);
        AddJob(db, tenantId, "FT131221", JobStatus.InProgress);
        AddJob(db, tenantId, "FT16371", JobStatus.InProgress);
        AddJob(db, tenantId, "SD410", JobStatus.InProgress);
        AddJob(db, tenantId, "SD408", JobStatus.InProgress);
        AddJob(db, tenantId, "FT99999", JobStatus.Closed);
        AddJob(db, tenantId, "FTNB14116", JobStatus.InProgress);
        AddJob(db, tenantId, "FT STOCK CORRECTIONS", JobStatus.InProgress);
        AddJob(db, tenantId, "J-900", JobStatus.InProgress);
        AddJob(db, tenantId, "FT0001", JobStatus.Scheduled);
        var taken = AddJob(db, tenantId, "FT10001", JobStatus.InProgress);
        taken.AssignedEmployeeId = other.Id;
        await db.SaveChangesAsync();

        var provider = Tenant(tenantId);
        var first = await FieldAssignmentSeeder.RunAsync(db, provider.Object);
        Assert.Equal(5, first.Assigned);
        Assert.Equal(0, first.AlreadyAssigned);
        Assert.Equal(
            new[] { "FT154444", "FT131221", "FT16371", "SD410", "SD408" },
            first.JobNumbers);
        Assert.Equal(FieldAssignmentSeeder.DemoEmployeeNumber, first.EmployeeNumber);

        var second = await FieldAssignmentSeeder.RunAsync(db, provider.Object);
        Assert.Equal(0, second.Assigned);
        Assert.True(second.AlreadyAssigned >= FieldAssignmentSeeder.StopWhenAtLeast);

        var assigned = await db.Set<Job>().IgnoreQueryFilters()
            .Where(j => j.AssignedEmployeeId != null && j.AssignedEmployeeId != other.Id)
            .Select(j => j.JobNumber)
            .ToListAsync();
        Assert.Equal(5, assigned.Count);
        Assert.DoesNotContain("FT99999", assigned);
        Assert.DoesNotContain("FTNB14116", assigned);
        Assert.DoesNotContain("J-900", assigned);
        Assert.DoesNotContain("FT10001", assigned);
        Assert.DoesNotContain("FT0001", assigned);

        var demoId = await db.Set<Employee>().IgnoreQueryFilters()
            .Where(e => e.EmployeeNumber == FieldAssignmentSeeder.DemoEmployeeNumber)
            .Select(e => e.Id)
            .SingleAsync();
        Assert.Equal(userId, await db.Set<Employee>().IgnoreQueryFilters()
            .Where(e => e.Id == demoId)
            .Select(e => e.LinkedUserId)
            .SingleAsync());
        Assert.Equal(5, await db.Set<JobCrewAssignment>().IgnoreQueryFilters()
            .CountAsync(a => a.EmployeeId == demoId && !a.IsDeleted));
        Assert.Equal(other.Id, await db.Set<Job>().IgnoreQueryFilters()
            .Where(j => j.JobNumber == "FT10001")
            .Select(j => j.AssignedEmployeeId)
            .SingleAsync());
    }

    [Fact]
    public async Task RunAsync_StopsWhenTechAlreadyHasAHandful()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var userId = await SeedTechUserAsync(db, tenantId);
        var tech = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = "EMP-LINKED",
            FirstName = "Johan",
            LastName = "van der Berg",
            IsActive = true,
            LinkedUserId = userId
        };
        db.Add(tech);
        for (var i = 0; i < 3; i++)
        {
            var job = AddJob(db, tenantId, $"FT20{i}", JobStatus.InProgress);
            job.AssignedEmployeeId = tech.Id;
        }
        AddJob(db, tenantId, "FT2099", JobStatus.InProgress);
        await db.SaveChangesAsync();

        var result = await FieldAssignmentSeeder.RunAsync(db, Tenant(tenantId).Object);

        Assert.Equal(0, result.Assigned);
        Assert.Equal(3, result.AlreadyAssigned);
        Assert.Equal("EMP-LINKED", result.EmployeeNumber);
        Assert.Equal(0, await db.Set<Employee>().IgnoreQueryFilters()
            .CountAsync(e => e.EmployeeNumber == FieldAssignmentSeeder.DemoEmployeeNumber));
        Assert.Null(await db.Set<Job>().IgnoreQueryFilters()
            .Where(j => j.JobNumber == "FT2099")
            .Select(j => j.AssignedEmployeeId)
            .SingleAsync());
    }

    [Fact]
    public async Task RunAsync_WithoutTechUser_AssignsNothing()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        AddJob(db, tenantId, "FT16010", JobStatus.InProgress);
        await db.SaveChangesAsync();

        var result = await FieldAssignmentSeeder.RunAsync(db, Tenant(tenantId).Object);

        Assert.Equal(0, result.Assigned);
        Assert.False(result.EmployeeReady);
        Assert.Equal(0, await db.Set<Employee>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task RunAsync_FallsBackToInProgressJobs_WhenNoTrfIds()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        await SeedTechUserAsync(db, tenantId);
        AddJob(db, tenantId, "J-100", JobStatus.InProgress);
        AddJob(db, tenantId, "J-101", JobStatus.InProgress);
        AddJob(db, tenantId, "J-050", JobStatus.Closed);
        await db.SaveChangesAsync();

        var result = await FieldAssignmentSeeder.RunAsync(db, Tenant(tenantId).Object);

        Assert.Equal(2, result.Assigned);
        Assert.Equal(new[] { "J-101", "J-100" }, result.JobNumbers);
    }

    [Fact]
    public async Task GetAssignedOpenJobsForUserAsync_ReturnsLeadAndCrewOnly()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);

        var tech = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = "TECH-1",
            FirstName = "Field",
            LastName = "Technician",
            IsActive = true,
            LinkedUserId = userId
        };
        var other = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = "TECH-2",
            FirstName = "Other",
            LastName = "Tech",
            IsActive = true,
            LinkedUserId = otherUserId
        };
        db.AddRange(tech, other);

        var customer = new Customer { TenantId = tenantId, Name = "Sanbonani" };
        db.Add(customer);

        var lead = AddJob(db, tenantId, "FT16371", JobStatus.InProgress, customer.Id);
        lead.AssignedEmployeeId = tech.Id;
        var crewOnly = AddJob(db, tenantId, "SD393", JobStatus.Scheduled, customer.Id);
        db.Add(new JobCrewAssignment { TenantId = tenantId, JobId = crewOnly.Id, EmployeeId = tech.Id });
        var someoneElse = AddJob(db, tenantId, "FT10001", JobStatus.InProgress, customer.Id);
        someoneElse.AssignedEmployeeId = other.Id;
        var closed = AddJob(db, tenantId, "FT0002", JobStatus.Closed, customer.Id);
        closed.AssignedEmployeeId = tech.Id;
        var unassigned = AddJob(db, tenantId, "FT16010", JobStatus.InProgress, customer.Id);
        var droppedCrew = AddJob(db, tenantId, "SD001", JobStatus.InProgress, customer.Id);
        db.Add(new JobCrewAssignment
        {
            TenantId = tenantId,
            JobId = droppedCrew.Id,
            EmployeeId = tech.Id,
            IsDeleted = true
        });
        await db.SaveChangesAsync();

        var service = new JobService(db);
        var mine = await service.GetAssignedOpenJobsForUserAsync(userId);
        Assert.Equal(new[] { "FT16371", "SD393" }, mine.Select(j => j.JobNumber).OrderBy(n => n).ToArray());

        Assert.Empty(await service.GetAssignedOpenJobsForUserAsync(Guid.NewGuid()));

        var office = await service.GetAllAsync(pageSize: 50);
        Assert.Contains(office, j => j.JobNumber == unassigned.JobNumber);
        Assert.Contains(office, j => j.JobNumber == someoneElse.JobNumber);
        Assert.True(office.Count >= 6);
    }

    private static AppDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Tenant(tenantId).Object, new Mock<ICurrentUserService>().Object);
    }

    private static Mock<ITenantProvider> Tenant(Guid tenantId)
    {
        var holder = new Guid[] { tenantId };
        var provider = new Mock<ITenantProvider>();
        provider.Setup(p => p.GetCurrentTenantId()).Returns(() => holder[0]);
        provider.Setup(p => p.SetTenantId(It.IsAny<Guid>())).Callback<Guid>(id => holder[0] = id);
        return provider;
    }

    private static async Task<Guid> SeedTechUserAsync(AppDbContext db, Guid tenantId)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = FieldAssignmentSeeder.TechEmail,
            NormalizedUserName = FieldAssignmentSeeder.TechEmail.ToUpperInvariant(),
            Email = FieldAssignmentSeeder.TechEmail,
            NormalizedEmail = FieldAssignmentSeeder.TechEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            TenantId = tenantId,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static Job AddJob(AppDbContext db, Guid tenantId, string number, JobStatus status, Guid? customerId = null)
    {
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId ?? Guid.NewGuid(),
            JobNumber = number,
            Title = number,
            Status = status
        };
        db.Add(job);
        return job;
    }
}
