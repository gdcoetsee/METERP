using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Master-data mutations must refresh list caches that embed related navigation properties.
/// </summary>
public class CrossModuleCacheInvalidationTests
{
    private sealed class Harness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public TenantDistributedCacheService Cache { get; }
        public CustomerService Customers { get; }
        public OpportunityService Opportunities { get; }
        public QuoteService Quotes { get; }
        public JobService Jobs { get; }
        public AssetService Assets { get; }
        public InvoiceService Invoices { get; }

        public Harness(Guid tenantId)
        {
            TenantId = tenantId;
            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(s => s.TenantId).Returns(tenantId);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            Db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);

            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 120);
            var provider = services.BuildServiceProvider();
            Cache = new TenantDistributedCacheService(
                provider.GetRequiredService<IDistributedCache>(),
                tenantProvider.Object,
                provider.GetRequiredService<IOptions<CacheOptions>>());

            Customers = new CustomerService(Db, Cache);
            Opportunities = new OpportunityService(Db, cache: Cache);
            Quotes = new QuoteService(Db, cache: Cache);
            Jobs = new JobService(Db, cache: Cache);
            Assets = new AssetService(Db, Cache);
            Invoices = new InvoiceService(Db, cache: Cache);
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<(Customer Customer, Quote Quote, Job Job)> SeedJobWithQuoteAsync(
        Harness harness,
        string quoteNumber)
    {
        var customer = new Customer
        {
            TenantId = harness.TenantId,
            Name = "Quote Link Customer"
        };
        harness.Db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            QuoteNumber = quoteNumber,
            TaxRate = 0.15m
        };
        harness.Db.Set<Quote>().Add(quote);

        var job = new Job
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            QuoteId = quote.Id,
            JobNumber = "J-QUOTE-LINK-001",
            Title = "Quoted install",
            QuotedTotal = 12000m
        };
        harness.Db.Set<Job>().Add(job);

        await harness.Db.SaveChangesAsync();
        return (customer, quote, job);
    }

    private static async Task<(Customer Customer, Job Job)> SeedJobWithInvoiceAsync(
        Harness harness,
        string jobNumber)
    {
        var customer = new Customer
        {
            TenantId = harness.TenantId,
            Name = "Invoice Link Customer"
        };
        harness.Db.Set<Customer>().Add(customer);

        var job = new Job
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            JobNumber = jobNumber,
            Title = "Billable job",
            QuotedTotal = 8000m
        };
        harness.Db.Set<Job>().Add(job);

        harness.Db.Set<Invoice>().Add(new Invoice
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            JobId = job.Id,
            InvoiceNumber = "INV-CROSS-001",
            TaxRate = 0.15m
        });

        await harness.Db.SaveChangesAsync();
        return (customer, job);
    }

    private sealed class WorkforceHarness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public TenantDistributedCacheService Cache { get; }
        public EmployeeService Employees { get; }
        public AssetService Assets { get; }
        public JobService Jobs { get; }

        public WorkforceHarness(Guid tenantId)
        {
            TenantId = tenantId;
            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(s => s.TenantId).Returns(tenantId);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            Db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);

            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 120);
            var provider = services.BuildServiceProvider();
            Cache = new TenantDistributedCacheService(
                provider.GetRequiredService<IDistributedCache>(),
                tenantProvider.Object,
                provider.GetRequiredService<IOptions<CacheOptions>>());

            Employees = new EmployeeService(Db, Cache);
            Assets = new AssetService(Db, Cache);
            Jobs = new JobService(Db, cache: Cache);
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<Customer> SeedCustomerWithSpineAsync(Harness harness, string customerName)
    {
        var customer = new Customer
        {
            TenantId = harness.TenantId,
            Name = customerName
        };
        harness.Db.Set<Customer>().Add(customer);

        harness.Db.Set<Opportunity>().Add(new Opportunity
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            Title = "Panel upgrade opp",
            Value = 12000m
        });

        harness.Db.Set<Quote>().Add(new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-CROSS-001",
            TaxRate = 0.15m
        });

        harness.Db.Set<Job>().Add(new Job
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            JobNumber = "J-CROSS-001",
            Title = "Install job",
            QuotedTotal = 9000m
        });

        await harness.Db.SaveChangesAsync();
        return customer;
    }

    private static async Task SeedCustomerAssetAsync(Harness harness, string customerName)
    {
        var customer = new Customer
        {
            TenantId = harness.TenantId,
            Name = customerName
        };
        harness.Db.Set<Customer>().Add(customer);
        harness.Db.Set<Asset>().Add(new Asset
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            AssetNumber = "AST-CROSS-001",
            Name = "Main transformer"
        });
        await harness.Db.SaveChangesAsync();
    }

    private static async Task<(Customer Customer, Employee Employee, Asset Asset)> SeedWorkforceJobAsync(
        WorkforceHarness harness,
        string customerName,
        string employeeLastName,
        string assetName)
    {
        var customer = new Customer
        {
            TenantId = harness.TenantId,
            Name = customerName
        };
        harness.Db.Set<Customer>().Add(customer);

        var employee = new Employee
        {
            TenantId = harness.TenantId,
            EmployeeNumber = "EMP-CROSS-001",
            FirstName = "Alex",
            LastName = employeeLastName,
            DefaultHourlyRate = 250m
        };
        harness.Db.Set<Employee>().Add(employee);

        var asset = new Asset
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            AssetNumber = "AST-CROSS-002",
            Name = assetName
        };
        harness.Db.Set<Asset>().Add(asset);

        harness.Db.Set<Job>().Add(new Job
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            AssignedEmployeeId = employee.Id,
            AssetId = asset.Id,
            JobNumber = "J-WORKFORCE-001",
            Title = "Service call",
            QuotedTotal = 4500m
        });

        await harness.Db.SaveChangesAsync();
        return (customer, employee, asset);
    }

    [Fact]
    public async Task CustomerUpdate_InvalidatesAssetListCache_WithFreshCustomerName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        await SeedCustomerAssetAsync(harness, "Old Customer Co");

        Assert.Equal("Old Customer Co", (await harness.Assets.GetAllAsync())[0].Customer!.Name);

        var customer = await harness.Db.Set<Customer>().FirstAsync();
        customer.Name = "Renamed Customer Co";
        await harness.Customers.UpdateAsync(customer);

        var refreshed = await harness.Assets.GetAllAsync();
        Assert.Equal("Renamed Customer Co", refreshed[0].Customer!.Name);
    }

    [Fact]
    public async Task CustomerUpdate_InvalidatesOpportunityListCache_WithFreshCustomerName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var customer = await SeedCustomerWithSpineAsync(harness, "Old Customer Co");

        Assert.Equal("Old Customer Co", (await harness.Opportunities.GetAllAsync())[0].Customer!.Name);

        customer.Name = "Renamed Customer Co";
        await harness.Customers.UpdateAsync(customer);

        var refreshed = await harness.Opportunities.GetAllAsync();
        Assert.Equal("Renamed Customer Co", refreshed[0].Customer!.Name);
    }

    [Fact]
    public async Task CustomerUpdate_InvalidatesQuoteListCache_WithFreshCustomerName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var customer = await SeedCustomerWithSpineAsync(harness, "Old Customer Co");

        Assert.Equal("Old Customer Co", (await harness.Quotes.GetAllAsync())[0].Customer!.Name);

        customer.Name = "Renamed Customer Co";
        await harness.Customers.UpdateAsync(customer);

        var refreshed = await harness.Quotes.GetAllAsync();
        Assert.Equal("Renamed Customer Co", refreshed[0].Customer!.Name);
    }

    [Fact]
    public async Task CustomerUpdate_InvalidatesJobListCache_WithFreshCustomerName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var customer = await SeedCustomerWithSpineAsync(harness, "Old Customer Co");

        Assert.Equal("Old Customer Co", (await harness.Jobs.GetAllAsync())[0].Customer!.Name);

        customer.Name = "Renamed Customer Co";
        await harness.Customers.UpdateAsync(customer);

        var refreshed = await harness.Jobs.GetAllAsync();
        Assert.Equal("Renamed Customer Co", refreshed[0].Customer!.Name);
    }

    [Fact]
    public async Task CustomerDelete_InvalidatesOpportunityListCache()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var customer = await SeedCustomerWithSpineAsync(harness, "Delete Me Co");

        Assert.Equal("Panel upgrade opp", (await harness.Opportunities.GetAllAsync())[0].Title);

        await harness.Customers.DeleteAsync(customer.Id);

        var opp = await harness.Db.Set<Opportunity>().FirstAsync();
        opp.Title = "Opp title after customer delete";
        await harness.Db.SaveChangesAsync();

        Assert.Equal("Opp title after customer delete", (await harness.Opportunities.GetAllAsync())[0].Title);
    }

    private sealed class SupplierHarness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public TenantDistributedCacheService Cache { get; }
        public SupplierService Suppliers { get; }
        public PurchaseOrderService PurchaseOrders { get; }

        public SupplierHarness(Guid tenantId)
        {
            TenantId = tenantId;
            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(s => s.TenantId).Returns(tenantId);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            Db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);

            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 120);
            var provider = services.BuildServiceProvider();
            Cache = new TenantDistributedCacheService(
                provider.GetRequiredService<IDistributedCache>(),
                tenantProvider.Object,
                provider.GetRequiredService<IOptions<CacheOptions>>());

            Suppliers = new SupplierService(Db, Cache);
            PurchaseOrders = new PurchaseOrderService(Db, new InventoryService(Db), cache: Cache);
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<Supplier> SeedSupplierWithPurchaseOrderAsync(SupplierHarness harness, string supplierName)
    {
        var supplier = new Supplier
        {
            TenantId = harness.TenantId,
            Name = supplierName
        };
        harness.Db.Set<Supplier>().Add(supplier);

        harness.Db.Set<PurchaseOrder>().Add(new PurchaseOrder
        {
            TenantId = harness.TenantId,
            SupplierId = supplier.Id,
            PoNumber = "PO-CROSS-001",
            PoDate = DateTime.UtcNow,
            TaxRate = 0.15m
        });

        await harness.Db.SaveChangesAsync();
        return supplier;
    }

    [Fact]
    public async Task SupplierUpdate_InvalidatesPurchaseOrderListCache_WithFreshSupplierName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new SupplierHarness(tenantId);
        var supplier = await SeedSupplierWithPurchaseOrderAsync(harness, "Old Supplier Co");

        Assert.Equal("Old Supplier Co", (await harness.PurchaseOrders.GetAllAsync())[0].Supplier!.Name);

        supplier.Name = "Renamed Supplier Co";
        await harness.Suppliers.UpdateAsync(supplier);

        var refreshed = await harness.PurchaseOrders.GetAllAsync();
        Assert.Equal("Renamed Supplier Co", refreshed[0].Supplier!.Name);
    }

    [Fact]
    public async Task SupplierCreate_InvalidatesPurchaseOrderListCache()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new SupplierHarness(tenantId);
        await SeedSupplierWithPurchaseOrderAsync(harness, "Existing Supplier");

        Assert.Null((await harness.PurchaseOrders.GetAllAsync())[0].Notes);

        var po = await harness.Db.Set<PurchaseOrder>().FirstAsync();
        po.Notes = "Mutated after cache warm";
        await harness.Db.SaveChangesAsync();

        Assert.Null((await harness.PurchaseOrders.GetAllAsync())[0].Notes);

        await harness.Suppliers.CreateAsync(new Supplier
        {
            TenantId = harness.TenantId,
            Name = "New Supplier"
        });

        Assert.Equal("Mutated after cache warm", (await harness.PurchaseOrders.GetAllAsync())[0].Notes);
    }

    [Fact]
    public async Task EmployeeUpdate_InvalidatesJobListCache_WithFreshEmployeeName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new WorkforceHarness(tenantId);
        var (_, employee, _) = await SeedWorkforceJobAsync(harness, "Acme Site", "Oldson", "Panel A");

        Assert.Equal("Oldson", (await harness.Jobs.GetAllAsync())[0].AssignedEmployee!.LastName);

        employee.LastName = "Newson";
        await harness.Employees.UpdateAsync(employee);

        var refreshed = await harness.Jobs.GetAllAsync();
        Assert.Equal("Newson", refreshed[0].AssignedEmployee!.LastName);
    }

    [Fact]
    public async Task AssetUpdate_InvalidatesJobListCache_WithFreshAssetName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new WorkforceHarness(tenantId);
        var (_, _, asset) = await SeedWorkforceJobAsync(harness, "Acme Site", "Crew", "Old Asset Name");

        Assert.Equal("Old Asset Name", (await harness.Jobs.GetAllAsync())[0].Asset!.Name);

        asset.Name = "Renamed Asset";
        await harness.Assets.UpdateAsync(asset);

        var refreshed = await harness.Jobs.GetAllAsync();
        Assert.Equal("Renamed Asset", refreshed[0].Asset!.Name);
    }

    [Fact]
    public async Task QuoteUpdate_InvalidatesJobListCache_WithFreshQuoteNumber()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var (_, quote, _) = await SeedJobWithQuoteAsync(harness, "Q-OLD-001");

        Assert.Equal("Q-OLD-001", (await harness.Jobs.GetAllAsync())[0].Quote!.QuoteNumber);

        quote.QuoteNumber = "Q-RENAMED-001";
        await harness.Quotes.UpdateAsync(quote);

        var refreshed = await harness.Jobs.GetAllAsync();
        Assert.Equal("Q-RENAMED-001", refreshed[0].Quote!.QuoteNumber);
    }

    [Fact]
    public async Task JobUpdate_InvalidatesInvoiceListCache_WithFreshJobNumber()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var (_, job) = await SeedJobWithInvoiceAsync(harness, "J-OLD-001");

        Assert.Equal("J-OLD-001", (await harness.Invoices.GetAllAsync())[0].Job!.JobNumber);

        job.JobNumber = "J-RENAMED-001";
        await harness.Jobs.UpdateAsync(job);

        var refreshed = await harness.Invoices.GetAllAsync();
        Assert.Equal("J-RENAMED-001", refreshed[0].Job!.JobNumber);
    }
}