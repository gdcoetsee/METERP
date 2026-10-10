using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class CustomersInCreditTests
{
    [Fact]
    public async Task GetCustomersInCreditAsync_OpenCreditAboveInvoice_ShowsExcessAndDropsOverdueRow()
    {
        var now = DateTime.UtcNow;
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var inCredit = new Customer { TenantId = tenantId, Name = "In Credit Co" };
            var owing = new Customer { TenantId = tenantId, Name = "Still Owing" };
            db.Set<Customer>().AddRange(inCredit, owing);
            db.Set<Invoice>().AddRange(
                Sales(tenantId, inCredit.Id, "INV-50", now.AddDays(-10), 50m),
                Credit(tenantId, inCredit.Id, "CRN-200", 200m, InvoiceStatus.Sent),
                Sales(tenantId, owing.Id, "INV-80", now.AddDays(-8), 80m));
            await db.SaveChangesAsync();

            var credits = await service.GetCustomersInCreditAsync();
            var row = Assert.Single(credits);
            Assert.Equal(inCredit.Id, row.CustomerId);
            Assert.Equal("In Credit Co", row.CustomerName);
            Assert.Equal(150m, row.CreditAmount);

            var aged = await service.GetAgedDebtorsAsync();
            Assert.DoesNotContain(aged, r => r.InvoiceNumber == "INV-50" || r.CustomerName == "In Credit Co");
            var owingRow = Assert.Single(aged);
            Assert.Equal("INV-80", owingRow.InvoiceNumber);
            Assert.Equal(80m, owingRow.BalanceDue);
        }
    }

    [Fact]
    public async Task GetCustomersInCreditAsync_IgnoresDraftCredit()
    {
        var now = DateTime.UtcNow;
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Draft Credit Co" };
            db.Set<Customer>().Add(customer);
            db.Set<Invoice>().AddRange(
                Sales(tenantId, customer.Id, "INV-DRAFT-CREDIT", now.AddDays(-4), 50m),
                Credit(tenantId, customer.Id, "CRN-DRAFT", 200m, InvoiceStatus.Draft));
            await db.SaveChangesAsync();

            var credits = await service.GetCustomersInCreditAsync();
            Assert.Empty(credits);

            var aged = await service.GetAgedDebtorsAsync();
            var row = Assert.Single(aged);
            Assert.Equal("INV-DRAFT-CREDIT", row.InvoiceNumber);
            Assert.Equal(50m, row.BalanceDue);
        }
    }

    [Fact]
    public async Task GetCustomersInCreditAsync_IsTenantScoped_AndIgnoresSoftDeletedCredit()
    {
        var database = $"in-credit-tenant-{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var dbB = CreateDb(database, tenantB))
        {
            var other = new Customer { TenantId = tenantB, Name = "Other Tenant" };
            dbB.Set<Customer>().Add(other);
            dbB.Set<Invoice>().AddRange(
                Sales(tenantB, other.Id, "INV-B", now.AddDays(-5), 10m),
                Credit(tenantB, other.Id, "CRN-B", 500m, InvoiceStatus.Sent));
            await dbB.SaveChangesAsync();
        }

        await using var dbA = CreateDb(database, tenantA);
        var service = new InvoiceService(dbA);
        var own = new Customer { TenantId = tenantA, Name = "Own Co" };
        dbA.Set<Customer>().Add(own);
        var deletedCredit = Credit(tenantA, own.Id, "CRN-DELETED", 200m, InvoiceStatus.Sent);
        deletedCredit.IsDeleted = true;
        dbA.Set<Invoice>().AddRange(
            Sales(tenantA, own.Id, "INV-A", now.AddDays(-5), 50m),
            deletedCredit);
        await dbA.SaveChangesAsync();

        var credits = await service.GetCustomersInCreditAsync();
        Assert.Empty(credits);

        var aged = await service.GetAgedDebtorsAsync();
        var row = Assert.Single(aged);
        Assert.Equal("INV-A", row.InvoiceNumber);
        Assert.Equal("Own Co", row.CustomerName);
        Assert.Equal(50m, row.BalanceDue);
        Assert.DoesNotContain(aged, r => r.CustomerName == "Other Tenant");
    }

    private static Invoice Sales(Guid tenantId, Guid customerId, string number, DateTime due, decimal total) =>
        new()
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = number,
            DocumentType = InvoiceDocumentType.Standard,
            Status = InvoiceStatus.Overdue,
            InvoiceDate = due.AddDays(-1),
            DueDate = due,
            Subtotal = total,
            Tax = 0m,
            Total = total,
            AmountPaid = 0m
        };

    private static Invoice Credit(Guid tenantId, Guid customerId, string number, decimal total, InvoiceStatus status) =>
        new()
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = number,
            DocumentType = InvoiceDocumentType.CreditNote,
            Status = status,
            InvoiceDate = DateTime.UtcNow.Date,
            DueDate = DateTime.UtcNow.Date,
            Subtotal = total,
            Tax = 0m,
            Total = total,
            AmountPaid = 0m
        };

    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var db = CreateDb($"in-credit-{Guid.NewGuid():N}", tenantId);
        return (new InvoiceService(db), db, tenantId);
    }

    private static AppDbContext CreateDb(string database, Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(database)
            .Options;
        return new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
    }
}
