using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Relational provider coverage for the home sign-off queue.
/// InMemory evaluates <see cref="JobLabor.TotalCost"/> in process; Npgsql does not translate it.
/// </summary>
public class AwaitingSignOffQueueTests
{
    [Fact]
    public async Task GetAwaitingSignOffQueueAsync_TranslatesOnSqlite_UsesHeaderCostPlusLabor_SkipsClosedSignedAndFullyBilled()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(s => s.UserName).Returns("test-user");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        await db.Database.EnsureCreatedAsync();

        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Sign-off Co" });

        var waitingId = Guid.NewGuid();
        var laborOnlyId = Guid.NewGuid();
        var signedId = Guid.NewGuid();
        var billedId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        var noWorkId = Guid.NewGuid();

        db.Set<Job>().AddRange(
            Job(waitingId, tenantId, customerId, "J-WAIT", JobStatus.InProgress, JobSignOffStatus.None, actualCost: 1650m),
            Job(laborOnlyId, tenantId, customerId, "J-LABOR", JobStatus.Completed, JobSignOffStatus.PendingExecutive, actualCost: 0m),
            Job(signedId, tenantId, customerId, "J-SIGNED", JobStatus.InProgress, JobSignOffStatus.SignedOff, actualCost: 800m),
            Job(billedId, tenantId, customerId, "J-BILLED", JobStatus.InProgress, JobSignOffStatus.None, actualCost: 400m),
            Job(closedId, tenantId, customerId, "J-CLOSED", JobStatus.Closed, JobSignOffStatus.None, actualCost: 900m),
            Job(noWorkId, tenantId, customerId, "J-EMPTY", JobStatus.InProgress, JobSignOffStatus.None, actualCost: 0m));

        db.Set<JobCost>().Add(new JobCost
        {
            TenantId = tenantId,
            JobId = waitingId,
            Amount = 9999m,
            CostType = "Travel",
            Description = "Soft-deleted travel must not be required for the queue",
            IsDeleted = true
        });
        db.Set<JobLabor>().AddRange(
            new JobLabor
            {
                TenantId = tenantId,
                JobId = laborOnlyId,
                Hours = 4m,
                HourlyRate = 250m,
                WorkDate = DateTime.UtcNow.Date
            },
            new JobLabor
            {
                TenantId = tenantId,
                JobId = noWorkId,
                Hours = 8m,
                HourlyRate = 200m,
                WorkDate = DateTime.UtcNow.Date,
                IsDeleted = true
            });
        db.Set<Invoice>().Add(new Invoice
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobId = billedId,
            InvoiceNumber = "INV-COVERED",
            DocumentType = InvoiceDocumentType.Deposit,
            Status = InvoiceStatus.Sent,
            Total = 400m
        });
        await db.SaveChangesAsync();

        var queue = await new JobService(db).GetAwaitingSignOffQueueAsync();

        Assert.Contains(queue, r => r.JobId == waitingId && r.Reason == "Sign-off" && r.UnbilledResidual == 1650m);
        Assert.Contains(queue, r => r.JobId == laborOnlyId && r.Reason == "Sign-off" && r.UnbilledResidual == 1000m);
        Assert.DoesNotContain(queue, r => r.JobId == signedId);
        Assert.DoesNotContain(queue, r => r.JobId == billedId);
        Assert.DoesNotContain(queue, r => r.JobId == closedId);
        Assert.DoesNotContain(queue, r => r.JobId == noWorkId);
    }

    [Fact]
    public async Task GetAwaitingSignOffQueueAsync_LaborOnlyTake_KeepsJobNumberOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(s => s.UserName).Returns("test-user");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        await db.Database.EnsureCreatedAsync();

        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Labor Co" });

        var highId = Guid.NewGuid();
        var tieAId = Guid.NewGuid();
        var tieBId = Guid.NewGuid();
        var lowId = Guid.NewGuid();
        db.Set<Job>().AddRange(
            Job(highId, tenantId, customerId, "J-HIGH", JobStatus.InProgress, JobSignOffStatus.None, actualCost: 0m, quotedTotal: 9000m),
            Job(tieBId, tenantId, customerId, "J-B", JobStatus.InProgress, JobSignOffStatus.None, actualCost: 0m, quotedTotal: 5000m),
            Job(tieAId, tenantId, customerId, "J-A", JobStatus.InProgress, JobSignOffStatus.None, actualCost: 0m, quotedTotal: 5000m),
            Job(lowId, tenantId, customerId, "J-LOW", JobStatus.Completed, JobSignOffStatus.None, actualCost: 0m, quotedTotal: 100m));
        db.Set<JobLabor>().AddRange(
            Labor(tenantId, highId, 1m, 10m),
            Labor(tenantId, tieBId, 1m, 30m),
            Labor(tenantId, tieAId, 1m, 20m),
            Labor(tenantId, lowId, 1m, 40m));
        await db.SaveChangesAsync();

        var queue = await new JobService(db).GetAwaitingSignOffQueueAsync(take: 2);

        // SQL window is JobNumber ascending: J-A, J-B, then J-HIGH, then J-LOW.
        Assert.Equal(2, queue.Count);
        Assert.Contains(queue, r => r.JobId == tieAId);
        Assert.Contains(queue, r => r.JobId == tieBId);
        Assert.DoesNotContain(queue, r => r.JobId == highId);
        Assert.DoesNotContain(queue, r => r.JobId == lowId);
    }

    private static JobLabor Labor(Guid tenantId, Guid jobId, decimal hours, decimal rate) =>
        new()
        {
            TenantId = tenantId,
            JobId = jobId,
            Hours = hours,
            HourlyRate = rate,
            WorkDate = DateTime.UtcNow.Date
        };

    private static Job Job(
        Guid id,
        Guid tenantId,
        Guid customerId,
        string number,
        JobStatus status,
        JobSignOffStatus signOff,
        decimal actualCost,
        decimal quotedTotal = 5000m) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = number,
            Title = number,
            Status = status,
            SignOffStatus = signOff,
            QuotedTotal = quotedTotal,
            ActualCost = actualCost
        };
}
