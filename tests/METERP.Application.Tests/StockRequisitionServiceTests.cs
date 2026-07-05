using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class StockRequisitionServiceTests
{
    private static (StockRequisitionService Service, AppDbContext Db, Guid TenantId, Mock<IInventoryService> Inventory) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var inventory = new Mock<IInventoryService>();
        inventory.Setup(i => i.RecordStockTransactionAsync(
                It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<StockTransactionType>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"req-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        var service = new StockRequisitionService(db, inventory.Object, audit: audit.Object);
        return (service, db, tenantId, inventory);
    }

    private static async Task<(Job Job, InventoryItem Item)> SeedJobAndItemAsync(AppDbContext db, Guid tenantId, decimal onHand = 10m)
    {
        var customer = new Customer { TenantId = tenantId, Name = "Req Co" };
        db.Set<Customer>().Add(customer);
        var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "Install", QuotedTotal = 5000m };
        db.Set<Job>().Add(job);
        var item = new InventoryItem
        {
            TenantId = tenantId,
            Sku = "REQ-001",
            Name = "Cable",
            QuantityOnHand = onHand,
            UnitCost = 100m,
            IsActive = true
        };
        db.Set<InventoryItem>().Add(item);
        await db.SaveChangesAsync();
        return (job, item);
    }

    [Fact]
    public async Task SubmitAsync_SetsPendingManagerAndAssignsNumber()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId);
            var requesterId = Guid.NewGuid();

            var id = await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = requesterId,
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 2 }]
            });

            var saved = await db.Set<StockRequisition>().Include(r => r.Lines).FirstAsync(r => r.Id == id);
            Assert.Equal(RequisitionStatus.PendingManager, saved.Status);
            Assert.False(string.IsNullOrWhiteSpace(saved.RequisitionNumber));
            Assert.Single(saved.Lines);
        }
    }

    [Fact]
    public async Task ApprovalChain_ReservesStockAndIssuesToJob()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId, onHand: 10m);
            var managerId = Guid.NewGuid();
            var executiveId = Guid.NewGuid();
            var storesId = Guid.NewGuid();

            var id = await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 3 }]
            });

            Assert.True(await service.ApproveManagerAsync(id, managerId));
            Assert.True(await service.ApproveExecutiveAsync(id, executiveId));

            var approved = await db.Set<StockRequisition>().Include(r => r.Lines).FirstAsync(r => r.Id == id);
            Assert.Equal(RequisitionStatus.Approved, approved.Status);
            Assert.Equal(3m, approved.Lines.First().QuantityReserved);

            var itemAfterReserve = await db.Set<InventoryItem>().FirstAsync(i => i.Id == item.Id);
            Assert.Equal(3m, itemAfterReserve.QuantityReserved);

            Assert.True(await service.IssueAsync(id, storesId));

            inventory.Verify(i => i.RecordStockTransactionAsync(
                item.Id, -3m, StockTransactionType.Issue,
                approved.RequisitionNumber, job.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

            var issued = await db.Set<StockRequisition>().FirstAsync(r => r.Id == id);
            Assert.Equal(RequisitionStatus.Issued, issued.Status);

            var jobCost = await db.Set<JobCost>().FirstOrDefaultAsync(c => c.JobId == job.Id);
            Assert.NotNull(jobCost);
            Assert.Equal("Material", jobCost.CostType);
            Assert.Equal(300m, jobCost.Amount);
        }
    }

    [Fact]
    public async Task ApproveExecutiveAsync_NoStock_SetsAwaitingProcurement()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId, onHand: 0m);

            var id = await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 5 }]
            });

            Assert.True(await service.ApproveManagerAsync(id, Guid.NewGuid()));
            Assert.True(await service.ApproveExecutiveAsync(id, Guid.NewGuid()));

            var saved = await db.Set<StockRequisition>().Include(r => r.Lines).FirstAsync(r => r.Id == id);
            Assert.Equal(RequisitionStatus.AwaitingProcurement, saved.Status);
            Assert.Equal(0m, saved.Lines.First().QuantityReserved);
        }
    }

    [Fact]
    public async Task RejectAsync_ReleasesReservations()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId, onHand: 10m);

            var id = await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 4 }]
            });

            await service.ApproveManagerAsync(id, Guid.NewGuid());
            await service.ApproveExecutiveAsync(id, Guid.NewGuid());

            Assert.True(await service.RejectAsync(id, Guid.NewGuid(), "No longer needed"));

            var saved = await db.Set<StockRequisition>().FirstAsync(r => r.Id == id);
            Assert.Equal(RequisitionStatus.Rejected, saved.Status);

            var itemAfter = await db.Set<InventoryItem>().FirstAsync(i => i.Id == item.Id);
            Assert.Equal(0m, itemAfter.QuantityReserved);
        }
    }

    [Fact]
    public async Task SubmitAsync_ThrowsWhenJobMissing()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (_, item) = await SeedJobAndItemAsync(db, tenantId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = Guid.Empty,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 1 }]
            }));
        }
    }

    [Fact]
    public async Task SubmitAsync_ThrowsWhenNoLines()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, _) = await SeedJobAndItemAsync(db, tenantId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = []
            }));
        }
    }

    [Fact]
    public async Task RejectAsync_ReturnsFalse_WhenAlreadyRejected()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId);

            var id = await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 2 }]
            });

            Assert.True(await service.RejectAsync(id, Guid.NewGuid(), "Cancelled"));
            Assert.False(await service.RejectAsync(id, Guid.NewGuid(), "Again"));
        }
    }

    [Fact]
    public async Task GetByJobIdAsync_ReturnsRequisitionsForJob()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId);
            var otherCustomer = new Customer { TenantId = tenantId, Name = "Other Co" };
            db.Set<Customer>().Add(otherCustomer);
            var otherJob = new Job { TenantId = tenantId, CustomerId = otherCustomer.Id, Title = "Other", QuotedTotal = 1000m };
            db.Set<Job>().Add(otherJob);
            await db.SaveChangesAsync();

            await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 1 }]
            });
            await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = otherJob.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 2 }]
            });

            var forJob = await service.GetByJobIdAsync(job.Id);
            Assert.Single(forJob);
            Assert.Equal(job.Id, forJob[0].JobId);
        }
    }

    [Fact]
    public async Task ApproveExecutiveAsync_ReturnsFalse_WhenWrongStage()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId);

            var id = await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 1 }]
            });

            Assert.False(await service.ApproveExecutiveAsync(id, Guid.NewGuid()));
            Assert.True(await service.ApproveManagerAsync(id, Guid.NewGuid()));
            Assert.True(await service.ApproveExecutiveAsync(id, Guid.NewGuid()));
            Assert.False(await service.ApproveExecutiveAsync(id, Guid.NewGuid()));

            var saved = await db.Set<StockRequisition>().FirstAsync(r => r.Id == id);
            Assert.Equal(RequisitionStatus.Approved, saved.Status);
        }
    }

    [Fact]
    public async Task ApproveManagerAsync_ReturnsFalse_WhenWrongStage()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId);

            var id = await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 1 }]
            });

            Assert.True(await service.ApproveManagerAsync(id, Guid.NewGuid()));
            Assert.False(await service.ApproveManagerAsync(id, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task IssueAsync_ReturnsFalse_WhenNotApproved()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId);

            var id = await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 1 }]
            });

            Assert.False(await service.IssueAsync(id, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySearch()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId);

            await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Notes = "Alpha panel cable run",
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 1 }]
            });
            await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Notes = "Beta transformer oil",
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 2 }]
            });

            var filtered = await service.GetAllAsync(search: "Alpha");
            var misses = await service.GetAllAsync(search: "nonexistent-xyz");

            Assert.Single(filtered);
            Assert.Contains("Alpha", filtered[0].Notes, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(misses);
        }
    }

    [Fact]
    public async Task FulfillAfterPoReceiptAsync_ReturnsFalse_WhenNoLinkedRequisition()
    {
        var (service, db, _, _) = Create();
        await using (db)
        {
            Assert.False(await service.FulfillAfterPoReceiptAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task GetPendingApprovalsAsync_ReturnsManagerAndExecutiveQueues()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var (job, item) = await SeedJobAndItemAsync(db, tenantId);

            await service.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 1 }]
            });

            var pending = await service.GetPendingApprovalsAsync();
            Assert.Single(pending);
            Assert.Equal(RequisitionStatus.PendingManager, pending[0].Status);
        }
    }
}