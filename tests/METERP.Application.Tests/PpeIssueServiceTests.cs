using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class PpeIssueServiceTests
{
    private static (PpeIssueService Service, AppDbContext Db, Guid TenantId, InventoryService Inventory) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ppe-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        var inventory = new InventoryService(db);
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return (new PpeIssueService(db, inventory, audit.Object), db, tenantId, inventory);
    }

    [Fact]
    public async Task RecordFromRequisitionIssueAsync_SkipsNonPpeRequisitions()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var requisition = new StockRequisition
            {
                TenantId = tenantId,
                JobId = Guid.NewGuid(),
                RequestedByUserId = Guid.NewGuid(),
                IsPpe = false,
                RequisitionNumber = "REQ-001"
            };
            db.Set<StockRequisition>().Add(requisition);
            await db.SaveChangesAsync();

            await service.RecordFromRequisitionIssueAsync(requisition);

            Assert.Empty(await db.Set<EmployeePpeIssue>().ToListAsync());
        }
    }

    [Fact]
    public async Task RecordFromRequisitionIssueAsync_CreatesIssuesForIssuedPpeLines()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Mine Co" };
            db.Set<Customer>().Add(customer);

            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E1",
                FirstName = "Field",
                LastName = "Tech",
                HireDate = DateTime.UtcNow.AddYears(-1)
            };
            db.Set<Employee>().Add(employee);

            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = "Site work",
                QuotedTotal = 1000m,
                AssignedEmployeeId = employee.Id
            };
            db.Set<Job>().Add(job);

            var helmet = new InventoryItem { TenantId = tenantId, Sku = "PPE-H", Name = "Helmet", IsActive = true };
            var gloves = new InventoryItem { TenantId = tenantId, Sku = "PPE-G", Name = "Gloves", IsActive = true };
            db.Set<InventoryItem>().AddRange(helmet, gloves);
            await db.SaveChangesAsync();

            var requesterId = Guid.NewGuid();
            var requisition = new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = requesterId,
                IsPpe = true,
                RequisitionNumber = "REQ-PPE-01",
                Lines =
                [
                    new StockRequisitionLine { InventoryItemId = helmet.Id, QuantityRequested = 1, QuantityIssued = 1 },
                    new StockRequisitionLine { InventoryItemId = gloves.Id, QuantityRequested = 2, QuantityIssued = 0 }
                ]
            };
            db.Set<StockRequisition>().Add(requisition);
            await db.SaveChangesAsync();

            await service.RecordFromRequisitionIssueAsync(requisition);

            var issues = await db.Set<EmployeePpeIssue>().ToListAsync();
            Assert.Single(issues);
            Assert.Equal(employee.Id, issues[0].EmployeeId);
            Assert.Equal(helmet.Id, issues[0].InventoryItemId);
            Assert.Equal(1m, issues[0].Quantity);
            Assert.Equal(job.Id, issues[0].JobId);
            Assert.Equal(requesterId, issues[0].RequestedByUserId);
            Assert.Contains("REQ-PPE-01", issues[0].Notes);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_WithoutJob_DecrementsStockAndRegisters()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "EMP-9",
                FirstName = "Sam",
                LastName = "Store",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var item = new InventoryItem
            {
                TenantId = tenantId,
                Sku = "PPE-BOOT",
                Name = "Safety boots",
                QuantityOnHand = 10,
                UnitCost = 500m,
                IsActive = true
            };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            var issuer = Guid.NewGuid();
            var issueId = await service.IssueToEmployeeAsync(employee.Id, item.Id, 2m, issuer, jobId: null, notes: "New hire kit");

            var issue = await db.Set<EmployeePpeIssue>().Include(p => p.Employee).FirstAsync(p => p.Id == issueId);
            Assert.Equal(employee.Id, issue.EmployeeId);
            Assert.Null(issue.JobId);
            Assert.Equal(2m, issue.Quantity);
            Assert.Equal(issuer, issue.RequestedByUserId);

            var stock = await db.Set<InventoryItem>().FirstAsync(i => i.Id == item.Id);
            Assert.Equal(8m, stock.QuantityOnHand);

            var tx = await db.Set<StockTransaction>().FirstAsync(t => t.InventoryItemId == item.Id);
            Assert.Equal(StockTransactionType.Issue, tx.Type);
            Assert.Equal(-2m, tx.Quantity);
            Assert.Null(tx.JobId);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_InsufficientStock_Throws()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "EMP-1",
                FirstName = "A",
                LastName = "B",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var item = new InventoryItem
            {
                TenantId = tenantId,
                Sku = "PPE-X",
                Name = "Hard hat",
                QuantityOnHand = 1,
                IsActive = true
            };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueToEmployeeAsync(employee.Id, item.Id, 5m, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByEmployee()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var emp1 = new Employee { TenantId = tenantId, EmployeeNumber = "1", FirstName = "One", LastName = "A", IsActive = true };
            var emp2 = new Employee { TenantId = tenantId, EmployeeNumber = "2", FirstName = "Two", LastName = "B", IsActive = true };
            db.Set<Employee>().AddRange(emp1, emp2);
            var item = new InventoryItem { TenantId = tenantId, Sku = "PPE-1", Name = "Vest", IsActive = true, QuantityOnHand = 50 };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            await service.IssueToEmployeeAsync(emp1.Id, item.Id, 1m, Guid.NewGuid());
            await service.IssueToEmployeeAsync(emp2.Id, item.Id, 1m, Guid.NewGuid());

            var forEmp1 = await service.GetHistoryAsync(emp1.Id);
            Assert.Single(forEmp1);
            Assert.Equal(emp1.Id, forEmp1[0].EmployeeId);
        }
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsMostRecentFirst()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var item = new InventoryItem { TenantId = tenantId, Sku = "PPE-1", Name = "Vest", IsActive = true };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            db.Set<EmployeePpeIssue>().AddRange(
                new EmployeePpeIssue
                {
                    TenantId = tenantId,
                    JobId = null,
                    RequestedByUserId = Guid.NewGuid(),
                    InventoryItemId = item.Id,
                    Quantity = 1,
                    IssuedAt = DateTime.UtcNow.AddDays(-2)
                },
                new EmployeePpeIssue
                {
                    TenantId = tenantId,
                    JobId = null,
                    RequestedByUserId = Guid.NewGuid(),
                    InventoryItemId = item.Id,
                    Quantity = 1,
                    IssuedAt = DateTime.UtcNow.AddHours(-1)
                });
            await db.SaveChangesAsync();

            var history = await service.GetHistoryAsync();

            Assert.Equal(2, history.Count);
            Assert.True(history[0].IssuedAt > history[1].IssuedAt);
        }
    }
}
