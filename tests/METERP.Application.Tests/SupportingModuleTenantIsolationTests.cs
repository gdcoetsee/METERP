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
}