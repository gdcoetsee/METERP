using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class CustomerPortalServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_OnlyReturnsLinkedCustomerDocuments()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        db.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, Name = "Acme Hospital" },
            new Customer { Id = otherId, TenantId = tenantId, Name = "Other Co" });
        db.Invoices.AddRange(
            new Invoice
            {
                TenantId = tenantId,
                CustomerId = customerId,
                InvoiceNumber = "INV-A",
                Status = InvoiceStatus.Sent,
                Total = 1150m,
                AmountPaid = 150m
            },
            new Invoice
            {
                TenantId = tenantId,
                CustomerId = otherId,
                InvoiceNumber = "INV-SECRET",
                Status = InvoiceStatus.Sent,
                Total = 9000m
            });
        db.Quotes.Add(new Quote
        {
            TenantId = tenantId,
            CustomerId = customerId,
            QuoteNumber = "Q-A",
            Status = QuoteStatus.Sent,
            Total = 5000m
        });
        await db.SaveChangesAsync();

        var dashboard = await new CustomerPortalService(db).GetDashboardAsync(customerId);

        Assert.Equal("Acme Hospital", dashboard.CustomerName);
        Assert.Equal(1000m, dashboard.BalanceDue);
        Assert.Contains(dashboard.Invoices, i => i.InvoiceNumber == "INV-A");
        Assert.DoesNotContain(dashboard.Invoices, i => i.InvoiceNumber == "INV-SECRET");
        Assert.Equal(1, dashboard.OpenQuoteCount);
    }

    [Fact]
    public async Task GetDashboardAsync_BalanceMatchesStatement_ForAccessBook()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        db.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, Name = "Metro Power" },
            new Customer { Id = otherId, TenantId = tenantId, Name = "Other Co" });

        var sold = InvoiceRow(tenantId, customerId, "INV-SOLD", 1150m, 1150m, InvoiceStatus.Paid, new DateTime(2026, 9, 1));
        var open = InvoiceRow(tenantId, customerId, "INV-OPEN", 1000m, 0m, InvoiceStatus.Sent, new DateTime(2026, 9, 2));
        var credit = InvoiceRow(
            tenantId, customerId, "CRN-OPEN", 80m, 0m, InvoiceStatus.Sent, new DateTime(2026, 9, 3),
            InvoiceDocumentType.CreditNote);
        var proforma = InvoiceRow(
            tenantId, customerId, "INV-PRO", 50m, 0m, InvoiceStatus.Sent, new DateTime(2026, 9, 4),
            InvoiceDocumentType.Proforma);
        var draft = InvoiceRow(tenantId, customerId, "INV-DRAFT", 999m, 0m, InvoiceStatus.Draft, new DateTime(2026, 9, 5));
        var future = InvoiceRow(tenantId, customerId, "INV-FUTURE", 70m, 0m, InvoiceStatus.Sent, new DateTime(2099, 1, 1));
        var laterPay = InvoiceRow(
            tenantId, customerId, "INV-LATER", 400m, 150m, InvoiceStatus.PartiallyPaid, new DateTime(2026, 9, 6));
        laterPay.Payments.Add(new InvoicePayment
        {
            TenantId = tenantId,
            Invoice = laterPay,
            Amount = 150m,
            PaymentDate = new DateTime(2099, 1, 2),
            Reference = "FUTURE"
        });
        var deleted = InvoiceRow(tenantId, customerId, "INV-GONE", 500m, 0m, InvoiceStatus.Sent, new DateTime(2026, 9, 7));
        deleted.IsDeleted = true;
        var secret = InvoiceRow(tenantId, otherId, "INV-SECRET", 9000m, 0m, InvoiceStatus.Sent, new DateTime(2026, 9, 1));

        db.Invoices.AddRange(sold, open, credit, proforma, draft, future, laterPay, deleted, secret);
        await db.SaveChangesAsync();

        var dashboard = await new CustomerPortalService(db).GetDashboardAsync(customerId);
        var statement = await new InvoiceService(db).GetCustomerStatementAsync(customerId);

        Assert.NotNull(statement);
        // Paid Access invoice closes to 0, open 1000, open credit -80, receipt after today still owing 400.
        Assert.Equal(1320m, statement.ClosingBalance);
        Assert.Equal(statement.ClosingBalance, dashboard.BalanceDue);
        Assert.DoesNotContain(dashboard.Invoices, i => i.InvoiceNumber == "INV-SECRET");
        Assert.DoesNotContain(dashboard.Invoices, i => i.CustomerId == otherId);
    }

    [Fact]
    public void UnavailableMessage_IsTheLockedLoginCopy()
    {
        Assert.Equal("This portal login is not available. Contact MET office.", CustomerPortalMessages.Unavailable);
        Assert.DoesNotContain("not linked", CustomerPortalMessages.Unavailable, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDashboardAsync_EmptyCustomer_ReturnsFriendlyEmpty()
    {
        await using var db = CreateContext(Guid.NewGuid());

        var dashboard = await new CustomerPortalService(db).GetDashboardAsync(Guid.Empty);

        Assert.True(dashboard.IsUnlinked);
        Assert.Equal(CustomerPortalMessages.Unlinked, dashboard.CustomerName);
        Assert.Empty(dashboard.Quotes);
        Assert.Empty(dashboard.Invoices);
        Assert.Equal(0, dashboard.OpenQuoteCount);
        Assert.Equal(0, dashboard.OpenInvoiceCount);
        Assert.Equal(0m, dashboard.BalanceDue);
    }

    [Fact]
    public async Task GetDashboardAsync_MissingCustomer_ReturnsFriendlyEmpty()
    {
        await using var db = CreateContext(Guid.NewGuid());

        var dashboard = await new CustomerPortalService(db).GetDashboardAsync(Guid.NewGuid());

        Assert.True(dashboard.IsUnlinked);
        Assert.Equal(CustomerPortalMessages.Unlinked, dashboard.CustomerName);
        Assert.Empty(dashboard.Quotes);
        Assert.Empty(dashboard.Invoices);
    }

    [Fact]
    public async Task GetDashboardAsync_SoftDeletedCustomer_ReturnsFriendlyEmpty()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        db.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            Name = "Wiped Hospital",
            IsDeleted = true
        });
        db.Quotes.Add(new Quote
        {
            TenantId = tenantId,
            CustomerId = customerId,
            QuoteNumber = "Q-GONE",
            Status = QuoteStatus.Sent,
            Total = 10m
        });
        db.Invoices.Add(new Invoice
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = "INV-GONE",
            Status = InvoiceStatus.Sent,
            Total = 10m
        });
        await db.SaveChangesAsync();

        var dashboard = await new CustomerPortalService(db).GetDashboardAsync(customerId);

        Assert.True(dashboard.IsUnlinked);
        Assert.Equal(CustomerPortalMessages.Unlinked, dashboard.CustomerName);
        Assert.DoesNotContain(dashboard.Quotes, q => q.QuoteNumber == "Q-GONE");
        Assert.DoesNotContain(dashboard.Invoices, i => i.InvoiceNumber == "INV-GONE");
    }

    [Fact]
    public async Task AcceptQuoteAsync_OnlySentQuoteForLinkedCustomer()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        db.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Hospital" });
        var sent = new Quote
        {
            TenantId = tenantId,
            CustomerId = customerId,
            QuoteNumber = "Q-SENT",
            Status = QuoteStatus.Sent,
            Total = 1000m
        };
        var draft = new Quote
        {
            TenantId = tenantId,
            CustomerId = customerId,
            QuoteNumber = "Q-DRAFT",
            Status = QuoteStatus.Draft
        };
        var other = new Quote
        {
            TenantId = tenantId,
            CustomerId = otherId,
            QuoteNumber = "Q-OTHER",
            Status = QuoteStatus.Sent
        };
        db.Quotes.AddRange(sent, draft, other);
        await db.SaveChangesAsync();

        var service = new CustomerPortalService(db);
        await service.AcceptQuoteAsync(customerId, sent.Id);

        Assert.Equal(QuoteStatus.Accepted, (await db.Quotes.FindAsync(sent.Id))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptQuoteAsync(customerId, draft.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptQuoteAsync(customerId, other.Id));
    }

    [Fact]
    public async Task ReportPaymentAsync_RejectsOverBalanceAndOtherCustomer()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = "INV-PAY",
            Status = InvoiceStatus.Sent,
            Total = 500m,
            AmountPaid = 100m
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var service = new CustomerPortalService(db);
        await service.ReportPaymentAsync(customerId, invoice.Id, 400m, "EFT-1");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReportPaymentAsync(customerId, invoice.Id, 401m, "EFT-2"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReportPaymentAsync(Guid.NewGuid(), invoice.Id, 10m, "EFT-3"));
    }

    private static Invoice InvoiceRow(
        Guid tenantId,
        Guid customerId,
        string number,
        decimal total,
        decimal amountPaid,
        InvoiceStatus status,
        DateTime invoiceDate,
        InvoiceDocumentType documentType = InvoiceDocumentType.Standard) =>
        new()
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = number,
            DocumentType = documentType,
            Status = status,
            InvoiceDate = invoiceDate,
            DueDate = invoiceDate.AddDays(30),
            Total = total,
            AmountPaid = amountPaid
        };

    private static AppDbContext CreateContext(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }
}
