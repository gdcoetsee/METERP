using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Tests for Job service and costing (labor + explicit travel costs).
/// Core to contractor use-case: variance tracking with travel explicit.
/// Follows full-testing rules: entity pure methods + service behavior + counters + soft deletes.
/// </summary>
public class JobTests
{
    private AppDbContext CreateInMemoryContext(Guid? fixedTenantId = null)
    {
        var tenantProviderMock = new Mock<ITenantProvider>();
        tenantProviderMock.Setup(p => p.GetCurrentTenantId()).Returns(fixedTenantId ?? Guid.NewGuid());

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(s => s.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(s => s.UserName).Returns("test-user");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProviderMock.Object, currentUserMock.Object);
    }

    [Fact]
    public void Job_GetActualTotal_IncludesMaterialLaborAndExplicitTravel()
    {
        var job = new Job
        {
            QuotedTotal = 15000m,
            ActualCost = 0m, // base not used in Get; use JobCost entries for explicit tracking
            ActualCosts = new List<JobCost>
            {
                new JobCost { Amount = 9200m, CostType = "Material", IsDeleted = false }, // explicit material as cost
                new JobCost { Amount = 620m, CostType = "Travel", IsDeleted = false },
                new JobCost { Amount = 300m, CostType = "Other", IsDeleted = false }
            },
            Labors = new List<JobLabor>
            {
                new JobLabor { Hours = 8, HourlyRate = 195m, IsDeleted = false },
                new JobLabor { Hours = 4, HourlyRate = 210m, IsDeleted = true } // soft deleted
            }
        };

        var actual = job.GetActualTotal();
        var variance = job.GetVariance();

        Assert.Equal(9200m + 620m + 300m + 1560m, actual); // material + travel + other + active labor
        Assert.Equal(-3320m, variance); // under budget
    }

    [Fact]
    public void Job_GetActualTotal_ExcludesSoftDeletedCostsAndLabor()
    {
        var job = new Job
        {
            QuotedTotal = 5000m,
            ActualCost = 1000m,
            ActualCosts = new List<JobCost> { new JobCost { Amount = 800m, CostType = "Travel", IsDeleted = true } },
            Labors = new List<JobLabor> { new JobLabor { Hours = 5, HourlyRate = 200m, IsDeleted = true } }
        };

        var actual = job.GetActualTotal();
        var variance = job.GetVariance();

        Assert.Equal(0m, actual); // base not included in Get; only active tracked costs + labor (both soft-deleted here)
        Assert.Equal(-5000m, variance);
    }

    [Fact]
    public void Job_IsReadyToInvoice_RequiresSignOffAndOpenJob()
    {
        var ready = new Job { SignOffStatus = JobSignOffStatus.SignedOff, Status = JobStatus.Completed };
        var notSigned = new Job { SignOffStatus = JobSignOffStatus.Pending, Status = JobStatus.Completed };
        var pendingExec = new Job { SignOffStatus = JobSignOffStatus.PendingExecutive, Status = JobStatus.Completed };
        var invoicedLegacy = new Job { SignOffStatus = JobSignOffStatus.SignedOff, Status = JobStatus.Invoiced };
        var closed = new Job { SignOffStatus = JobSignOffStatus.SignedOff, Status = JobStatus.Closed };
        var cancelled = new Job { SignOffStatus = JobSignOffStatus.SignedOff, Status = JobStatus.Cancelled };

        Assert.True(ready.IsReadyToInvoice());
        Assert.False(notSigned.IsReadyToInvoice());
        Assert.False(pendingExec.IsReadyToInvoice());
        Assert.True(invoicedLegacy.IsReadyToInvoice());
        Assert.False(closed.IsReadyToInvoice());
        Assert.False(cancelled.IsReadyToInvoice());
    }

    [Fact]
    public async Task JobService_AdvanceWorkSignOff_DualChain()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new JobService(db, audit: audit.Object);
        var jobId = await SeedJobAsync(db, service, tenantId);
        var manager = Guid.NewGuid();
        var exec = Guid.NewGuid();

        Assert.True(await service.AdvanceWorkSignOffAsync(jobId, manager));
        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(JobSignOffStatus.PendingManager, job.SignOffStatus);

        Assert.True(await service.AdvanceWorkSignOffAsync(jobId, manager));
        job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(JobSignOffStatus.PendingExecutive, job.SignOffStatus);
        Assert.Equal(manager, job.ManagerSignedOffByUserId);

