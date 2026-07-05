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
/// Phase 4: tenant isolation for spine-feeding supporting modules (global query filters).
/// </summary>
public class SupportingModuleTenantIsolationTests
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
    public async Task CustomerService_GetAllAsync_ReturnsOnlyCurrentTenantCustomers()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed each tenant from its own context so query filters bind to the correct tenant.
        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Customer>().Add(new Customer { TenantId = tenantA, Name = "Acme Tenant A" });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Customer>().Add(new Customer { TenantId = tenantB, Name = "Beta Tenant B" });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var resultsA = await new CustomerService(dbA).GetAllAsync();
        Assert.Single(resultsA);
        Assert.Equal("Acme Tenant A", resultsA[0].Name);

        await using var dbB = CreateContext(dbName, tenantB);
        var resultsB = await new CustomerService(dbB).GetAllAsync();
        Assert.Single(resultsB);
        Assert.Equal("Beta Tenant B", resultsB[0].Name);
    }

    [Fact]
    public async Task OpportunityService_GetAllAsync_ReturnsOnlyCurrentTenantOpportunities()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Opportunity>().Add(new Opportunity { TenantId = tenantA, Title = "Opp A", CustomerName = "A", Value = 1000m });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Opportunity>().Add(new Opportunity { TenantId = tenantB, Title = "Opp B", CustomerName = "B", Value = 2000m });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var resultsA = await new OpportunityService(dbA).GetAllAsync();
        Assert.Single(resultsA);
        Assert.Equal("Opp A", resultsA[0].Title);

        await using var dbB = CreateContext(dbName, tenantB);
        var resultsB = await new OpportunityService(dbB).GetAllAsync();
        Assert.Single(resultsB);
        Assert.Equal("Opp B", resultsB[0].Title);
    }

    [Fact]
    public async Task InventoryService_GetAllItemsAsync_ReturnsOnlyCurrentTenantItems()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<InventoryItem>().Add(new InventoryItem { TenantId = tenantA, Sku = "A-1", Name = "Tenant A fuse", QuantityOnHand = 5, ReorderLevel = 1 });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<InventoryItem>().Add(new InventoryItem { TenantId = tenantB, Sku = "B-1", Name = "Tenant B fuse", QuantityOnHand = 5, ReorderLevel = 1 });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var itemsA = await new InventoryService(dbA).GetAllItemsAsync();
        Assert.Single(itemsA);
        Assert.Equal("A-1", itemsA[0].Sku);

        await using var dbB = CreateContext(dbName, tenantB);
        var itemsB = await new InventoryService(dbB).GetAllItemsAsync();
        Assert.Single(itemsB);
        Assert.Equal("B-1", itemsB[0].Sku);
    }

    [Fact]
    public async Task SupplierService_GetAllAsync_ReturnsOnlyCurrentTenantSuppliers()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Supplier>().Add(new Supplier { TenantId = tenantA, Name = "Supplier A", IsActive = true });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Supplier>().Add(new Supplier { TenantId = tenantB, Name = "Supplier B", IsActive = true });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var suppliersA = await new SupplierService(dbA).GetAllAsync();
        Assert.Single(suppliersA);
        Assert.Equal("Supplier A", suppliersA[0].Name);

        await using var dbB = CreateContext(dbName, tenantB);
        var suppliersB = await new SupplierService(dbB).GetAllAsync();
        Assert.Single(suppliersB);
        Assert.Equal("Supplier B", suppliersB[0].Name);
    }

    [Fact]
    public async Task AssetService_GetByIdAsync_DoesNotReturnOtherTenantAsset()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid assetBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Tenant B customer" };
            seedB.Set<Customer>().Add(customer);
            var asset = new Asset { TenantId = tenantB, CustomerId = customer.Id, Name = "Tenant B transformer" };
            seedB.Set<Asset>().Add(asset);
            await seedB.SaveChangesAsync();
            assetBId = asset.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var crossTenant = await new AssetService(dbA).GetByIdAsync(assetBId);
        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task AssetService_GetAllAsync_ReturnsOnlyCurrentTenantAssets()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "A customer" };
            seedA.Set<Customer>().Add(customer);
            seedA.Set<Asset>().Add(new Asset { TenantId = tenantA, CustomerId = customer.Id, Name = "Asset A" });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "B customer" };
            seedB.Set<Customer>().Add(customer);
            seedB.Set<Asset>().Add(new Asset { TenantId = tenantB, CustomerId = customer.Id, Name = "Asset B" });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var assetsA = await new AssetService(dbA).GetAllAsync();
        Assert.Single(assetsA);
        Assert.Equal("Asset A", assetsA[0].Name);

        await using var dbB = CreateContext(dbName, tenantB);
        var assetsB = await new AssetService(dbB).GetAllAsync();
        Assert.Single(assetsB);
        Assert.Equal("Asset B", assetsB[0].Name);
    }

    [Fact]
    public async Task DivisionService_GetAllAsync_ReturnsOnlyCurrentTenantDivisions()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Division>().Add(new Division { TenantId = tenantA, Code = "A", Name = "Division A", IsActive = true });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Division>().Add(new Division { TenantId = tenantB, Code = "B", Name = "Division B", IsActive = true });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var divisionsA = await new DivisionService(dbA).GetAllAsync();
        Assert.Single(divisionsA);
        Assert.Equal("Division A", divisionsA[0].Name);

        await using var dbB = CreateContext(dbName, tenantB);
        var divisionsB = await new DivisionService(dbB).GetAllAsync();
        Assert.Single(divisionsB);
        Assert.Equal("Division B", divisionsB[0].Name);
    }

    [Fact]
    public async Task CompanyDocumentService_GetAllAsync_ReturnsOnlyCurrentTenantDocuments()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<CompanyDocument>().Add(new CompanyDocument { TenantId = tenantA, DocumentType = "COID", Title = "Doc A", NoExpiry = true });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<CompanyDocument>().Add(new CompanyDocument { TenantId = tenantB, DocumentType = "Tax", Title = "Doc B", NoExpiry = true });
            await seedB.SaveChangesAsync();
        }

        var storage = new Mock<IDocumentStorageService>();
        var audit = new Mock<IAuditService>();

        await using var dbA = CreateContext(dbName, tenantA);
        var tenantProviderA = new Mock<ITenantProvider>();
        tenantProviderA.Setup(p => p.GetCurrentTenantId()).Returns(tenantA);
        var docsA = await new CompanyDocumentService(
            dbA,
            storage.Object,
            tenantProviderA.Object,
            audit.Object).GetAllAsync();
        Assert.Single(docsA);
        Assert.Equal("Doc A", docsA[0].Title);

        await using var dbB = CreateContext(dbName, tenantB);
        var tenantProviderB = new Mock<ITenantProvider>();
        tenantProviderB.Setup(p => p.GetCurrentTenantId()).Returns(tenantB);
        var docsB = await new CompanyDocumentService(
            dbB,
            storage.Object,
            tenantProviderB.Object,
            audit.Object).GetAllAsync();
        Assert.Single(docsB);
        Assert.Equal("Doc B", docsB[0].Title);
    }

    [Fact]
    public async Task StockTakeService_GetAllAsync_ReturnsOnlyCurrentTenantSessions()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<StockTakeSession>().Add(new StockTakeSession
            {
                TenantId = tenantA,
                StartedByUserId = userId,
                Status = StockTakeStatus.Open
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<StockTakeSession>().Add(new StockTakeSession
            {
                TenantId = tenantB,
                StartedByUserId = userId,
                Status = StockTakeStatus.Posted
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var sessionsA = await new StockTakeService(dbA, new InventoryService(dbA)).GetAllAsync();
        Assert.Single(sessionsA);
        Assert.Equal(StockTakeStatus.Open, sessionsA[0].Status);

        await using var dbB = CreateContext(dbName, tenantB);
        var sessionsB = await new StockTakeService(dbB, new InventoryService(dbB)).GetAllAsync();
        Assert.Single(sessionsB);
        Assert.Equal(StockTakeStatus.Posted, sessionsB[0].Status);
    }

    [Fact]
    public async Task CustomerService_GetByIdAsync_DoesNotReturnOtherTenantCustomer()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid customerBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Other tenant customer" };
            seedB.Set<Customer>().Add(customer);
            await seedB.SaveChangesAsync();
            customerBId = customer.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        Assert.Null(await new CustomerService(dbA).GetByIdAsync(customerBId));
    }

    [Fact]
    public async Task SupplierService_GetByIdAsync_DoesNotReturnOtherTenantSupplier()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid supplierBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var supplier = new Supplier { TenantId = tenantB, Name = "Other tenant supplier", IsActive = true };
            seedB.Set<Supplier>().Add(supplier);
            await seedB.SaveChangesAsync();
            supplierBId = supplier.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        Assert.Null(await new SupplierService(dbA).GetByIdAsync(supplierBId));
    }

    [Fact]
    public async Task OpportunityService_GetByIdAsync_DoesNotReturnOtherTenantOpportunity()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid opportunityBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var opportunity = new Opportunity
            {
                TenantId = tenantB,
                Title = "Other tenant opp",
                CustomerName = "B",
                Value = 500m
            };
            seedB.Set<Opportunity>().Add(opportunity);
            await seedB.SaveChangesAsync();
            opportunityBId = opportunity.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        Assert.Null(await new OpportunityService(dbA).GetByIdAsync(opportunityBId));
    }

    [Fact]
    public async Task EmployeeService_GetByIdAsync_DoesNotReturnOtherTenantEmployee()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid employeeBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var employee = new Employee
            {
                TenantId = tenantB,
                EmployeeNumber = "B-1",
                FirstName = "Other",
                LastName = "Tenant Tech",
                IsActive = true
            };
            seedB.Set<Employee>().Add(employee);
            await seedB.SaveChangesAsync();
            employeeBId = employee.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        Assert.Null(await new EmployeeService(dbA).GetByIdAsync(employeeBId));
    }

    [Fact]
    public async Task DivisionService_GetByIdAsync_DoesNotReturnOtherTenantDivision()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid divisionBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var division = new Division { TenantId = tenantB, Code = "B", Name = "Other tenant division", IsActive = true };
            seedB.Set<Division>().Add(division);
            await seedB.SaveChangesAsync();
            divisionBId = division.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        Assert.Null(await new DivisionService(dbA).GetByIdAsync(divisionBId));
    }

    [Fact]
    public async Task CompanyDocumentService_GetByIdAsync_DoesNotReturnOtherTenantDocument()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid docBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var doc = new CompanyDocument { TenantId = tenantB, DocumentType = "Tax", Title = "Other tenant doc", NoExpiry = true };
            seedB.Set<CompanyDocument>().Add(doc);
            await seedB.SaveChangesAsync();
            docBId = doc.Id;
        }

        var storage = new Mock<IDocumentStorageService>();
        var audit = new Mock<IAuditService>();
        var tenantProviderA = new Mock<ITenantProvider>();
        tenantProviderA.Setup(p => p.GetCurrentTenantId()).Returns(tenantA);

        await using var dbA = CreateContext(dbName, tenantA);
        var service = new CompanyDocumentService(dbA, storage.Object, tenantProviderA.Object, audit.Object);
        Assert.Null(await service.GetByIdAsync(docBId));
    }

    [Fact]
    public async Task StockTakeService_GetByIdAsync_DoesNotReturnOtherTenantSession()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid sessionBId;

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var session = new StockTakeSession
            {
                TenantId = tenantB,
                StartedByUserId = Guid.NewGuid(),
                Status = StockTakeStatus.Open
            };
            seedB.Set<StockTakeSession>().Add(session);
            await seedB.SaveChangesAsync();
            sessionBId = session.Id;
        }

        await using var dbA = CreateContext(dbName, tenantA);
        Assert.Null(await new StockTakeService(dbA, new InventoryService(dbA)).GetByIdAsync(sessionBId));
    }

    [Fact]
    public async Task StockTakeService_StartSessionAsync_UsesOnlyCurrentTenantInventory()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<InventoryItem>().Add(new InventoryItem
            {
                TenantId = tenantA,
                Sku = "STK-A",
                Name = "Stock take item A",
                QuantityOnHand = 12,
                ReorderLevel = 2,
                IsActive = true
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<InventoryItem>().Add(new InventoryItem
            {
                TenantId = tenantB,
                Sku = "STK-B",
                Name = "Stock take item B",
                QuantityOnHand = 8,
                ReorderLevel = 1,
                IsActive = true
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var serviceA = new StockTakeService(dbA, new InventoryService(dbA));
        var sessionId = await serviceA.StartSessionAsync(userId);
        var session = await serviceA.GetByIdAsync(sessionId);

        Assert.NotNull(session);
        Assert.Single(session!.Lines);
        Assert.Equal(12, session.Lines.First().SystemQuantity);
        Assert.Equal(tenantA, session.TenantId);
    }

    [Fact]
    public async Task LeaveService_GetPendingApprovalsAsync_ReturnsOnlyCurrentTenantRequests()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var employeeA = new Employee
            {
                TenantId = tenantA,
                EmployeeNumber = "LA-1",
                FirstName = "Leave",
                LastName = "A",
                IsActive = true
            };
            seedA.Set<Employee>().Add(employeeA);
            seedA.Set<LeaveRequest>().Add(new LeaveRequest
            {
                TenantId = tenantA,
                EmployeeId = employeeA.Id,
                StartDate = DateTime.UtcNow.AddDays(7),
                EndDate = DateTime.UtcNow.AddDays(9),
                DaysRequested = 2m,
                Status = LeaveRequestStatus.PendingManager
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var employeeB = new Employee
            {
                TenantId = tenantB,
                EmployeeNumber = "LB-1",
                FirstName = "Leave",
                LastName = "B",
                IsActive = true
            };
            seedB.Set<Employee>().Add(employeeB);
            seedB.Set<LeaveRequest>().Add(new LeaveRequest
            {
                TenantId = tenantB,
                EmployeeId = employeeB.Id,
                StartDate = DateTime.UtcNow.AddDays(14),
                EndDate = DateTime.UtcNow.AddDays(16),
                DaysRequested = 2m,
                Status = LeaveRequestStatus.PendingManager
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var pendingA = await new LeaveService(dbA).GetPendingApprovalsAsync();
        Assert.Single(pendingA);
        Assert.Equal(tenantA, pendingA[0].TenantId);

        await using var dbB = CreateContext(dbName, tenantB);
        var pendingB = await new LeaveService(dbB).GetPendingApprovalsAsync();
        Assert.Single(pendingB);
        Assert.Equal(tenantB, pendingB[0].TenantId);
    }

    [Fact]
    public async Task StockRequisitionService_GetAllAsync_ReturnsOnlyCurrentTenantRequisitions()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "Req list customer A" };
            seedA.Set<Customer>().Add(customer);
            var job = new Job { TenantId = tenantA, CustomerId = customer.Id, Title = "Req list job A", QuotedTotal = 500m };
            seedA.Set<Job>().Add(job);
            await seedA.SaveChangesAsync();
            seedA.Set<StockRequisition>().Add(new StockRequisition
            {
                TenantId = tenantA,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                RequisitionNumber = "REQ-A-ALL",
                Status = RequisitionStatus.Approved
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Req list customer B" };
            seedB.Set<Customer>().Add(customer);
            var job = new Job { TenantId = tenantB, CustomerId = customer.Id, Title = "Req list job B", QuotedTotal = 600m };
            seedB.Set<Job>().Add(job);
            await seedB.SaveChangesAsync();
            seedB.Set<StockRequisition>().Add(new StockRequisition
            {
                TenantId = tenantB,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                RequisitionNumber = "REQ-B-ALL",
                Status = RequisitionStatus.Rejected
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var requisitionsA = await new StockRequisitionService(dbA, new InventoryService(dbA)).GetAllAsync(pageSize: 50);
        Assert.Single(requisitionsA);
        Assert.Equal("REQ-A-ALL", requisitionsA[0].RequisitionNumber);

        await using var dbB = CreateContext(dbName, tenantB);
        var requisitionsB = await new StockRequisitionService(dbB, new InventoryService(dbB)).GetAllAsync(pageSize: 50);
        Assert.Single(requisitionsB);
        Assert.Equal("REQ-B-ALL", requisitionsB[0].RequisitionNumber);
    }

    [Fact]
    public async Task StockRequisitionService_GetPendingApprovalsAsync_ReturnsOnlyCurrentTenantRequisitions()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "Req customer A" };
            seedA.Set<Customer>().Add(customer);
            var job = new Job { TenantId = tenantA, CustomerId = customer.Id, Title = "Req job A", QuotedTotal = 500m };
            seedA.Set<Job>().Add(job);
            await seedA.SaveChangesAsync();
            seedA.Set<StockRequisition>().Add(new StockRequisition
            {
                TenantId = tenantA,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                RequisitionNumber = "REQ-A-PEND",
                Status = RequisitionStatus.PendingManager
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Req customer B" };
            seedB.Set<Customer>().Add(customer);
            var job = new Job { TenantId = tenantB, CustomerId = customer.Id, Title = "Req job B", QuotedTotal = 600m };
            seedB.Set<Job>().Add(job);
            await seedB.SaveChangesAsync();
            seedB.Set<StockRequisition>().Add(new StockRequisition
            {
                TenantId = tenantB,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                RequisitionNumber = "REQ-B-PEND",
                Status = RequisitionStatus.PendingExecutive
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var pendingA = await new StockRequisitionService(dbA, new InventoryService(dbA)).GetPendingApprovalsAsync();
        Assert.Single(pendingA);
        Assert.Equal("REQ-A-PEND", pendingA[0].RequisitionNumber);

        await using var dbB = CreateContext(dbName, tenantB);
        var pendingB = await new StockRequisitionService(dbB, new InventoryService(dbB)).GetPendingApprovalsAsync();
        Assert.Single(pendingB);
        Assert.Equal("REQ-B-PEND", pendingB[0].RequisitionNumber);
    }

    [Fact]
    public async Task PurchaseOrderService_GetAllAsync_ReturnsOnlyCurrentTenantOrders()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var supplier = new Supplier { TenantId = tenantA, Name = "PO Supplier A", IsActive = true };
            seedA.Set<Supplier>().Add(supplier);
            seedA.Set<PurchaseOrder>().Add(new PurchaseOrder
            {
                TenantId = tenantA,
                SupplierId = supplier.Id,
                PoNumber = "PO-A-001",
                Status = PurchaseOrderStatus.Draft
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var supplier = new Supplier { TenantId = tenantB, Name = "PO Supplier B", IsActive = true };
            seedB.Set<Supplier>().Add(supplier);
            seedB.Set<PurchaseOrder>().Add(new PurchaseOrder
            {
                TenantId = tenantB,
                SupplierId = supplier.Id,
                PoNumber = "PO-B-001",
                Status = PurchaseOrderStatus.Sent
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var ordersA = await new PurchaseOrderService(dbA, new InventoryService(dbA)).GetAllAsync();
        Assert.Single(ordersA);
        Assert.Equal("PO-A-001", ordersA[0].PoNumber);

        await using var dbB = CreateContext(dbName, tenantB);
        var ordersB = await new PurchaseOrderService(dbB, new InventoryService(dbB)).GetAllAsync();
        Assert.Single(ordersB);
        Assert.Equal("PO-B-001", ordersB[0].PoNumber);
    }

    [Fact]
    public async Task PpeIssueService_GetHistoryAsync_ReturnsOnlyCurrentTenantIssues()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "PPE customer A" };
            seedA.Set<Customer>().Add(customer);
            var job = new Job { TenantId = tenantA, CustomerId = customer.Id, Title = "Job A", QuotedTotal = 100m };
            seedA.Set<Job>().Add(job);
            var item = new InventoryItem { TenantId = tenantA, Sku = "PPE-A", Name = "Helmet A", IsActive = true };
            seedA.Set<InventoryItem>().Add(item);
            await seedA.SaveChangesAsync();
            seedA.Set<EmployeePpeIssue>().Add(new EmployeePpeIssue
            {
                TenantId = tenantA,
                JobId = job.Id,
                InventoryItemId = item.Id,
                RequestedByUserId = Guid.NewGuid(),
                Quantity = 1,
                IssuedAt = DateTime.UtcNow
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "PPE customer B" };
            seedB.Set<Customer>().Add(customer);
            var job = new Job { TenantId = tenantB, CustomerId = customer.Id, Title = "Job B", QuotedTotal = 200m };
            seedB.Set<Job>().Add(job);
            var item = new InventoryItem { TenantId = tenantB, Sku = "PPE-B", Name = "Helmet B", IsActive = true };
            seedB.Set<InventoryItem>().Add(item);
            await seedB.SaveChangesAsync();
            seedB.Set<EmployeePpeIssue>().Add(new EmployeePpeIssue
            {
                TenantId = tenantB,
                JobId = job.Id,
                InventoryItemId = item.Id,
                RequestedByUserId = Guid.NewGuid(),
                Quantity = 2,
                IssuedAt = DateTime.UtcNow
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var historyA = await new PpeIssueService(dbA).GetHistoryAsync();
        Assert.Single(historyA);
        Assert.Equal(1m, historyA[0].Quantity);

        await using var dbB = CreateContext(dbName, tenantB);
        var historyB = await new PpeIssueService(dbB).GetHistoryAsync();
        Assert.Single(historyB);
        Assert.Equal(2m, historyB[0].Quantity);
    }

    [Fact]
    public async Task RecurringJobService_GetAllAsync_ReturnsOnlyCurrentTenantSchedules()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "Recurring customer A" };
            seedA.Set<Customer>().Add(customer);
            await seedA.SaveChangesAsync();
            seedA.Set<RecurringJobSchedule>().Add(new RecurringJobSchedule
            {
                TenantId = tenantA,
                CustomerId = customer.Id,
                Title = "Quarterly panel inspection (recurring)",
                IntervalDays = 90,
                NextRunDate = DateTime.UtcNow.Date.AddDays(14),
                DefaultQuotedTotal = 8500m,
                IsActive = true
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Recurring customer B" };
            seedB.Set<Customer>().Add(customer);
            await seedB.SaveChangesAsync();
            seedB.Set<RecurringJobSchedule>().Add(new RecurringJobSchedule
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                Title = "Beta-only recurring maintenance",
                IntervalDays = 60,
                NextRunDate = DateTime.UtcNow.Date.AddDays(30),
                DefaultQuotedTotal = 4200m,
                IsActive = true
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var schedulesA = await new RecurringJobService(dbA, new JobService(dbA)).GetAllAsync(activeOnly: false);
        Assert.Single(schedulesA);
        Assert.Contains("Quarterly panel", schedulesA[0].Title, StringComparison.OrdinalIgnoreCase);

        await using var dbB = CreateContext(dbName, tenantB);
        var schedulesB = await new RecurringJobService(dbB, new JobService(dbB)).GetAllAsync(activeOnly: false);
        Assert.Single(schedulesB);
        Assert.Contains("Beta-only", schedulesB[0].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecurringJobService_ProcessDueAsync_SpawnsOnlyCurrentTenantJobs()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Recur due customer B" };
            seedB.Set<Customer>().Add(customer);
            await seedB.SaveChangesAsync();
            seedB.Set<RecurringJobSchedule>().Add(new RecurringJobSchedule
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                Title = "Beta due recurring job",
                IntervalDays = 30,
                NextRunDate = DateTime.UtcNow.Date,
                DefaultQuotedTotal = 3000m,
                IsActive = true
            });
            await seedB.SaveChangesAsync();
        }

        await using (var dbA = CreateContext(dbName, tenantA))
        {
            var serviceA = new RecurringJobService(dbA, new JobService(dbA));
            Assert.Equal(0, await serviceA.ProcessDueAsync());

            var customer = new Customer { TenantId = tenantA, Name = "Recur due customer A" };
            dbA.Set<Customer>().Add(customer);
            await dbA.SaveChangesAsync();
            dbA.Set<RecurringJobSchedule>().Add(new RecurringJobSchedule
            {
                TenantId = tenantA,
                CustomerId = customer.Id,
                Title = "Acme due recurring job",
                IntervalDays = 30,
                NextRunDate = DateTime.UtcNow.Date,
                DefaultQuotedTotal = 5000m,
                IsActive = true
            });
            await dbA.SaveChangesAsync();

            Assert.Equal(1, await serviceA.ProcessDueAsync());
            var jobsA = await new JobService(dbA).GetAllAsync(pageSize: 50);
            Assert.Single(jobsA);
            Assert.Equal("Acme due recurring job", jobsA[0].Title);
        }

        await using var dbB = CreateContext(dbName, tenantB);
        var jobsB = await new JobService(dbB).GetAllAsync(pageSize: 50);
        Assert.Empty(jobsB);
    }

    [Fact]
    public async Task PayrollService_GetMonthlySummariesAsync_ReturnsOnlyCurrentTenantEmployees()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var month = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Employee>().Add(new Employee
            {
                TenantId = tenantB,
                EmployeeNumber = "B-PAY-1",
                FirstName = "Beta",
                LastName = "Payroll",
                DefaultHourlyRate = 150m,
                IsActive = true
            });
            await seedB.SaveChangesAsync();
        }

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Employee>().Add(new Employee
            {
                TenantId = tenantA,
                EmployeeNumber = "A-PAY-1",
                FirstName = "Acme",
                LastName = "Payroll",
                DefaultHourlyRate = 200m,
                IsActive = true
            });
            await seedA.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var summariesA = await new PayrollService(dbA).GetMonthlySummariesAsync(month);
        Assert.Single(summariesA);
        Assert.Contains("Acme", summariesA[0].Name, StringComparison.OrdinalIgnoreCase);

        await using var dbB = CreateContext(dbName, tenantB);
        var summariesB = await new PayrollService(dbB).GetMonthlySummariesAsync(month);
        Assert.Single(summariesB);
        Assert.Contains("Beta", summariesB[0].Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkforceReportService_GetTechnicianUtilizationAsync_ReturnsOnlyCurrentTenantEmployees()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var month = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

        var techA = Guid.NewGuid();
        var techB = Guid.NewGuid();
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Employee>().Add(new Employee
            {
                Id = techA,
                TenantId = tenantA,
                FirstName = "Acme",
                LastName = "Tech",
                IsActive = true
            });
            seedA.Set<Job>().Add(new Job { Id = jobA, TenantId = tenantA, CustomerId = Guid.NewGuid(), Title = "Acme job", QuotedTotal = 1000m });
            seedA.Set<JobLabor>().Add(new JobLabor
            {
                TenantId = tenantA,
                JobId = jobA,
                EmployeeId = techA,
                WorkDate = month,
                Hours = 40,
                HourlyRate = 200m
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Employee>().Add(new Employee
            {
                Id = techB,
                TenantId = tenantB,
                FirstName = "Beta",
                LastName = "Tech",
                IsActive = true
            });
            seedB.Set<Job>().Add(new Job { Id = jobB, TenantId = tenantB, CustomerId = Guid.NewGuid(), Title = "Beta job", QuotedTotal = 2000m });
            seedB.Set<JobLabor>().Add(new JobLabor
            {
                TenantId = tenantB,
                JobId = jobB,
                EmployeeId = techB,
                WorkDate = month,
                Hours = 80,
                HourlyRate = 150m
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var utilizationA = await new WorkforceReportService(dbA).GetTechnicianUtilizationAsync(month);
        Assert.Single(utilizationA);
        Assert.Equal("Acme Tech", utilizationA[0].Name);
        Assert.Equal(40m, utilizationA[0].HoursLogged);

        await using var dbB = CreateContext(dbName, tenantB);
        var utilizationB = await new WorkforceReportService(dbB).GetTechnicianUtilizationAsync(month);
        Assert.Single(utilizationB);
        Assert.Equal("Beta Tech", utilizationB[0].Name);
        Assert.Equal(80m, utilizationB[0].HoursLogged);
    }

    [Fact]
    public async Task JobReportService_GetJobProfitabilitySummaryAsync_ReturnsOnlyCurrentTenantJobs()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var profitableJobA = Guid.NewGuid();
        var profitableJobB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Job>().Add(new Job
            {
                Id = profitableJobA,
                TenantId = tenantA,
                CustomerId = Guid.NewGuid(),
                JobNumber = "A-JOB",
                Title = "Acme profitable",
                QuotedTotal = 10_000m
            });
            seedA.Set<JobCost>().Add(new JobCost
            {
                TenantId = tenantA,
                JobId = profitableJobA,
                Amount = 7_000m,
                CostType = "Material"
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Job>().Add(new Job
            {
                Id = profitableJobB,
                TenantId = tenantB,
                CustomerId = Guid.NewGuid(),
                JobNumber = "B-JOB",
                Title = "Beta profitable",
                QuotedTotal = 5_000m
            });
            seedB.Set<JobCost>().Add(new JobCost
            {
                TenantId = tenantB,
                JobId = profitableJobB,
                Amount = 2_000m,
                CostType = "Material"
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var summaryA = await new JobReportService(dbA).GetJobProfitabilitySummaryAsync();
        Assert.Equal(1, summaryA.JobsAnalyzed);
        Assert.NotNull(summaryA.TopPerformer);
        Assert.Equal("Acme profitable", summaryA.TopPerformer!.Title);

        await using var dbB = CreateContext(dbName, tenantB);
        var summaryB = await new JobReportService(dbB).GetJobProfitabilitySummaryAsync();
        Assert.Equal(1, summaryB.JobsAnalyzed);
        Assert.NotNull(summaryB.TopPerformer);
        Assert.Equal("Beta profitable", summaryB.TopPerformer!.Title);
    }

    [Fact]
    public async Task CashflowReportService_GetCashflowForecastAsync_ReturnsOnlyCurrentTenantDocuments()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            var customerA = Guid.NewGuid();
            seedA.Set<Invoice>().Add(new Invoice
            {
                TenantId = tenantA,
                CustomerId = customerA,
                InvoiceNumber = "A-INV",
                Status = InvoiceStatus.Sent,
                Total = 100_000m
            });
            seedA.Set<Quote>().Add(new Quote
            {
                TenantId = tenantA,
                CustomerId = customerA,
                QuoteNumber = "A-Q",
                Status = QuoteStatus.Accepted,
                Total = 50_000m
            });
            seedA.Set<PurchaseOrder>().Add(new PurchaseOrder
            {
                TenantId = tenantA,
                SupplierId = Guid.NewGuid(),
                PoNumber = "A-PO",
                Status = PurchaseOrderStatus.Sent,
                Total = 25_000m
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var customerB = Guid.NewGuid();
            seedB.Set<Invoice>().Add(new Invoice
            {
                TenantId = tenantB,
                CustomerId = customerB,
                InvoiceNumber = "B-INV",
                Status = InvoiceStatus.Sent,
                Total = 999_000m
            });
            seedB.Set<Quote>().Add(new Quote
            {
                TenantId = tenantB,
                CustomerId = customerB,
                QuoteNumber = "B-Q",
                Status = QuoteStatus.Accepted,
                Total = 888_000m
            });
            seedB.Set<PurchaseOrder>().Add(new PurchaseOrder
            {
                TenantId = tenantB,
                SupplierId = Guid.NewGuid(),
                PoNumber = "B-PO",
                Status = PurchaseOrderStatus.Sent,
                Total = 777_000m
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var forecastA = await new CashflowReportService(dbA).GetCashflowForecastAsync();
        Assert.Equal(100_000m, forecastA.ReceivableInflow);
        Assert.Equal(50_000m, forecastA.PipelineInflow);
        Assert.Equal(25_000m, forecastA.CommittedOutflow);
        Assert.Equal(125_000m, forecastA.NetForecastInflow);

        await using var dbB = CreateContext(dbName, tenantB);
        var forecastB = await new CashflowReportService(dbB).GetCashflowForecastAsync();
        Assert.Equal(999_000m, forecastB.ReceivableInflow);
        Assert.Equal(888_000m, forecastB.PipelineInflow);
        Assert.Equal(777_000m, forecastB.CommittedOutflow);
        Assert.Equal(1_110_000m, forecastB.NetForecastInflow);
    }
}