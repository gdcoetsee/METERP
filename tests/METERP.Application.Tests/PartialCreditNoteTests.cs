using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class PartialCreditNoteTests
{
    [Fact]
    public async Task CreatePartialCreditNoteAsync_Splits115OnALargerInvoice()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Bushings Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-LARGE", total: 1150m, amountPaid: 200m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Labour", Quantity = 2, UnitPrice = 500m });
            source.Lines.Add(new InvoiceLine { Description = "Travel", Quantity = 1, UnitPrice = 150m });
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            var credit = await service.CreatePartialCreditNoteAsync(source.Id, "One bushing only", 115m, null);

            Assert.Equal(InvoiceDocumentType.CreditNote, credit.DocumentType);
            Assert.Equal(InvoiceStatus.Draft, credit.Status);
            Assert.Equal(source.Id, credit.CreditNoteForInvoiceId);
            Assert.Equal(0.15m, credit.TaxRate);
            Assert.Equal(100m, credit.Subtotal);
            Assert.Equal(15m, credit.Tax);
            Assert.Equal(115m, credit.Total);
            var line = Assert.Single(credit.Lines, l => !l.IsDeleted);
            Assert.Equal(1m, line.Quantity);
            Assert.Equal(100m, line.UnitPrice);
            Assert.Equal("Credit: One bushing only", line.Description);
            Assert.Contains("R 115.00 incl. VAT", credit.Notes);

            var sourceAfter = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == source.Id);
            Assert.Equal(1150m, sourceAfter.Total);
            Assert.Equal(200m, sourceAfter.AmountPaid);
            Assert.Equal(InvoiceStatus.Sent, sourceAfter.Status);
        }
    }

    [Fact]
    public async Task CreatePartialCreditNoteAsync_PinsSplitWhenRecalcWouldDrift()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Odd Cents Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-ODD", total: 10m, amountPaid: 0m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Sundry", Quantity = 1, UnitPrice = 8.70m });
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            var credit = await service.CreatePartialCreditNoteAsync(source.Id, "Small adjustment", 0.11m, null);

            Assert.Equal(0.10m, credit.Subtotal);
            Assert.Equal(0.01m, credit.Tax);
            Assert.Equal(0.11m, credit.Total);
        }
    }

    [Fact]
    public async Task CreatePartialCreditNoteAsync_PercentOfTotal_UsesTheSameSplit()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Percent Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-PCT", total: 1150m, amountPaid: 0m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Job", Quantity = 1, UnitPrice = 1000m });
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            var credit = await service.CreatePartialCreditNoteAsync(source.Id, "Ten percent", null, 10m);

            Assert.Equal(100m, credit.Subtotal);
            Assert.Equal(15m, credit.Tax);
            Assert.Equal(115m, credit.Total);
            Assert.Equal(source.Id, credit.CreditNoteForInvoiceId);
        }
    }

    [Fact]
    public async Task CreatePartialCreditNoteAsync_AllowsAmountEqualToBalanceDue()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Exact Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-EXACT", total: 230m, amountPaid: 115m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Job", Quantity = 1, UnitPrice = 200m });
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            var credit = await service.CreatePartialCreditNoteAsync(source.Id, "Balance only", 115m, null);

            Assert.Equal(100m, credit.Subtotal);
            Assert.Equal(15m, credit.Tax);
            Assert.Equal(115m, credit.Total);
        }
    }

    [Fact]
    public async Task CreatePartialCreditNoteAsync_RefusesAmountAboveBalanceDue()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Short Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-SHORT", total: 1000m, amountPaid: 900m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Job", Quantity = 1, UnitPrice = 869.57m });
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreatePartialCreditNoteAsync(source.Id, "Too much", 115m, null));
            Assert.Contains("balance due", ex.Message, StringComparison.OrdinalIgnoreCase);

            var notes = await db.Set<Invoice>().IgnoreQueryFilters().CountAsync(i => i.DocumentType == InvoiceDocumentType.CreditNote);
            Assert.Equal(0, notes);
        }
    }

    [Fact]
    public async Task CreatePartialCreditNoteAsync_RefusesAmountAndPercentTogether()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Both Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-BOTH", total: 1150m, amountPaid: 0m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Job", Quantity = 1, UnitPrice = 1000m });
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreatePartialCreditNoteAsync(source.Id, "Pick one", 115m, 10m));
            Assert.Equal("Enter either a VAT-inclusive amount or a percent, not both.", ex.Message);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public async Task CreatePartialCreditNoteAsync_RequiresReason(string reason)
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Reason Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-WHY", total: 1150m, amountPaid: 0m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Job", Quantity = 1, UnitPrice = 1000m });
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreatePartialCreditNoteAsync(source.Id, reason, 115m, null));
        }
    }

    [Fact]
    public async Task CreatePartialCreditNoteAsync_RejectsReasonTooLong_AndAccepts500()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Long Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-LONG", total: 1150m, amountPaid: 0m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Job", Quantity = 1, UnitPrice = 1000m });
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            var tooLong = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreatePartialCreditNoteAsync(source.Id, new string('R', 501), 115m, null));
            Assert.Contains("500 characters", tooLong.Message);

            var reason = new string('R', 500);
            var credit = await service.CreatePartialCreditNoteAsync(source.Id, reason, 115m, null);
            Assert.Contains(reason, credit.Notes);
            Assert.Equal(100m, credit.Subtotal);
            Assert.Equal(15m, credit.Tax);
        }
    }

    [Fact]
    public async Task CreateCreditNoteAsync_FullInvoiceStillClonesEveryLine()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Full Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-FULL", total: 575m, amountPaid: 0m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Labour", Quantity = 1, UnitPrice = 200m });
            source.Lines.Add(new InvoiceLine { Description = "Travel", Quantity = 1, UnitPrice = 300m, LineType = "Travel" });
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            var credit = await service.CreateCreditNoteAsync(source.Id, "Whole invoice");

            Assert.Equal(source.Id, credit.CreditNoteForInvoiceId);
            Assert.Equal(2, credit.Lines.Count(l => !l.IsDeleted));
            Assert.Equal(500m, credit.Subtotal);
            Assert.Equal(75m, credit.Tax);
            Assert.Equal(575m, credit.Total);
            Assert.Equal($"Credit for INV-FULL: Whole invoice", credit.Notes);
        }
    }

    [Fact]
    public async Task CreatePartialCreditNoteAsync_IsTenantScoped()
    {
        var database = $"partial-cn-tenant-{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid otherInvoiceId;

        var providerB = Tenant(tenantB);
        await using (var dbB = CreateDb(database, providerB.Object))
        {
            var customer = new Customer { Name = "Other Co" };
            dbB.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-B", total: 1150m, amountPaid: 0m, taxRate: 0.15m);
            source.Lines.Add(new InvoiceLine { Description = "Job", Quantity = 1, UnitPrice = 1000m });
            dbB.Set<Invoice>().Add(source);
            await dbB.SaveChangesAsync();
            otherInvoiceId = source.Id;
        }

        var providerA = Tenant(tenantA);
        await using var dbA = CreateDb(database, providerA.Object);
        var serviceA = new InvoiceService(dbA, tenantProvider: providerA.Object);
        var hidden = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceA.CreatePartialCreditNoteAsync(otherInvoiceId, "Not ours", 115m, null));
        Assert.Equal("Source invoice not found.", hidden.Message);
    }

    private static Mock<ITenantProvider> Tenant(Guid tenantId)
    {
        var provider = new Mock<ITenantProvider>();
        provider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        return provider;
    }

    private static Invoice SentInvoice(
        Guid customerId,
        string number,
        decimal total,
        decimal amountPaid,
        decimal taxRate) =>
        new()
        {
            CustomerId = customerId,
            InvoiceNumber = number,
            Status = InvoiceStatus.Sent,
            DocumentType = InvoiceDocumentType.Standard,
            TaxRate = taxRate,
            Total = total,
            AmountPaid = amountPaid
        };

    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var db = CreateDb($"partial-cn-{Guid.NewGuid():N}", tenantId);
        return (new InvoiceService(db), db, tenantId);
    }

    private static AppDbContext CreateDb(string database, Guid tenantId) =>
        CreateDb(database, Tenant(tenantId).Object);

    private static AppDbContext CreateDb(string database, ITenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(database)
            .Options;
        return new AppDbContext(options, tenantProvider, new Mock<ICurrentUserService>().Object);
    }
}
