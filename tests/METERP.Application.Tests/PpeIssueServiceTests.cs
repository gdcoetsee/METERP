using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class PpeIssueServiceTests
{
    private static (PpeIssueService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ppe-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        return (new PpeIssueService(db), db, tenantId);
    }

    [Fact]
    public async Task RecordFromRequisitionIssueAsync_SkipsNonPpeRequisitions()
    {
        var (service, db, tenantId) = Create();
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
        var (service, db, tenantId) = Create();
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
            Assert.Equal(requesterId, issues[0].RequestedByUserId);
            Assert.Contains("REQ-PPE-01", issues[0].Notes);
        }
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsMostRecentFirst()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "PPE Co" };
            db.Set<Customer>().Add(customer);
            var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "PPE job", QuotedTotal = 500m };
            db.Set<Job>().Add(job);
            var item = new InventoryItem { TenantId = tenantId, Sku = "PPE-1", Name = "Vest", IsActive = true };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            db.Set<EmployeePpeIssue>().AddRange(
                new EmployeePpeIssue
                {
                    TenantId = tenantId,
                    JobId = job.Id,
                    RequestedByUserId = Guid.NewGuid(),
                    InventoryItemId = item.Id,
                    Quantity = 1,
                    IssuedAt = DateTime.UtcNow.AddDays(-2)
                },
                new EmployeePpeIssue
                {
                    TenantId = tenantId,
                    JobId = job.Id,
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