using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class InvoiceAgedDebtorsTests
{
    /// <summary>
    /// Matches <c>InvoiceService.GetAgedDebtorsAsync</c> candidate window.
    /// The parent under test must sit strictly outside that oldest-N cut.
    /// </summary>
    private const int CandidateWindow = 200;

    [Fact]
    public async Task GetAgedDebtorsAsync_LinkedCreditParentOutsideTakeWindow_NetsParentBeforeSibling()
    {
        var now = DateTime.UtcNow;
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var parentCustomer = new Customer { TenantId = tenantId, Name = "Parent Co" };
            var fillerCustomer = new Customer { TenantId = tenantId, Name = "Filler Co" };
            db.Set<Customer>().AddRange(parentCustomer, fillerCustomer);

            // Oldest overdue invoice for Parent Co — inside the global window.
            var sibling = InvoiceRow(tenantId, parentCustomer.Id, "INV-SIBLING", now.AddDays(-400), 1000m);
            // Newer than every filler, so Take(200) on due date drops it. Still overdue.
            var parent = InvoiceRow(tenantId, parentCustomer.Id, "INV-PARENT", now.AddDays(-5), 500m);
            db.Set<Invoice>().AddRange(sibling, parent);

            for (var n = 0; n < CandidateWindow; n++)
            {
                db.Set<Invoice>().Add(InvoiceRow(
                    tenantId,
                    fillerCustomer.Id,
                    $"INV-FILL-{n:000}",
                    now.AddDays(-100).AddMinutes(n),
                    100m));
            }

            // Positive total. Linked to the parent that the old Take(200) never loaded.
            // 500 belongs on the parent; the leftover 200 belongs on the oldest sibling.
            // Missing the parent would dump all 700 onto INV-SIBLING (balance 300).
            db.Set<Invoice>().Add(new Invoice
            {
                TenantId = tenantId,
                CustomerId = parentCustomer.Id,
                InvoiceNumber = "CRN-LINKED",
                DocumentType = InvoiceDocumentType.CreditNote,
                Status = InvoiceStatus.Sent,
                InvoiceDate = now.AddDays(-1),
                DueDate = now.AddDays(-1),
                Subtotal = 700m,
                Tax = 0m,
                Total = 700m,
                AmountPaid = 0m,
                CreditNoteForInvoiceId = parent.Id
            });
            await db.SaveChangesAsync();

            var rows = await service.GetAgedDebtorsAsync();

            Assert.DoesNotContain(rows, r => r.InvoiceNumber == "CRN-LINKED");
            var siblingRow = Assert.Single(rows, r => r.InvoiceNumber == "INV-SIBLING");
            Assert.Equal("Parent Co", siblingRow.CustomerName);
            Assert.Equal(1000m, siblingRow.Total);
            Assert.Equal(800m, siblingRow.BalanceDue);
            Assert.Equal(50, rows.Count);
            Assert.All(
                rows.Where(r => r.InvoiceNumber.StartsWith("INV-FILL-", StringComparison.Ordinal)),
                r => Assert.Equal(100m, r.BalanceDue));

            var storedCredit = await db.Set<Invoice>().AsNoTracking()
                .SingleAsync(i => i.InvoiceNumber == "CRN-LINKED");
            Assert.Equal(InvoiceDocumentType.CreditNote, storedCredit.DocumentType);
            Assert.True(storedCredit.Total > 0m);
        }
    }

    private static Invoice InvoiceRow(Guid tenantId, Guid customerId, string number, DateTime due, decimal total) =>
        new()
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = number,
            DocumentType = InvoiceDocumentType.Standard,
            Status = InvoiceStatus.Sent,
            InvoiceDate = due.AddDays(-1),
            DueDate = due,
            Subtotal = total,
            Tax = 0m,
            Total = total,
            AmountPaid = 0m
        };

    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"aged-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        return (new InvoiceService(db), db, tenantId);
    }
}
