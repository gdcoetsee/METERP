using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class IssueCreditNoteTests
{
    [Fact]
    public async Task IssueCreditNoteAsync_SetsSent_Audits_AndReducesStatementByVatInclusiveTotal()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb($"issue-cn-{Guid.NewGuid():N}", tenantId);
        var audit = new Mock<IAuditService>();
        var service = new InvoiceService(db, auditService: audit.Object);

        var customer = new Customer { Name = "Substation Co" };
        db.Set<Customer>().Add(customer);
        var source = SentInvoice(customer.Id, "INV-1000", 1000m, unitPrice: 100m);
        db.Set<Invoice>().Add(source);
        await db.SaveChangesAsync();

        var draft = await service.CreateCreditNoteAsync(source.Id, "Rework on the bushings");
        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Equal(115m, draft.Total);

        var before = await service.GetCustomerStatementAsync(customer.Id);
        Assert.NotNull(before);
        Assert.Equal(1000m, before.ClosingBalance);
        Assert.DoesNotContain(before.Lines, line => line.Reference == draft.InvoiceNumber);

        var issued = await service.IssueCreditNoteAsync(draft.Id);

        Assert.Equal(InvoiceStatus.Sent, issued.Status);
        Assert.Equal(115m, issued.Total);
        Assert.Equal(draft.InvoiceNumber, issued.InvoiceNumber);

        var sourceAfter = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == source.Id);
        Assert.Equal(InvoiceStatus.Sent, sourceAfter.Status);
        Assert.Equal(1000m, sourceAfter.Total);

        var after = await service.GetCustomerStatementAsync(customer.Id);
        Assert.NotNull(after);
        Assert.Equal(885m, after.ClosingBalance);
        Assert.Equal(before.ClosingBalance - issued.Total, after.ClosingBalance);
        var creditLine = Assert.Single(after.Lines, line => line.Kind == "Credit note");
        Assert.Equal(issued.InvoiceNumber, creditLine.Reference);
        Assert.Equal(115m, creditLine.Credit);

        audit.Verify(a => a.LogAsync(
            "ISSUE",
            "Invoice",
            issued.InvoiceNumber,
            It.Is<string>(d => d.Contains(issued.InvoiceNumber) && d.Contains(issued.Total.ToString("N2"))),
            It.IsAny<CancellationToken>()), Times.Once);

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IssueCreditNoteAsync(draft.Id));
        Assert.Equal("Only a draft credit note can be issued.", again.Message);
    }

    [Fact]
    public async Task UpdateStatusAsync_Sent_IssuesDraftCreditNoteWithoutCustomerEmail()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "No Mail Co" };
            db.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-MAIL", 200m, unitPrice: 100m);
            db.Set<Invoice>().Add(source);
            await db.SaveChangesAsync();

            var draft = await service.CreateCreditNoteAsync(source.Id, "Price correction");
            Assert.Equal(115m, draft.Total);

            var before = await service.GetCustomerStatementAsync(customer.Id);
            Assert.NotNull(before);
            Assert.Equal(200m, before.ClosingBalance);

            await service.UpdateStatusAsync(draft.Id, InvoiceStatus.Sent);

            var saved = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == draft.Id);
            Assert.Equal(InvoiceStatus.Sent, saved.Status);
            var after = await service.GetCustomerStatementAsync(customer.Id);
            Assert.NotNull(after);
            Assert.Equal(85m, after.ClosingBalance);
        }
    }

    [Fact]
    public async Task IssueCreditNoteAsync_RejectsCreditNoteWhoseSourceIsACreditNote()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Chain Co" };
            db.Set<Customer>().Add(customer);
            var parent = new Invoice
            {
                CustomerId = customer.Id,
                InvoiceNumber = "CN-PARENT",
                Status = InvoiceStatus.Sent,
                DocumentType = InvoiceDocumentType.CreditNote,
                Total = 80m
            };
            db.Set<Invoice>().Add(parent);
            await db.SaveChangesAsync();

            var child = DraftCredit(customer.Id, parent.Id, "CN-CHILD");
            db.Set<Invoice>().Add(child);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueCreditNoteAsync(child.Id));
            Assert.Equal("Cannot issue a credit note from another credit note.", ex.Message);

            var stillDraft = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == child.Id);
            Assert.Equal(InvoiceStatus.Draft, stillDraft.Status);
        }
    }

    [Fact]
    public async Task IssueCreditNoteAsync_RejectsProforma()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Proforma Co" };
            db.Set<Customer>().Add(customer);
            var proforma = new Invoice
            {
                CustomerId = customer.Id,
                InvoiceNumber = "PF-1",
                Status = InvoiceStatus.Sent,
                DocumentType = InvoiceDocumentType.Proforma,
                Total = 400m,
                Lines = { new InvoiceLine { Description = "Estimate", Quantity = 1, UnitPrice = 400m } }
            };
            db.Set<Invoice>().Add(proforma);
            await db.SaveChangesAsync();

            var direct = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueCreditNoteAsync(proforma.Id));
            Assert.Equal("Cannot issue a credit note from a proforma.", direct.Message);

            var child = DraftCredit(customer.Id, proforma.Id, "CN-PF");
            db.Set<Invoice>().Add(child);
            await db.SaveChangesAsync();

            var linked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueCreditNoteAsync(child.Id));
            Assert.Equal("Cannot issue a credit note from a proforma.", linked.Message);
        }
    }

    [Fact]
    public async Task IssueCreditNoteAsync_RejectsCancelledInvoice()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Cancel Co" };
            db.Set<Customer>().Add(customer);
            var cancelled = new Invoice
            {
                CustomerId = customer.Id,
                InvoiceNumber = "INV-CAN",
                Status = InvoiceStatus.Cancelled,
                DocumentType = InvoiceDocumentType.Standard,
                Total = 500m,
                Lines = { new InvoiceLine { Description = "Work", Quantity = 1, UnitPrice = 500m } }
            };
            db.Set<Invoice>().Add(cancelled);
            await db.SaveChangesAsync();

            var direct = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueCreditNoteAsync(cancelled.Id));
            Assert.Equal("Cannot issue a credit note from a cancelled invoice.", direct.Message);

            var child = DraftCredit(customer.Id, cancelled.Id, "CN-CAN");
            db.Set<Invoice>().Add(child);
            await db.SaveChangesAsync();

            var linked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueCreditNoteAsync(child.Id));
            Assert.Equal("Cannot issue a credit note from a cancelled invoice.", linked.Message);
        }
    }

    [Fact]
    public async Task IssueCreditNoteAsync_IsTenantScoped_AndIgnoresSoftDeleted()
    {
        var database = $"issue-cn-tenant-{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid otherCreditId;

        await using (var dbB = CreateDb(database, tenantB))
        {
            var customer = new Customer { Name = "Other Co" };
            dbB.Set<Customer>().Add(customer);
            var source = SentInvoice(customer.Id, "INV-B", 300m, unitPrice: 100m);
            dbB.Set<Invoice>().Add(source);
            await dbB.SaveChangesAsync();
            var credit = await new InvoiceService(dbB).CreateCreditNoteAsync(source.Id, "Their credit");
            otherCreditId = credit.Id;
        }

        await using var dbA = CreateDb(database, tenantA);
        var serviceA = new InvoiceService(dbA);
        var hidden = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceA.IssueCreditNoteAsync(otherCreditId));
        Assert.Equal("Credit note not found.", hidden.Message);

        await using var dbBAgain = CreateDb(database, tenantB);
        var stillDraft = await dbBAgain.Set<Invoice>().IgnoreQueryFilters().SingleAsync(i => i.Id == otherCreditId);
        Assert.Equal(InvoiceStatus.Draft, stillDraft.Status);

        var ownCustomer = new Customer { Name = "Own Co" };
        dbA.Set<Customer>().Add(ownCustomer);
        var ownSource = SentInvoice(ownCustomer.Id, "INV-A", 150m, unitPrice: 50m);
        dbA.Set<Invoice>().Add(ownSource);
        await dbA.SaveChangesAsync();
        var ownCredit = await serviceA.CreateCreditNoteAsync(ownSource.Id, "Own credit");
        ownCredit.IsDeleted = true;
        await dbA.SaveChangesAsync();

        var deleted = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceA.IssueCreditNoteAsync(ownCredit.Id));
        Assert.Equal("Credit note not found.", deleted.Message);
    }

    private static Invoice SentInvoice(Guid customerId, string number, decimal total, decimal unitPrice) =>
        new()
        {
            CustomerId = customerId,
            InvoiceNumber = number,
            Status = InvoiceStatus.Sent,
            DocumentType = InvoiceDocumentType.Standard,
            TaxRate = 0.15m,
            Total = total,
            Lines =
            {
                new InvoiceLine { Description = "Labour", Quantity = 1, UnitPrice = unitPrice }
            }
        };

    private static Invoice DraftCredit(Guid customerId, Guid parentId, string number) =>
        new()
        {
            CustomerId = customerId,
            InvoiceNumber = number,
            Status = InvoiceStatus.Draft,
            DocumentType = InvoiceDocumentType.CreditNote,
            CreditNoteForInvoiceId = parentId,
            TaxRate = 0.15m,
            Total = 50m,
            Lines =
            {
                new InvoiceLine { Description = "Credit: labour", Quantity = 1, UnitPrice = 50m }
            }
        };

    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var db = CreateDb($"issue-cn-{Guid.NewGuid():N}", tenantId);
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
