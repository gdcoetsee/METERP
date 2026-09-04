using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AccountingExportServiceTests
{
    [Fact]
    public async Task ExportOutstandingSalesAsync_PersistsProviderOnTenant()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            TenantId = tenantId,
            Name = "Acme",
            Subdomain = "acme",
            AccountingProvider = AccountingProvider.None
        });
        db.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Hospital" });
        await db.SaveChangesAsync();
        var customer = db.Customers.Single();
        db.Invoices.Add(new Invoice
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            InvoiceNumber = "INV-1",
            Status = InvoiceStatus.Sent,
            Subtotal = 100m,
            Tax = 15m,
            Total = 115m,
            InvoiceDate = DateTime.UtcNow.Date,
            DueDate = DateTime.UtcNow.Date.AddDays(14)
        });
        await db.SaveChangesAsync();

        var result = await new AccountingExportService(db, tenantProvider.Object)
            .ExportOutstandingSalesAsync(AccountingProvider.Sage, "210");

        Assert.Equal(1, result.InvoiceCount);
        Assert.Contains("Sales Invoice", result.Csv);
        var tenant = await db.Tenants.FindAsync(tenantId);
        Assert.Equal(AccountingProvider.Sage, tenant!.AccountingProvider);
        Assert.Equal("210", tenant.AccountingSalesAccountCode);
    }

    [Fact]
    public async Task ExportOutstandingSalesAsync_None_Throws()
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(Guid.NewGuid());
        var currentUser = new Mock<ICurrentUserService>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AccountingExportService(db, tenantProvider.Object)
                .ExportOutstandingSalesAsync(AccountingProvider.None));
    }
}
