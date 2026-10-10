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
    private AppDbContext CreateContext(Guid tenantId) => CreateContext(Guid.NewGuid().ToString(), tenantId);

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
    public async Task GetAllAsync_MatchesVatNumberAsWellAsNameEmailAndPhone()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        await service.CreateAsync(new Customer
        {
            Name = "Northern Mine",
            Email = "accounts@north.test",
            Phone = "011-100",
            VatNumber = "ZA4123456789"
        });
        await service.CreateAsync(new Customer
        {
            Name = "Southern Works",
            Email = "ap@south.test",
            Phone = "021-200",
            VatNumber = "4987654321"
        });
        await service.CreateAsync(new Customer { Name = "No Vat Co", Email = "plain@novat.test" });

        var byVat = await service.GetAllAsync("za4123456789");
        Assert.Single(byVat);
        Assert.Equal("Northern Mine", byVat[0].Name);

        var byPartialVat = await service.GetAllAsync("498765");
        Assert.Single(byPartialVat);
        Assert.Equal("Southern Works", byPartialVat[0].Name);

        var byEmail = await service.GetAllAsync("ap@south");
        Assert.Single(byEmail);
        Assert.Equal("Southern Works", byEmail[0].Name);

        var byPhone = await service.GetAllAsync("011-100");
        Assert.Single(byPhone);
        Assert.Equal("Northern Mine", byPhone[0].Name);

        var byName = await service.GetAllAsync("no vat");
        Assert.Single(byName);
        Assert.Equal("No Vat Co", byName[0].Name);
    }

    [Fact]
    public async Task GetAllAsync_VatSearch_DoesNotMatchAnotherTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Customer>().Add(new Customer
            {
                Name = "Tenant A Mine",
                VatNumber = "4111111111"
            });
            seedA.Set<Customer>().Add(new Customer
            {
                Name = "Deleted A",
                VatNumber = "4222222222",
                IsDeleted = true
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Customer>().Add(new Customer
            {
                Name = "Tenant B Works",
                VatNumber = "4222222222"
            });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var serviceA = new CustomerService(dbA);

        var own = await serviceA.GetAllAsync("4111111111");
        Assert.Single(own);
        Assert.Equal("Tenant A Mine", own[0].Name);

        var otherTenant = await serviceA.GetAllAsync("4222222222");
        Assert.Empty(otherTenant);

        await using var dbB = CreateContext(dbName, tenantB);
        var other = await new CustomerService(dbB).GetAllAsync("4222222222");
        Assert.Single(other);
        Assert.Equal("Tenant B Works", other[0].Name);
    }

    [Fact]
    public async Task GetAllAsync_EmptySearch_StillPages()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        await service.CreateAsync(new Customer { Name = "Alpha Co" });
        await service.CreateAsync(new Customer { Name = "Bravo Co" });
        await service.CreateAsync(new Customer { Name = "Charlie Co" });

        var first = await service.GetAllAsync(null, page: 1, pageSize: 2);
        Assert.Equal(new[] { "Alpha Co", "Bravo Co" }, first.Select(c => c.Name).ToArray());

        var blank = await service.GetAllAsync("", page: 1, pageSize: 2);
        Assert.Equal(new[] { "Alpha Co", "Bravo Co" }, blank.Select(c => c.Name).ToArray());

        var whitespace = await service.GetAllAsync("   ", page: 2, pageSize: 2);
        Assert.Equal(new[] { "Charlie Co" }, whitespace.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNameDuplicate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        await service.CreateAsync(new Customer { Name = "Acme Mining" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "Acme Mining" }));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNotesTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "Note Co", Notes = new string('N', 2001) }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AcceptsNotesAt2000Characters()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var id = await service.CreateAsync(new Customer { Name = "Note Ok Co", Notes = new string('N', 2000) });
        var saved = await db.Set<Customer>().FirstAsync(c => c.Id == id);
        Assert.Equal(2000, saved.Notes!.Length);
    }

    [Fact]
    public async Task CreateAsync_AcceptsNameAt200Characters()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var id = await service.CreateAsync(new Customer { Name = new string('C', 200) });
        var saved = await db.Set<Customer>().FirstAsync(c => c.Id == id);
        Assert.Equal(200, saved.Name.Length);
    }

    [Fact]
    public async Task CreateAsync_AcceptsPhoneAt50Characters()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var id = await service.CreateAsync(new Customer { Name = "Phone Ok Co", Phone = new string('1', 50) });
        var saved = await db.Set<Customer>().FirstAsync(c => c.Id == id);
        Assert.Equal(50, saved.Phone!.Length);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenPhoneTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "Phone Co", Phone = new string('1', 51) }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenCityTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "City Co", City = new string('C', 101) }));
        Assert.Contains("100 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenAddressLine1TooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "Addr Co", AddressLine1 = new string('A', 201) }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenPostalCodeTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "Postal Co", PostalCode = new string('1', 21) }));
        Assert.Contains("20 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenVatNumberTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "VAT Co", VatNumber = new string('V', 51) }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenCompanyRegistrationTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer
            {
                Name = "Reg Co",
                CompanyRegistrationNumber = new string('R', 51)
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task AddContactAsync_ThrowsWhenNameTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);
        var id = await service.CreateAsync(new Customer { Name = "Contact Host" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddContactAsync(new Contact
            {
                CustomerId = id,
                FirstName = new string('F', 101),
                LastName = "Ok"
            }));
        Assert.Contains("100 characters", ex.Message);
    }

    [Fact]
    public async Task AddContactAsync_ThrowsWhenPhoneTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);
        var id = await service.CreateAsync(new Customer { Name = "Contact Host" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddContactAsync(new Contact
            {
                CustomerId = id,
                FirstName = "Ok",
                LastName = "Contact",
                Phone = new string('1', 51)
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task AddContactAsync_ThrowsWhenNotesTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);
        var id = await service.CreateAsync(new Customer { Name = "Contact Host" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddContactAsync(new Contact
            {
                CustomerId = id,
                FirstName = "Ok",
                LastName = "Contact",
                Notes = new string('N', 501)
            }));
        Assert.Contains("500 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNameTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = new string('C', 201) }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmailDuplicate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        await service.CreateAsync(new Customer { Name = "A Co", Email = "billing@acme.demo" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "B Co", Email = "billing@acme.demo" }));
        Assert.Contains("email", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmailTooLong()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer
            {
                Name = "Long Email Co",
                Email = new string('a', 195) + "@x.com"
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmailInvalid()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "Bad Email Co", Email = "not-an-email" }));
        Assert.Contains("valid address", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task AddContactAsync_ThrowsWhenCustomerSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customerId = await service.CreateAsync(new Customer { Name = "Gone Client" });
        var customer = await db.Set<Customer>().FirstAsync(c => c.Id == customerId);
        customer.IsDeleted = true;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddContactAsync(new Contact
            {
                CustomerId = customerId,
                FirstName = "A",
                LastName = "B"
            }));
        Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenCustomerHasOpenSalesOrders()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customerId = await service.CreateAsync(new Customer { Name = "SO Client" });

        db.Set<SalesOrder>().Add(new SalesOrder
        {
            TenantId = tenantId,
            CustomerId = customerId,
            SoNumber = "SO-OPEN",
            Status = SalesOrderStatus.Confirmed
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(customerId));
        Assert.Contains("sales order", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await service.GetByIdAsync(customerId));
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenCustomerHasOpenJobs()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customerId = await service.CreateAsync(new Customer { Name = "Active Client" });

        db.Set<Job>().Add(new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = "J-OPEN",
            Title = "Open work",
            Status = JobStatus.InProgress
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(customerId));
        Assert.Contains("open jobs", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await service.GetByIdAsync(customerId));
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenCustomerHasUnpaidInvoices()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customerId = await service.CreateAsync(new Customer { Name = "Billed Client" });

        db.Set<Invoice>().Add(new Invoice
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = "INV-OPEN-1",
            Status = InvoiceStatus.Sent,
            InvoiceDate = DateTime.UtcNow.Date,
            DueDate = DateTime.UtcNow.Date.AddDays(30),
            Total = 1000m
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(customerId));
        Assert.Contains("invoice", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await service.GetByIdAsync(customerId));
    }

    [Fact]
    public async Task AddContactAsync_ThrowsWhenEmailInvalid()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customerId = await service.CreateAsync(new Customer { Name = "Contact Email Co" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddContactAsync(new Contact
            {
                CustomerId = customerId,
                FirstName = "Pat",
                LastName = "Lee",
                Email = "not-an-email"
            }));
        Assert.Contains("email", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task UpdateAsync_PersistsCustomerChanges()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var id = await service.CreateAsync(new Customer { Name = "Before Update", Email = "old@test.com" });

        var customer = await service.GetByIdAsync(id);
        Assert.NotNull(customer);
        customer!.Name = "After Update";
        customer.Email = "new@test.com";
        await service.UpdateAsync(customer);

        var reloaded = await service.GetByIdAsync(id);
        Assert.Equal("After Update", reloaded!.Name);
        Assert.Equal("new@test.com", reloaded.Email);
    }

    [Fact]
    public async Task DeleteContactAsync_SoftDeletesContact()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customerId = await service.CreateAsync(new Customer { Name = "Contact Delete Co" });
        var contactId = await service.AddContactAsync(new Contact
        {
            CustomerId = customerId,
            FirstName = "Temp",
            LastName = "Contact"
        });

        await service.DeleteContactAsync(contactId);

        var contacts = await service.GetContactsAsync(customerId);
        Assert.Empty(contacts);

        var deleted = await db.Set<Contact>().IgnoreQueryFilters().FirstAsync(c => c.Id == contactId);
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public async Task DeleteContactAsync_PromotesAnotherPrimary_WhenDeletingPrimary()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customerId = await service.CreateAsync(new Customer { Name = "Primary Co" });
        var primaryId = await service.AddContactAsync(new Contact
        {
            CustomerId = customerId,
            FirstName = "Primary",
            LastName = "One",
            IsPrimary = true
        });
        var secondaryId = await service.AddContactAsync(new Contact
        {
            CustomerId = customerId,
            FirstName = "Secondary",
            LastName = "Two",
            IsPrimary = false
        });

        await service.DeleteContactAsync(primaryId);

        var remaining = await service.GetContactsAsync(customerId);
        Assert.Single(remaining);
        Assert.Equal(secondaryId, remaining[0].Id);
        Assert.True(remaining[0].IsPrimary);
    }

    [Fact]
    public async Task UpdateContactAsync_ThrowsWhenCustomerSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var customerId = await service.CreateAsync(new Customer { Name = "Contact Parent" });
        var contactId = await service.AddContactAsync(new Contact
        {
            CustomerId = customerId,
            FirstName = "Pat",
            LastName = "Lee",
            Email = "pat@example.com"
        });

        var customer = await db.Set<Customer>().FirstAsync(c => c.Id == customerId);
        customer.IsDeleted = true;
        await db.SaveChangesAsync();

        var contact = await db.Set<Contact>().IgnoreQueryFilters().FirstAsync(c => c.Id == contactId);
        contact.FirstName = "Updated";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateContactAsync(contact));
        Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}