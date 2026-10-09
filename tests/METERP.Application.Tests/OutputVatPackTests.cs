using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class OutputVatPackTests
{
    private static readonly DateTime From = new(2026, 9, 1);
    private static readonly DateTime To = new(2026, 9, 30);

    [Fact]
    public void Build_Gross115_ContributesStoredVat15_NotAFreshSplit()
    {
        var stored = OutputVatPackBuilder.Build(From, To, new[]
        {
            Doc("INV-115", new DateTime(2026, 9, 2, 15, 0, 0), 100m, 15m, 115m)
        });

        Assert.Equal(100m, stored.Lines[0].Net);
        Assert.Equal(15m, stored.Lines[0].Vat);
        Assert.Equal(115m, stored.Lines[0].Gross);
        Assert.Equal(15m, stored.VatTotal);

        var notResplit = OutputVatPackBuilder.Build(From, To, new[]
        {
            Doc("INV-ODD", new DateTime(2026, 9, 2), 100.50m, 14.50m, 115m)
        });
        Assert.Equal(14.50m, notResplit.VatTotal);
        Assert.Equal(100.50m, notResplit.NetTotal);
        Assert.Equal(115m, notResplit.GrossTotal);
    }

    [Fact]
    public void Build_CreditNote_SubtractsStoredTaxAndTotal()
    {
        var pack = OutputVatPackBuilder.Build(From, To, new[]
        {
            Doc("INV-115", new DateTime(2026, 9, 2), 100m, 15m, 115m),
            Doc("CRN-115", new DateTime(2026, 9, 3), 100m, 15m, 115m, type: InvoiceDocumentType.CreditNote),
            Doc("INV-PART", new DateTime(2026, 9, 4), 200m, 30m, 230m, status: InvoiceStatus.PartiallyPaid, type: InvoiceDocumentType.Partial),
            Doc("INV-PAID", new DateTime(2026, 9, 5), 10m, 1.50m, 11.50m, status: InvoiceStatus.Paid),
            Doc("INV-OD", new DateTime(2026, 9, 6), 40m, 6m, 46m, status: InvoiceStatus.Overdue),
            Doc("DEP-1", new DateTime(2026, 9, 10), 20m, 3m, 23m, type: InvoiceDocumentType.Deposit)
        });

        var credit = Assert.Single(pack.Lines, l => l.Number == "CRN-115");
        Assert.Equal(-100m, credit.Net);
        Assert.Equal(-15m, credit.Vat);
        Assert.Equal(-115m, credit.Gross);

        Assert.Equal(270m, pack.NetTotal);
        Assert.Equal(40.50m, pack.VatTotal);
        Assert.Equal(310.50m, pack.GrossTotal);
    }

    [Fact]
    public void Build_OmitsDraftProformaCancelledDeletedAndOutOfRange()
    {
        var pack = OutputVatPackBuilder.Build(From, To, new[]
        {
            Doc("INV-OK", new DateTime(2026, 9, 1), 100m, 15m, 115m),
            Doc("INV-END", new DateTime(2026, 9, 30, 18, 0, 0), 10m, 1.50m, 11.50m),
            Doc("INV-DRAFT", new DateTime(2026, 9, 8), 999m, 149.85m, 1148.85m, status: InvoiceStatus.Draft),
            Doc("INV-PRO", new DateTime(2026, 9, 8), 50m, 7.50m, 57.50m, status: InvoiceStatus.Sent, type: InvoiceDocumentType.Proforma),
            Doc("INV-CAN", new DateTime(2026, 9, 8), 40m, 6m, 46m, status: InvoiceStatus.Cancelled),
            Doc("INV-BEFORE", new DateTime(2026, 8, 31), 80m, 12m, 92m),
            Doc("INV-AFTER", new DateTime(2026, 10, 1), 80m, 12m, 92m),
            Doc("INV-DEL", new DateTime(2026, 9, 9), 70m, 10.50m, 80.50m, deleted: true),
            Doc("  ", new DateTime(2026, 9, 11), 8m, 1.20m, 9.20m, customer: "  ")
        });

        Assert.Equal(new[] { "INV-OK", "Document", "INV-END" }, pack.Lines.Select(l => l.Number).ToArray());
        Assert.Equal("Customer", pack.Lines[1].Customer);
        Assert.Equal(new DateTime(2026, 9, 1), pack.Lines[0].Date);
        Assert.Equal(new DateTime(2026, 9, 11), pack.Lines[1].Date);
        Assert.Equal(new DateTime(2026, 9, 30), pack.Lines[2].Date);
        Assert.DoesNotContain(pack.Lines, l => l.Number is "INV-DRAFT" or "INV-PRO" or "INV-CAN" or "INV-BEFORE" or "INV-AFTER" or "INV-DEL");
    }

    [Fact]
    public void CsvTotals_MatchTheFiguresShownOnScreen()
    {
        var pack = OutputVatPackBuilder.Build(From, To, new[]
        {
            Doc("INV-115", new DateTime(2026, 9, 2), 100m, 15m, 115m, customer: "Metro, Power"),
            Doc("CRN-1", new DateTime(2026, 9, 3), 40m, 6m, 46m, type: InvoiceDocumentType.CreditNote)
        });

        var csv = pack.ToCsv();
        var rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Date,Number,Customer,Net,VAT,Gross", rows[0]);
        Assert.Contains("\"Metro, Power\"", rows[1]);
        Assert.Equal("2026-09-02,INV-115,\"Metro, Power\",100.00,15.00,115.00", rows[1]);
        Assert.Equal("2026-09-03,CRN-1,Metro Power,-40.00,-6.00,-46.00", rows[2]);

        var detailNet = 0m;
        var detailVat = 0m;
        var detailGross = 0m;
        for (var i = 1; i < rows.Length - 1; i++)
        {
            var cells = SplitCsv(rows[i]);
            detailNet += decimal.Parse(cells[3], System.Globalization.CultureInfo.InvariantCulture);
            detailVat += decimal.Parse(cells[4], System.Globalization.CultureInfo.InvariantCulture);
            detailGross += decimal.Parse(cells[5], System.Globalization.CultureInfo.InvariantCulture);
        }

        Assert.Equal(pack.NetTotal, detailNet);
        Assert.Equal(pack.VatTotal, detailVat);
        Assert.Equal(pack.GrossTotal, detailGross);
        Assert.Equal(
            $",,Total,{OutputVatPack.FormatAmount(pack.NetTotal)},{OutputVatPack.FormatAmount(pack.VatTotal)},{OutputVatPack.FormatAmount(pack.GrossTotal)}",
            rows[^1]);
        Assert.Equal("output-vat-20260901-20260930.csv", pack.FileName);
    }

    [Fact]
    public void Build_EmptyRange_IsHeaderAndZeroTotal()
    {
        var pack = OutputVatPackBuilder.Build(From, To, null);
        Assert.Empty(pack.Lines);
        Assert.Equal(0m, pack.VatTotal);
        var rows = pack.ToCsv().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { "Date,Number,Customer,Net,VAT,Gross", ",,Total,0.00,0.00,0.00" }, rows);
    }

    [Fact]
    public void Build_EndBeforeStart_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OutputVatPackBuilder.Build(To, From, Array.Empty<OutputVatDocument>()));
        Assert.Equal("VAT pack end date is before the start date.", ex.Message);
    }

    [Fact]
    public async Task GetOutputVatPackAsync_UsesStoredTax_AndHidesOtherTenant()
    {
        var database = $"vat-{Guid.NewGuid():N}";
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        await using (var other = CreateDb(database, otherTenantId))
        {
            var otherCustomer = new Customer { Name = "Other Co" };
            other.Set<Customer>().Add(otherCustomer);
            await other.SaveChangesAsync();
            other.Set<Invoice>().Add(Invoice(otherCustomer.Id, "INV-OTHER", new DateTime(2026, 9, 2), 100m, 15m, 115m, InvoiceStatus.Sent));
            await other.SaveChangesAsync();
        }

        await using var db = CreateDb(database, tenantId);
        var customer = new Customer { Name = "Metro Power" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        db.Set<Invoice>().AddRange(
            Invoice(customer.Id, "INV-115", new DateTime(2026, 9, 2), 100m, 15m, 115m, InvoiceStatus.Sent),
            Invoice(customer.Id, "CRN-115", new DateTime(2026, 9, 3), 100m, 15m, 115m, InvoiceStatus.Sent, InvoiceDocumentType.CreditNote),
            Invoice(customer.Id, "INV-DRAFT", new DateTime(2026, 9, 4), 80m, 12m, 92m, InvoiceStatus.Draft),
            Invoice(customer.Id, "INV-PRO", new DateTime(2026, 9, 4), 50m, 7.50m, 57.50m, InvoiceStatus.Sent, InvoiceDocumentType.Proforma),
            Invoice(customer.Id, "INV-CAN", new DateTime(2026, 9, 4), 40m, 6m, 46m, InvoiceStatus.Cancelled),
            Invoice(customer.Id, "INV-BEFORE", new DateTime(2026, 8, 31), 80m, 12m, 92m, InvoiceStatus.Sent),
            Invoice(customer.Id, "INV-AFTER", new DateTime(2026, 10, 1), 80m, 12m, 92m, InvoiceStatus.Paid),
            Invoice(customer.Id, "INV-DEL", new DateTime(2026, 9, 9), 70m, 10.50m, 80.50m, InvoiceStatus.Sent, deleted: true));
        await db.SaveChangesAsync();

        var pack = await new FinanceService(db).GetOutputVatPackAsync(From, To);

        Assert.Equal(new[] { "INV-115", "CRN-115" }, pack.Lines.Select(l => l.Number).ToArray());
        Assert.Equal(0m, pack.NetTotal);
        Assert.Equal(0m, pack.VatTotal);
        Assert.Equal(0m, pack.GrossTotal);
        Assert.Equal(15m, pack.Lines[0].Vat);
        Assert.Equal(-15m, pack.Lines[1].Vat);

        var totalLine = pack.ToCsv().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];
        Assert.Equal(
            $",,Total,{OutputVatPack.FormatAmount(pack.NetTotal)},{OutputVatPack.FormatAmount(pack.VatTotal)},{OutputVatPack.FormatAmount(pack.GrossTotal)}",
            totalLine);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FinanceService(db).GetOutputVatPackAsync(To, From));
    }

    private static OutputVatDocument Doc(
        string number,
        DateTime date,
        decimal net,
        decimal vat,
        decimal gross,
        InvoiceStatus status = InvoiceStatus.Sent,
        InvoiceDocumentType type = InvoiceDocumentType.Standard,
        string? customer = "Metro Power",
        bool deleted = false) =>
        new(date, type, status, number, customer, net, vat, gross, deleted);

    private static Invoice Invoice(
        Guid customerId,
        string number,
        DateTime date,
        decimal net,
        decimal vat,
        decimal gross,
        InvoiceStatus status,
        InvoiceDocumentType type = InvoiceDocumentType.Standard,
        bool deleted = false) =>
        new()
        {
            CustomerId = customerId,
            InvoiceNumber = number,
            InvoiceDate = date,
            DueDate = date.AddDays(30),
            Status = status,
            DocumentType = type,
            Subtotal = net,
            Tax = vat,
            Total = gross,
            IsDeleted = deleted
        };

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
