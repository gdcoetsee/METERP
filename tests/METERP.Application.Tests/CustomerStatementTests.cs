using System.Globalization;
using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class CustomerStatementTests
{
    private static readonly DateTime AsOf = new(2026, 10, 9);

    [Fact]
    public void Build_AccessPaidInvoice_WithNoReceipt_ClosesToZero()
    {
        var statement = CustomerStatementBuilder.Build(
            Guid.NewGuid(),
            "  Metro Power  ",
            AsOf,
            new[]
            {
                Doc("INV-100", new DateTime(2026, 9, 1), 1150m, amountPaid: 1150m, status: InvoiceStatus.Paid)
            });

        Assert.Equal("Metro Power", statement.CustomerName);
        Assert.Equal(new DateTime(2026, 10, 9), statement.AsOf);
        Assert.Equal(2, statement.Lines.Count);
        Assert.Equal(("Invoice", 1150m, 0m), (statement.Lines[0].Kind, statement.Lines[0].Debit, statement.Lines[0].Credit));
        Assert.Equal(("Collected", 0m, 1150m), (statement.Lines[1].Kind, statement.Lines[1].Debit, statement.Lines[1].Credit));
        Assert.Equal("On the invoice, no receipt row", statement.Lines[1].Detail);
        Assert.Equal(0m, statement.ClosingBalance);
        Assert.Equal("R 0.00", statement.ClosingBalanceDisplay);
    }

    [Fact]
    public void Build_MixedBook_MatchesSignedOpenBalance_AndSkipsNonArDocuments()
    {
        var documents = new[]
        {
            Doc("INV-OPEN", new DateTime(2026, 8, 1), 1000m),
            Doc("INV-SOLD", new DateTime(2026, 8, 2), 1150m, amountPaid: 1150m, status: InvoiceStatus.Paid),
            Doc("INV-PART", new DateTime(2026, 8, 3), 500m, amountPaid: 200m, status: InvoiceStatus.PartiallyPaid,
                payments: new[] { Receipt(new DateTime(2026, 8, 10), 200m, "EFT123") }),
            Doc("CRN-OPEN", new DateTime(2026, 8, 4), 80m, type: InvoiceDocumentType.CreditNote),
            Doc("CRN-SOLD", new DateTime(2026, 8, 5), 100m, amountPaid: 100m, status: InvoiceStatus.Paid,
                type: InvoiceDocumentType.CreditNote),
            Doc("INV-DRAFT", new DateTime(2026, 8, 6), 999m, status: InvoiceStatus.Draft),
            Doc("INV-PRO", new DateTime(2026, 8, 6), 50m, type: InvoiceDocumentType.Proforma, status: InvoiceStatus.Sent),
            Doc("INV-CAN", new DateTime(2026, 8, 6), 40m, status: InvoiceStatus.Cancelled),
            Doc("INV-FUTURE", new DateTime(2026, 10, 20), 70m),
            Doc("INV-LATER-PAY", new DateTime(2026, 9, 1), 400m, amountPaid: 150m, status: InvoiceStatus.PartiallyPaid,
                payments: new[] { Receipt(new DateTime(2026, 10, 20), 150m, "FUTURE") })
        };

        var statement = CustomerStatementBuilder.Build(Guid.NewGuid(), "MET Customer", AsOf, documents);

        Assert.DoesNotContain(statement.Lines, l => l.Reference is "INV-DRAFT" or "INV-PRO" or "INV-CAN" or "INV-FUTURE" or "FUTURE");
        Assert.Contains(statement.Lines, l => l.Kind == "Receipt" && l.Reference == "EFT123" && l.Credit == 200m);
        Assert.Contains(statement.Lines, l => l.Kind == "Credit note" && l.Reference == "CRN-OPEN" && l.Credit == 80m);
        Assert.Contains(statement.Lines, l => l.Kind == "Credit applied" && l.Reference == "CRN-SOLD" && l.Debit == 100m);
        Assert.Contains(statement.Lines, l => l.Kind == "Invoice" && l.Reference == "INV-LATER-PAY" && l.Debit == 400m);

        // Open 1000 + sold 0 + partial 300 + open credit -80 + settled credit 0 + later-pay still 400.
        Assert.Equal(1620m, statement.ClosingBalance);
        Assert.Equal(ExpectedOpenBalance(documents), statement.ClosingBalance);
        Assert.Equal("R 1,620.00", statement.ClosingBalanceDisplay);
        Assert.Equal("Deposit, VAT-inclusive", CustomerStatementBuilder.Build(
            Guid.NewGuid(),
            "X",
            AsOf,
            new[] { Doc("DEP-1", new DateTime(2026, 9, 1), 300m, type: InvoiceDocumentType.Deposit) }).Lines[0].Detail);
    }

    [Fact]
    public void Build_OpenCreditOnly_ShowsParentheses()
    {
        var statement = CustomerStatementBuilder.Build(
            Guid.NewGuid(),
            "",
            AsOf,
            new[] { Doc("CRN-1", new DateTime(2026, 9, 1), 80m, type: InvoiceDocumentType.CreditNote) });

        Assert.Equal("Customer", statement.CustomerName);
        Assert.Equal(-80m, statement.ClosingBalance);
        Assert.Equal("(R 80.00)", statement.ClosingBalanceDisplay);
    }

    [Fact]
    public void Build_EmptyCustomer_IsZero()
    {
        var statement = CustomerStatementBuilder.Build(Guid.NewGuid(), "Empty", AsOf, null);

        Assert.Empty(statement.Lines);
        Assert.Equal(0m, statement.ClosingBalance);
    }

    [Fact]
    public void ToCsv_MatchesBuilderLines_AndClosingBalance_WithNoSecondTotal()
    {
        var statement = CustomerStatementBuilder.Build(
            Guid.NewGuid(),
            "  Metro Power  ",
            AsOf,
            new[]
            {
                Doc("INV-100", new DateTime(2026, 9, 1), 1150m, amountPaid: 1150m, status: InvoiceStatus.Paid),
                Doc("CRN-1", new DateTime(2026, 9, 2), 80m, type: InvoiceDocumentType.CreditNote)
            });

        var csv = statement.ToCsv();
        var rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(CustomerStatement.CsvHeader, rows[0]);
        Assert.Equal(statement.Lines.Count + 1, rows.Length);
        Assert.Equal("statement-Metro-Power-20261009.csv", statement.FileName);

        Assert.Equal("2026-09-01,Invoice,INV-100,VAT-inclusive,1150.00,0.00,1150.00", rows[1]);
        var collected = statement.Lines[1];
        Assert.Equal("Collected", collected.Kind);
        Assert.Equal(
            $"2026-09-01,Collected,INV-100,\"On the invoice, no receipt row\",{CustomerStatement.FormatAmount(collected.Debit)},{CustomerStatement.FormatAmount(collected.Credit)},{CustomerStatement.FormatAmount(collected.RunningBalance)}",
            rows[2]);

        for (var i = 0; i < statement.Lines.Count; i++)
        {
            var cells = SplitCsv(rows[i + 1]);
            var line = statement.Lines[i];
            Assert.Equal(line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cells[0]);
            Assert.Equal(line.Kind, cells[1]);
            Assert.Equal(line.Reference, cells[2]);
            Assert.Equal(line.Detail, cells[3]);
            Assert.Equal(line.Debit, decimal.Parse(cells[4], CultureInfo.InvariantCulture));
            Assert.Equal(line.Credit, decimal.Parse(cells[5], CultureInfo.InvariantCulture));
            Assert.Equal(line.RunningBalance, decimal.Parse(cells[6], CultureInfo.InvariantCulture));
        }

        var closingInFile = decimal.Parse(SplitCsv(rows[^1])[6], CultureInfo.InvariantCulture);
        Assert.Equal(statement.ClosingBalance, closingInFile);
        Assert.Equal(-80m, closingInFile);
        Assert.DoesNotContain(rows, row => row.Contains("Total", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToCsv_EmptyStatement_IsHeaderOnly()
    {
        var statement = CustomerStatementBuilder.Build(Guid.NewGuid(), "Empty", AsOf, null);

        var rows = statement.ToCsv().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { CustomerStatement.CsvHeader }, rows);
        Assert.Equal(0m, statement.ClosingBalance);
    }

    private static string[] SplitCsv(string row)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < row.Length; i++)
        {
            var ch = row[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < row.Length && row[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                quoted = true;
                continue;
            }

            if (ch == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        cells.Add(current.ToString());
        return cells.ToArray();
    }

    [Fact]
    public async Task GetCustomerStatementAsync_HidesOtherTenant_AndDeleted_AndUnknownCustomer()
    {
        var database = $"statement-{Guid.NewGuid():N}";
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        Guid otherCustomerId;

        await using (var otherDb = CreateDb(database, otherTenantId))
        {
            var otherCustomer = new Customer { Name = "Other Co" };
            otherDb.Set<Customer>().Add(otherCustomer);
            otherDb.Set<Invoice>().Add(InvoiceRow(otherTenantId, otherCustomer.Id, "INV-THEIRS", 999m, 0m, InvoiceStatus.Sent));
            await otherDb.SaveChangesAsync();
            otherCustomerId = otherCustomer.Id;
        }

        await using var db = CreateDb(database, tenantId);
        var service = new InvoiceService(db);
        var customer = new Customer { Name = "Substation Co" };
        db.Set<Customer>().Add(customer);
        db.Set<Invoice>().Add(InvoiceRow(tenantId, customer.Id, "INV-OURS", 1150m, 1150m, InvoiceStatus.Paid));
        db.Set<Invoice>().Add(new Invoice
        {
            CustomerId = customer.Id,
            InvoiceNumber = "INV-GONE",
            DocumentType = InvoiceDocumentType.Standard,
            Status = InvoiceStatus.Sent,
            InvoiceDate = new DateTime(2026, 9, 1),
            DueDate = new DateTime(2026, 10, 1),
            Total = 500m,
            AmountPaid = 0m,
            IsDeleted = true
        });
        await db.SaveChangesAsync();

        var statement = await service.GetCustomerStatementAsync(customer.Id, AsOf);
        Assert.NotNull(statement);
        Assert.Equal("Substation Co", statement.CustomerName);
        Assert.Equal(0m, statement.ClosingBalance);
        Assert.Contains(statement.Lines, l => l.Reference == "INV-OURS");
        Assert.DoesNotContain(statement.Lines, l => l.Reference is "INV-GONE" or "INV-THEIRS");

        Assert.Null(await service.GetCustomerStatementAsync(Guid.NewGuid(), AsOf));
        Assert.Null(await service.GetCustomerStatementAsync(Guid.Empty, AsOf));
        Assert.Null(await service.GetCustomerStatementAsync(otherCustomerId, AsOf));
    }

    [Fact]
    public async Task GetCustomerStatementAsync_IncludesReceiptRow()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Receipt Co" };
            db.Set<Customer>().Add(customer);
            var invoice = InvoiceRow(tenantId, customer.Id, "INV-R", 500m, 200m, InvoiceStatus.PartiallyPaid);
            invoice.Payments.Add(new InvoicePayment
            {
                TenantId = tenantId,
                Invoice = invoice,
                Amount = 200m,
                PaymentDate = new DateTime(2026, 9, 15),
                Reference = "POP-9"
            });
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            var statement = await service.GetCustomerStatementAsync(customer.Id, AsOf);

            Assert.NotNull(statement);
            Assert.Equal(300m, statement.ClosingBalance);
            var receipt = Assert.Single(statement.Lines, l => l.Kind == "Receipt");
            Assert.Equal("POP-9", receipt.Reference);
            Assert.Equal(200m, receipt.Credit);
            Assert.DoesNotContain(statement.Lines, l => l.Kind == "Collected");
        }
    }

    private static decimal ExpectedOpenBalance(IEnumerable<CustomerStatementDocument> documents)
    {
        decimal total = 0m;
        foreach (var document in documents)
        {
            if (document.IsDeleted || document.InvoiceDate.Date > AsOf.Date)
                continue;
            if (document.DocumentType == InvoiceDocumentType.Proforma)
                continue;
            if (document.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
                continue;

            var paidForAr = document.AmountPaid;
            if (document.DocumentType != InvoiceDocumentType.CreditNote)
            {
                var futureReceipts = document.Payments
                    .Where(p => !p.IsDeleted && p.PaymentDate.Date > AsOf.Date)
                    .Sum(p => Math.Abs(p.Amount));
                paidForAr = Math.Max(0m, document.AmountPaid - futureReceipts);
            }

            total += InvoiceCreditConvention.ArSignedOpenBalance(
                document.DocumentType,
                document.Status,
                document.Total,
                paidForAr);
        }

        return Math.Round(total, 2);
    }

    private static CustomerStatementDocument Doc(
        string number,
        DateTime date,
        decimal total,
        decimal amountPaid = 0m,
        InvoiceStatus status = InvoiceStatus.Sent,
        InvoiceDocumentType type = InvoiceDocumentType.Standard,
        IReadOnlyList<CustomerStatementReceipt>? payments = null) =>
        new(date, type, status, number, total, amountPaid, false, payments ?? Array.Empty<CustomerStatementReceipt>());

    private static CustomerStatementReceipt Receipt(DateTime date, decimal amount, string reference) =>
        new(date, amount, reference, false);

    private static Invoice InvoiceRow(
        Guid tenantId,
        Guid customerId,
        string number,
        decimal total,
        decimal amountPaid,
        InvoiceStatus status) =>
        new()
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = number,
            DocumentType = InvoiceDocumentType.Standard,
            Status = status,
            InvoiceDate = new DateTime(2026, 9, 1),
            DueDate = new DateTime(2026, 10, 1),
            Subtotal = total,
            Tax = 0m,
            Total = total,
            AmountPaid = amountPaid
        };

    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var db = CreateDb($"statement-{Guid.NewGuid():N}", tenantId);
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
