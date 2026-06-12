using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class CustomerServiceTests
{
    private AppDbContext CreateContext(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    [Fact]
    public async Task CreateAsync_PersistsCustomer()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customer = new Customer { Name = "Acme Mining", Email = "ops@acme.demo" };

        var id = await service.CreateAsync(customer);

        Assert.NotEqual(Guid.Empty, id);
        var loaded = await service.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Acme Mining", loaded.Name);
        Assert.Equal(tenantId, loaded.TenantId);
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySearchTerm()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        await service.CreateAsync(new Customer { Name = "Alpha Corp", Email = "alpha@test.com" });
        await service.CreateAsync(new Customer { Name = "Beta Ltd", Phone = "011-555-0100" });

        var results = await service.GetAllAsync("alpha");

        Assert.Single(results);
        Assert.Equal("Alpha Corp", results[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesCustomerAndContacts()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customer = new Customer { Name = "Delete Me Co" };
        var customerId = await service.CreateAsync(customer);
        await service.AddContactAsync(new Contact
        {
            CustomerId = customerId,
            FirstName = "Sam",
            LastName = "Site",
            IsPrimary = true
        });

        await service.DeleteAsync(customerId);

        var contacts = await db.Set<Contact>().IgnoreQueryFilters()
            .Where(c => c.CustomerId == customerId)
            .ToListAsync();
        Assert.All(contacts, c => Assert.True(c.IsDeleted));

        var deletedCustomer = await db.Set<Customer>().IgnoreQueryFilters()
            .FirstAsync(c => c.Id == customerId);
        Assert.True(deletedCustomer.IsDeleted);

        Assert.Null(await service.GetByIdAsync(customerId));
    }

    [Fact]
    public async Task AddContactAsync_ClearsOtherPrimary_WhenSettingPrimary()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customerId = await service.CreateAsync(new Customer { Name = "Primary Test" });
        var firstId = await service.AddContactAsync(new Contact
        {
            CustomerId = customerId,
            FirstName = "A",
            LastName = "One",
            IsPrimary = true
        });
        var secondId = await service.AddContactAsync(new Contact
        {
            CustomerId = customerId,
            FirstName = "B",
            LastName = "Two",
            IsPrimary = true
        });

        var contacts = await service.GetContactsAsync(customerId);
        Assert.Equal(2, contacts.Count);
        Assert.False(contacts.First(c => c.Id == firstId).IsPrimary);
        Assert.True(contacts.First(c => c.Id == secondId).IsPrimary);
    }
}