        Assert.True(await service.AdvanceWorkSignOffAsync(jobId, exec));
        job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(JobSignOffStatus.SignedOff, job.SignOffStatus);
        Assert.Equal(exec, job.SignedOffByUserId);
        Assert.True(job.IsReadyToInvoice());
    }

    [Fact]
    public async Task JobService_CancelAsync_BlocksOperations()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        Assert.True(await service.CancelAsync(jobId, Guid.NewGuid(), "Client cancelled scope"));
        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.False(job.IsOpenForOperations());
        Assert.Contains("Client cancelled", job.CancellationReason);

        await Assert.ThrowsAsync<JobClosedException>(() => service.AddCostAsync(new JobCost
        {
            JobId = jobId,
            Description = "Blocked",
            Amount = 10m,
            CostType = "Other",
            CostDate = DateTime.UtcNow
        }));
    }

    [Fact]
    public async Task JobService_CloseAsync_ReturnsFalseWhenJobSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.IsDeleted = true;
        await db.SaveChangesAsync();

        Assert.False(await service.CloseAsync(jobId, Guid.NewGuid(), "Nope"));
    }

    [Fact]
    public async Task JobService_ReopenAsync_ReturnsFalseWhenJobSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        Assert.True(await service.CloseAsync(jobId, Guid.NewGuid(), "Done"));

        var job = await db.Set<Job>().IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
        job.IsDeleted = true;
        await db.SaveChangesAsync();

        Assert.False(await service.ReopenAsync(jobId, Guid.NewGuid(), "Need more work"));
    }

    [Fact]
    public async Task JobService_CancelAsync_ReturnsFalseWhenJobSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.IsDeleted = true;
        await db.SaveChangesAsync();

        Assert.False(await service.CancelAsync(jobId, Guid.NewGuid(), "Cannot cancel deleted"));
    }

    [Fact]
    public async Task JobService_AddCostAsync_ThrowsWhenJobSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.IsDeleted = true;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddCostAsync(new JobCost
        {
            JobId = jobId,
            Description = "Blocked",
            Amount = 10m,
            CostType = "Other",
            CostDate = DateTime.UtcNow
        }));
        Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_ThrowsWhenJobSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.IsDeleted = true;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddLaborAsync(new JobLabor
        {
            JobId = jobId,
            Hours = 2m,
            HourlyRate = 100m,
            Technician = "Tech",
            WorkDate = DateTime.UtcNow.Date
        }));
        Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Job_GetProgressPercent_MapsStatus()
    {
        Assert.Equal(50, new Job { Status = JobStatus.InProgress }.GetProgressPercent());
        Assert.Equal(90, new Job { Status = JobStatus.Invoiced }.GetProgressPercent());
        Assert.Equal(100, new Job { Status = JobStatus.Closed }.GetProgressPercent());
    }

    [Fact]
    public void Job_GetMarginPercent_ReturnsZero_WhenQuotedTotalZero()
    {
        var job = new Job
        {
            QuotedTotal = 0m,
            ActualCosts = new List<JobCost> { new JobCost { Amount = 500m, CostType = "Travel", IsDeleted = false } },
            Labors = new List<JobLabor> { new JobLabor { Hours = 4, HourlyRate = 100m, IsDeleted = false } }
        };

        Assert.Equal(0m, job.GetMarginPercent());
    }

    [Fact]
    public async Task JobService_CreateAsync_IncrementsTenantJobCounter()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var tenantServiceMock = new Mock<ITenantService>();
        tenantServiceMock.Setup(t => t.IncrementJobCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                         .Returns(Task.CompletedTask);

        var service = new JobService(db, tenantServiceMock.Object);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Counter Co" });
        await db.SaveChangesAsync();

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = "",
            Title = "Test Job with Travel",
            QuotedTotal = 10000m
        };

        var id = await service.CreateAsync(job);

        tenantServiceMock.Verify(t => t.IncrementJobCountAsync(tenantId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task JobService_AddCostAsync_TravelCost_UpdatesAndExposesViaGetActualTotal()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            QuotedTotal = 10000m,
            ActualCost = 0m
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db, null);

        var travelCost = new JobCost
        {
            JobId = job.Id,
            Description = "Site travel",
            Amount = 1500m,
            CostType = "Travel",
            IsDeleted = false
        };

        await service.AddCostAsync(travelCost);

        var reloaded = await db.Set<Job>()
            .Include(j => j.ActualCosts)
            .FirstAsync(j => j.Id == job.Id);

        Assert.Equal(1500m, reloaded.GetActualTotal()); // base 0 + travel
        Assert.Equal("Travel", reloaded.ActualCosts.First().CostType);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_And_Delete_ExcludesFromActualTotal()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            QuotedTotal = 5000m,
            ActualCost = 0m
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db, null);

        var labor = new JobLabor
        {
            JobId = job.Id,
            Hours = 10,
            HourlyRate = 200m,
            Description = "Technician time",
            IsDeleted = false
        };

        await service.AddLaborAsync(labor);

        var reloaded = await db.Set<Job>()
            .Include(j => j.Labors)
            .FirstAsync(j => j.Id == job.Id);

        Assert.Equal(2000m, reloaded.GetActualTotal());

        // Now soft delete the labor
        await service.DeleteLaborAsync(labor.Id);

        var afterDelete = await db.Set<Job>()
            .Include(j => j.Labors)
            .FirstAsync(j => j.Id == job.Id);

        Assert.Equal(0m, afterDelete.GetActualTotal());
    }

    [Fact]
    public async Task JobService_AddLaborAsync_LinksEmployee_DefaultsTechnicianAndRate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var employeeId = Guid.NewGuid();
        db.Set<Employee>().Add(new Employee
        {
            Id = employeeId,
            TenantId = tenantId,
            FirstName = "Thabo",
            LastName = "Mokoena",
            DefaultHourlyRate = 195m,
            IsActive = true
        });

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            QuotedTotal = 5000m
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db, null);
        await service.AddLaborAsync(new JobLabor
        {
            JobId = job.Id,
            EmployeeId = employeeId,
            Hours = 6
        });

        var labor = await db.Set<JobLabor>().FirstAsync(l => l.JobId == job.Id);
        Assert.Equal(employeeId, labor.EmployeeId);
        Assert.Equal("Thabo Mokoena", labor.Technician);
        Assert.Equal(195m, labor.HourlyRate);
        Assert.Equal(1170m, labor.TotalCost);
    }

    [Fact]
    public async Task JobService_UpdateAsync_ThrowsWhenScheduledStartTooFarFuture()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Sched Co" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);
        var id = await service.CreateAsync(new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            Title = "Open job",
            Status = JobStatus.Scheduled
        });
        var job = await service.GetByIdAsync(id);
        job!.ScheduledStart = DateTime.UtcNow.Date.AddYears(3);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(job));
        Assert.Contains("2 years", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_CreateAsync_ThrowsWhenNotesTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Job
            {
                CustomerId = customerId,
                Title = "Install",
                Notes = new string('N', 2001)
            }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_CreateAsync_AcceptsNotesAt2000Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C" });
        await db.SaveChangesAsync();

        var id = await service.CreateAsync(new Job
        {
            CustomerId = customerId,
            Title = "Install",
            Notes = new string('N', 2000)
        });
        var saved = await db.Set<Job>().FirstAsync(j => j.Id == id);
        Assert.Equal(2000, saved.Notes!.Length);
    }

    [Fact]
    public async Task JobService_CreateAsync_AcceptsDescriptionAt2000Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C" });
        await db.SaveChangesAsync();

        var id = await service.CreateAsync(new Job
        {
            CustomerId = customerId,
            Title = "Install",
            Description = new string('D', 2000)
        });
        var saved = await db.Set<Job>().FirstAsync(j => j.Id == id);
        Assert.Equal(2000, saved.Description!.Length);
    }

    [Fact]
    public async Task JobService_CreateAsync_ThrowsWhenTitleTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "T Co" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = new string('J', 201),
                QuotedTotal = 100m
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_CreateAsync_AcceptsTitleAt200Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "T Co" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var id = await service.CreateAsync(new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            Title = new string('J', 200),
            QuotedTotal = 100m
        });
        var saved = await db.Set<Job>().FirstAsync(j => j.Id == id);
        Assert.Equal(200, saved.Title.Length);
    }

    [Fact]
    public async Task JobService_CreateAsync_ThrowsWhenQuotedTotalTooHigh()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Big Co" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = "Mega",
                QuotedTotal = 100_000_001m
            }));
        Assert.Contains("100,000,000", ex.Message);
    }

    [Fact]
    public async Task JobService_CreateAsync_ThrowsWhenJobNumberTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Job
            {
                CustomerId = customerId,
                Title = "Install",
                JobNumber = new string('J', 51)
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_CreateAsync_ThrowsWhenJobNumberDuplicate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Dup Job Co" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        await service.CreateAsync(new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobNumber = "J-DUP-1",
            Title = "First"
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                JobNumber = "J-DUP-1",
                Title = "Second"
            }));
        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_RejectsHoursOver24()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "L", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddLaborAsync(new JobLabor { JobId = job.Id, Hours = 25, HourlyRate = 100m }));
        Assert.Contains("24", ex.Message);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_RejectsHourlyRateTooHigh()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "L", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddLaborAsync(new JobLabor { JobId = job.Id, Hours = 8, HourlyRate = 50_001m }));
        Assert.Contains("50,000", ex.Message);
    }

    [Fact]
    public async Task JobService_AddCostAsync_RejectsAmountTooHigh()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "C", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddCostAsync(new JobCost
            {
                JobId = job.Id,
                Amount = 10_000_001m,
                Description = "Mega cost"
            }));
        Assert.Contains("10,000,000", ex.Message);
    }

    [Fact]
    public async Task JobService_AddCostAsync_RejectsDescriptionTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "C", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddCostAsync(new JobCost
            {
                JobId = job.Id,
                Amount = 10m,
                Description = new string('D', 501)
            }));
        Assert.Contains("500 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_AddCostAsync_AcceptsDescriptionAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "C", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var costId = await service.AddCostAsync(new JobCost
        {
            JobId = job.Id,
            Amount = 10m,
            Description = new string('D', 500)
        });
        var cost = await db.Set<JobCost>().FirstAsync(c => c.Id == costId);
        Assert.Equal(500, cost.Description.Length);
    }

    [Fact]
    public async Task JobService_AddCostAsync_RejectsCostTypeTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "C", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddCostAsync(new JobCost
            {
                JobId = job.Id,
                Amount = 10m,
                Description = "Ok",
                CostType = new string('T', 51)
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_AddCostAsync_AcceptsCostTypeAt50Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "C", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var costId = await service.AddCostAsync(new JobCost
        {
            JobId = job.Id,
            Amount = 10m,
            Description = "Ok",
            CostType = new string('T', 50)
        });
        var cost = await db.Set<JobCost>().FirstAsync(c => c.Id == costId);
        Assert.Equal(50, cost.CostType!.Length);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_RejectsDescriptionTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "L", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddLaborAsync(new JobLabor
            {
                JobId = job.Id,
                Hours = 4,
                HourlyRate = 100m,
                Description = new string('L', 501)
            }));
        Assert.Contains("500 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_AcceptsDescriptionAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "L", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var laborId = await service.AddLaborAsync(new JobLabor
        {
            JobId = job.Id,
            Hours = 4,
            HourlyRate = 100m,
            Description = new string('L', 500)
        });
        var labor = await db.Set<JobLabor>().FirstAsync(l => l.Id == laborId);
        Assert.Equal(500, labor.Description!.Length);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_RejectsTechnicianTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "L", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddLaborAsync(new JobLabor
            {
                JobId = job.Id,
                Hours = 4,
                HourlyRate = 100m,
                Technician = new string('T', 201)
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_AcceptsTechnicianAt200Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "L", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var laborId = await service.AddLaborAsync(new JobLabor
        {
            JobId = job.Id,
            Hours = 4,
            HourlyRate = 100m,
            Technician = new string('T', 200)
        });
        var labor = await db.Set<JobLabor>().FirstAsync(l => l.Id == laborId);
        Assert.Equal(200, labor.Technician!.Length);
    }

    [Fact]
    public async Task JobService_CreateAsync_ThrowsWhenDescriptionTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Job
            {
                CustomerId = customerId,
                Title = "Install",
                Description = new string('D', 2001)
            }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_CreateAsync_ThrowsWhenNotesFieldTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Job
            {
                CustomerId = customerId,
                Title = "Install",
                Notes = new string('N', 2001)
            }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_RejectsWorkDateTooFarFuture()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "L", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddLaborAsync(new JobLabor
            {
                JobId = job.Id,
                Hours = 8,
                HourlyRate = 100m,
                WorkDate = DateTime.UtcNow.Date.AddDays(7)
            }));
        Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_RejectsInactiveEmployee()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var empId = Guid.NewGuid();
        db.Set<Employee>().Add(new Employee
        {
            Id = empId,
            TenantId = tenantId,
            EmployeeNumber = "E-INACT",
            FirstName = "Old",
            LastName = "Hand",
            IsActive = false
        });
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "L", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddLaborAsync(new JobLabor
            {
                JobId = job.Id,
                EmployeeId = empId,
                Hours = 4,
                HourlyRate = 100m
            }));
        Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_AddCostAsync_RejectsFutureCostDate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var job = new Job { TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "C", Status = JobStatus.InProgress };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        var service = new JobService(db, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddCostAsync(new JobCost
            {
                JobId = job.Id,
                Amount = 100m,
                Description = "Future material",
                CostDate = DateTime.UtcNow.Date.AddDays(14)
            }));
        Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_AddCost_And_Labor_TravelExplicit_InVariance()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            QuotedTotal = 10000m,
            ActualCost = 3000m // some base material
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db, null);

        // Add explicit travel cost
        await service.AddCostAsync(new JobCost { JobId = job.Id, Amount = 1200m, CostType = "Travel" });

        // Add labor
        await service.AddLaborAsync(new JobLabor { JobId = job.Id, Hours = 8, HourlyRate = 150m });

        var reloaded = await db.Set<Job>()
            .Include(j => j.ActualCosts)
            .Include(j => j.Labors)
            .FirstAsync(j => j.Id == job.Id);

        var actual = reloaded.GetActualTotal();
        var variance = reloaded.GetVariance();

        // tracked costs (travel) + labor (base overwritten by service to costs sum, Get uses tracked)
        Assert.Equal(2400m, actual); // 1200 travel + 1200 labor
        Assert.Equal(-7600m, variance); // under
    }

    [Fact]
    public async Task JobService_DeleteCostAsync_SoftDeletesAndRecalculatesActualCost()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        var travelId = await service.AddCostAsync(new JobCost
        {
            JobId = jobId,
            Amount = 800m,
            CostType = "Travel"
        });
        await service.AddCostAsync(new JobCost
        {
            JobId = jobId,
            Amount = 200m,
            CostType = "Material"
        });

        var before = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(1000m, before.ActualCost);

        await service.DeleteCostAsync(travelId);

        var after = await db.Set<Job>()
            .Include(j => j.ActualCosts)
            .FirstAsync(j => j.Id == jobId);
        Assert.Equal(200m, after.ActualCost);
        Assert.Equal(200m, after.GetActualTotal());

        var deletedCost = await db.Set<JobCost>()
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == travelId);
        Assert.True(deletedCost.IsDeleted);
    }

    [Fact]
    public async Task JobService_DeleteAsync_SoftDeletesJobAndAllCosts()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        await service.AddCostAsync(new JobCost { JobId = jobId, Amount = 600m, CostType = "Travel" });
        await service.AddCostAsync(new JobCost { JobId = jobId, Amount = 400m, CostType = "Material" });

        await service.DeleteAsync(jobId);

        Assert.Null(await service.GetByIdAsync(jobId));

        var jobRow = await db.Set<Job>().IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
        Assert.True(jobRow.IsDeleted);

        var costs = await db.Set<JobCost>()
            .IgnoreQueryFilters()
            .Where(c => c.JobId == jobId)
            .ToListAsync();
        Assert.Equal(2, costs.Count);
        Assert.All(costs, c => Assert.True(c.IsDeleted));
    }

    [Fact]
    public async Task JobService_DeleteAsync_ThrowsWhenClosed()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        Assert.True(await service.CloseAsync(jobId, Guid.NewGuid(), "Done"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(jobId));
    }

    [Fact]
    public async Task JobService_DeleteAsync_ThrowsWhenInvoicesExist()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        var job = await service.GetByIdAsync(jobId);
        db.Set<Invoice>().Add(new Invoice
        {
            TenantId = tenantId,
            CustomerId = job!.CustomerId,
            JobId = jobId,
            InvoiceNumber = "INV-DEL",
            Status = InvoiceStatus.Draft
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(jobId));
        Assert.Contains("invoice", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_UpdateStatusAsync_SetsCompletedDate_WhenCompleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        await service.UpdateStatusAsync(jobId, JobStatus.Completed);

        var job = await service.GetByIdAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Completed, job!.Status);
        Assert.NotNull(job.CompletedDate);
    }

    [Fact]
    public async Task JobService_UpdateStatusAsync_SetsCompletedDate_WhenInvoiced()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        await service.UpdateStatusAsync(jobId, JobStatus.Invoiced);

        var job = await service.GetByIdAsync(jobId);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Invoiced, job!.Status);
        Assert.NotNull(job.CompletedDate);
    }

    private static async Task<Guid> SeedJobAsync(AppDbContext db, JobService service, Guid tenantId)
    {
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Job Co" });
        await db.SaveChangesAsync();

        return await service.CreateAsync(new Job
        {
            CustomerId = customerId,
            Title = "Service test job",
            QuotedTotal = 5000m
        });
    }

    private static async Task<(Guid JobId, Guid Emp1, Guid Emp2)> SeedJobWithTwoEmployeesAsync(
        AppDbContext db, JobService service, Guid tenantId)
    {
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Crew Co" });
        await db.SaveChangesAsync();

        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        db.Set<Employee>().AddRange(
            new Employee { Id = emp1, TenantId = tenantId, FirstName = "A", LastName = "One", IsActive = true },
            new Employee { Id = emp2, TenantId = tenantId, FirstName = "B", LastName = "Two", IsActive = true });
        await db.SaveChangesAsync();

        var jobId = await service.CreateAsync(new Job
        {
            CustomerId = customerId,
            Title = "Crew sync",
            QuotedTotal = 1000m
        });

        return (jobId, emp1, emp2);
    }

    [Fact]
    public async Task JobService_SetCrewAssignmentsAsync_AddsDistinctCrewMembers()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var (jobId, emp1, emp2) = await SeedJobWithTwoEmployeesAsync(db, service, tenantId);

        await service.SetCrewAssignmentsAsync(jobId, new[] { emp1, emp2, emp1, Guid.Empty });

        var reloaded = await service.GetByIdAsync(jobId);
        Assert.NotNull(reloaded);
        var crewIds = reloaded!.GetCrewEmployees().Select(e => e.Id).ToList();
        Assert.Equal(2, crewIds.Count);
        Assert.Contains(emp1, crewIds);
        Assert.Contains(emp2, crewIds);
    }

    [Fact]
    public async Task JobService_SetCrewAssignmentsAsync_SoftDeletesRemovedCrew()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var (jobId, emp1, emp2) = await SeedJobWithTwoEmployeesAsync(db, service, tenantId);

        await service.SetCrewAssignmentsAsync(jobId, new[] { emp1, emp2 });
        await service.SetCrewAssignmentsAsync(jobId, new[] { emp1 });

        var reloaded = await service.GetByIdAsync(jobId);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.GetCrewEmployees());

        var allRows = await db.Set<JobCrewAssignment>()
            .IgnoreQueryFilters()
            .Where(a => a.JobId == jobId)
            .ToListAsync();
        Assert.Equal(2, allRows.Count);
        Assert.True(allRows.Single(a => a.EmployeeId == emp2).IsDeleted);
        Assert.False(allRows.Single(a => a.EmployeeId == emp1).IsDeleted);
    }

    [Fact]
    public async Task JobService_SetCrewAssignmentsAsync_ReactivatesSoftDeletedCrew()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var (jobId, emp1, _) = await SeedJobWithTwoEmployeesAsync(db, service, tenantId);

        await service.SetCrewAssignmentsAsync(jobId, new[] { emp1 });
        await service.SetCrewAssignmentsAsync(jobId, Array.Empty<Guid>());
        await service.SetCrewAssignmentsAsync(jobId, new[] { emp1 });

        var reloaded = await service.GetByIdAsync(jobId);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.GetCrewEmployees());

        var row = await db.Set<JobCrewAssignment>()
            .IgnoreQueryFilters()
            .SingleAsync(a => a.JobId == jobId && a.EmployeeId == emp1);
        Assert.False(row.IsDeleted);
    }

    [Fact]
    public async Task JobService_SetCrewAssignmentsAsync_ThrowsWhenJobNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetCrewAssignmentsAsync(Guid.NewGuid(), new[] { Guid.NewGuid() }));
    }

    [Fact]
    public void Job_GetProgressPercent_UsesMilestoneAverageWhenPresent()
    {
        var job = new Job
        {
            Status = JobStatus.Scheduled,
            Milestones =
            {
                new JobMilestone { PercentComplete = 100, IsDeleted = false },
                new JobMilestone { PercentComplete = 40, IsDeleted = false }
            }
        };

        Assert.Equal(70, job.GetProgressPercent());
    }

    [Fact]
    public async Task JobService_AddMilestoneAndSnag_PersistAndResolve()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);

        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-M1", Title = "Test" });
        await db.SaveChangesAsync();

        var milestoneId = await service.AddMilestoneAsync(new JobMilestone
        {
            JobId = jobId,
            Title = "Commissioning",
            PercentComplete = 0,
            Status = JobMilestoneStatus.Pending
        });

        var snagId = await service.AddSnagAsync(new JobSnagItem
        {
            JobId = jobId,
            Description = "Loose gland"
        });

        var milestones = await service.GetMilestonesAsync(jobId);
        var snags = await service.GetSnagsAsync(jobId);

        Assert.Single(milestones);
        Assert.Equal("Commissioning", milestones[0].Title);
        Assert.Single(snags);
        Assert.False(snags[0].IsResolved);

        await service.ResolveSnagAsync(snagId, Guid.NewGuid());
        snags = await service.GetSnagsAsync(jobId);
        Assert.True(snags[0].IsResolved);

        await service.DeleteMilestoneAsync(milestoneId);
        milestones = await service.GetMilestonesAsync(jobId);
        Assert.Empty(milestones);
    }

    [Fact]
    public async Task JobService_DepositWithoutSignOff_LeavesJobOpen()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var jobService = new JobService(db);
        var invoiceService = new InvoiceService(db);
        var jobId = await SeedJobAsync(db, jobService, tenantId);

        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(JobSignOffStatus.None, job.SignOffStatus);

        var deposit = await invoiceService.CreateBillingDocumentAsync(jobId, InvoiceDocumentType.Deposit, 30m);
        Assert.Equal(InvoiceDocumentType.Deposit, deposit.DocumentType);

        job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.True(job.IsOpenForOperations());
        Assert.NotEqual(JobStatus.Closed, job.Status);

        await jobService.AddCostAsync(new JobCost
        {
            JobId = jobId,
            Description = "After deposit",
            Amount = 99m,
            CostType = "Travel",
            CostDate = DateTime.UtcNow
        });
        job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(99m, job.ActualCost);
    }

    [Fact]
    public async Task JobService_InvoiceWhileOpen_AllowsCostUntilExecutiveClose()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobService = new JobService(db, audit: audit.Object);
        var invoiceService = new InvoiceService(db, auditService: audit.Object);
        var jobId = await SeedJobAsync(db, jobService, tenantId);

        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.SignOffStatus = JobSignOffStatus.SignedOff;
        job.Status = JobStatus.Completed;
        await db.SaveChangesAsync();

        var invoice = await invoiceService.CreateFromJobAsync(jobId);
        Assert.NotNull(invoice);

        job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(JobStatus.Completed, job.Status);

        await jobService.AddCostAsync(new JobCost
        {
            JobId = jobId,
            Description = "Post-invoice travel",
            Amount = 450m,
            CostType = "Travel",
            CostDate = DateTime.UtcNow
        });

        job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(450m, job.ActualCost);

        var execId = Guid.NewGuid();
        Assert.True(await jobService.CloseAsync(jobId, execId, "P&L reviewed"));

        await Assert.ThrowsAsync<JobClosedException>(() => jobService.AddCostAsync(new JobCost
        {
            JobId = jobId,
            Description = "Should fail",
            Amount = 100m,
            CostType = "Other",
            CostDate = DateTime.UtcNow
        }));

        Assert.True(await jobService.ReopenAsync(jobId, execId, "Additional snag work discovered"));
        job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Null(job.ClosedAt);

        await jobService.AddCostAsync(new JobCost
        {
            JobId = jobId,
            Description = "After reopen",
            Amount = 120m,
            CostType = "Other",
            CostDate = DateTime.UtcNow
        });

        job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        Assert.Equal(570m, job.ActualCost);
    }

    [Fact]
    public async Task JobService_CloseAsync_LogsAudit()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new JobService(db, audit: audit.Object);
        var jobId = await SeedJobAsync(db, service, tenantId);

        Assert.True(await service.CloseAsync(jobId, Guid.NewGuid(), "Done"));

        audit.Verify(a => a.LogAsync("CLOSE", "Job", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JobService_SafetyIncident_AddAndClose()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);

        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-SAF", Title = "Site" });
        await db.SaveChangesAsync();

        var incidentId = await service.AddSafetyIncidentAsync(new JobSafetyIncident
        {
            JobId = jobId,
            Description = "Near miss — unsecured ladder",
            Severity = SafetyIncidentSeverity.High
        });

        var incidents = await service.GetSafetyIncidentsAsync(jobId);
        Assert.Single(incidents);
        Assert.False(incidents[0].IsClosed);

        await service.CloseSafetyIncidentAsync(incidentId, Guid.NewGuid(), "Ladder secured and toolbox talk held.");
        incidents = await service.GetSafetyIncidentsAsync(jobId);
        Assert.True(incidents[0].IsClosed);
        Assert.Contains("toolbox talk", incidents[0].CorrectiveAction);
    }

    [Fact]
    public async Task JobService_CloseAsync_RejectsNotesTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CloseAsync(jobId, Guid.NewGuid(), new string('N', 501)));
        Assert.Contains("500 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_CloseAsync_AcceptsNotesAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        Assert.True(await service.CloseAsync(jobId, Guid.NewGuid(), new string('N', 500)));
        var job = await service.GetByIdAsync(jobId);
        Assert.Equal(JobStatus.Closed, job!.Status);
    }

    [Fact]
    public async Task JobService_CancelAsync_RejectsReasonTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CancelAsync(jobId, Guid.NewGuid(), new string('C', 501)));
        Assert.Contains("500 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_CancelAsync_AcceptsReasonAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);

        Assert.True(await service.CancelAsync(jobId, Guid.NewGuid(), new string('C', 500)));
        var job = await service.GetByIdAsync(jobId);
        Assert.Equal(JobStatus.Cancelled, job!.Status);
    }

    [Fact]
    public async Task JobService_ReopenAsync_RejectsReasonTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        Assert.True(await service.CloseAsync(jobId, Guid.NewGuid(), "Done"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ReopenAsync(jobId, Guid.NewGuid(), new string('R', 501)));
        Assert.Contains("500 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_ReopenAsync_AcceptsReasonAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        Assert.True(await service.CloseAsync(jobId, Guid.NewGuid(), "Done"));

        Assert.True(await service.ReopenAsync(jobId, Guid.NewGuid(), new string('R', 500)));
        var job = await service.GetByIdAsync(jobId);
        Assert.NotEqual(JobStatus.Closed, job!.Status);
    }

    [Fact]
    public async Task JobService_AddMilestoneAsync_ThrowsWhenJobSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.IsDeleted = true;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddMilestoneAsync(new JobMilestone
            {
                JobId = jobId,
                Title = "Blocked",
                SortOrder = 1
            }));
        Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_AddSnagAsync_ThrowsWhenJobClosed()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        await service.CloseAsync(jobId, Guid.NewGuid(), "Done");

        await Assert.ThrowsAsync<JobClosedException>(() =>
            service.AddSnagAsync(new JobSnagItem
            {
                JobId = jobId,
                Description = "Snag after close"
            }));
    }

    [Fact]
    public async Task JobService_AddSafetyIncidentAsync_ThrowsWhenJobSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        var job = await db.Set<Job>().FirstAsync(j => j.Id == jobId);
        job.IsDeleted = true;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddSafetyIncidentAsync(new JobSafetyIncident
            {
                JobId = jobId,
                Description = "Near miss",
                Severity = SafetyIncidentSeverity.Low
            }));
        Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_AddSafetyIncidentAsync_AllowsClosedJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        await service.CloseAsync(jobId, Guid.NewGuid(), "Done");

        var id = await service.AddSafetyIncidentAsync(new JobSafetyIncident
        {
            JobId = jobId,
            Description = "Post-close report",
            Severity = SafetyIncidentSeverity.Low
        });
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task JobService_AddMilestoneAsync_RejectsTitleTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-MS", Title = "Site", Status = JobStatus.InProgress });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddMilestoneAsync(new JobMilestone
            {
                JobId = jobId,
                Title = new string('T', 201)
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_AddMilestoneAsync_AcceptsTitleAt200Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-MS-OK", Title = "Site", Status = JobStatus.InProgress });
        await db.SaveChangesAsync();

        var id = await service.AddMilestoneAsync(new JobMilestone
        {
            JobId = jobId,
            Title = new string('T', 200)
        });
        var saved = await db.Set<JobMilestone>().FirstAsync(m => m.Id == id);
        Assert.Equal(200, saved.Title.Length);
    }

    [Fact]
    public async Task JobService_AddSnagAsync_RejectsDescriptionTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-SN", Title = "Site", Status = JobStatus.InProgress });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddSnagAsync(new JobSnagItem
            {
                JobId = jobId,
                Description = new string('S', 2001)
            }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_AddSnagAsync_AcceptsDescriptionAt2000Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-SN-OK", Title = "Site", Status = JobStatus.InProgress });
        await db.SaveChangesAsync();

        var id = await service.AddSnagAsync(new JobSnagItem
        {
            JobId = jobId,
            Description = new string('S', 2000)
        });
        var saved = await db.Set<JobSnagItem>().FirstAsync(s => s.Id == id);
        Assert.Equal(2000, saved.Description.Length);
    }

    [Fact]
    public async Task JobService_AddSafetyIncidentAsync_RejectsDescriptionTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-SI", Title = "Site" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddSafetyIncidentAsync(new JobSafetyIncident
            {
                JobId = jobId,
                Description = new string('I', 2001),
                Severity = SafetyIncidentSeverity.Medium
            }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_AddSafetyIncidentAsync_AcceptsDescriptionAt2000Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-SI-OK", Title = "Site" });
        await db.SaveChangesAsync();

        var id = await service.AddSafetyIncidentAsync(new JobSafetyIncident
        {
            JobId = jobId,
            Description = new string('I', 2000),
            Severity = SafetyIncidentSeverity.Medium
        });
        var saved = await db.Set<JobSafetyIncident>().FirstAsync(i => i.Id == id);
        Assert.Equal(2000, saved.Description.Length);
    }

    [Fact]
    public async Task JobService_CloseSafetyIncidentAsync_AcceptsCorrectiveActionAt2000Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-SI-CA", Title = "Site" });
        await db.SaveChangesAsync();

        var id = await service.AddSafetyIncidentAsync(new JobSafetyIncident
        {
            JobId = jobId,
            Description = "Fall risk",
            Severity = SafetyIncidentSeverity.Medium
        });
        await service.CloseSafetyIncidentAsync(id, Guid.NewGuid(), new string('C', 2000));
        var saved = await db.Set<JobSafetyIncident>().FirstAsync(i => i.Id == id);
        Assert.Equal(2000, saved.CorrectiveAction!.Length);
    }

    [Fact]
    public async Task JobService_CloseSafetyIncidentAsync_RejectsCorrectiveActionTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = Guid.NewGuid();
        db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, JobNumber = "J-CA", Title = "Site" });
        await db.SaveChangesAsync();

        var incidentId = await service.AddSafetyIncidentAsync(new JobSafetyIncident
        {
            JobId = jobId,
            Description = "Minor cut",
            Severity = SafetyIncidentSeverity.Low
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSafetyIncidentAsync(incidentId, Guid.NewGuid(), new string('C', 2001)));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task JobService_UpdateAsync_ThrowsWhenClosed()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        Assert.True(await service.CloseAsync(jobId, Guid.NewGuid(), "Closed"));

        var job = await service.GetByIdAsync(jobId);
        Assert.NotNull(job);
        job!.Title = "Cannot edit";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(job));
        Assert.Contains("Closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobService_UpdateAsync_PreservesJobNumberAndRejectsStatusChange()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new JobService(db);
        var jobId = await SeedJobAsync(db, service, tenantId);
        var job = await service.GetByIdAsync(jobId);
        Assert.NotNull(job);
        var number = job!.JobNumber;

        job.Title = "  Retitled work  ";
        job.JobNumber = "HACKED";
        await service.UpdateAsync(job);

        var reloaded = await service.GetByIdAsync(jobId);
        Assert.Equal("Retitled work", reloaded!.Title);
        Assert.Equal(number, reloaded.JobNumber);

        reloaded.Status = JobStatus.Completed;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(reloaded));
    }
}
