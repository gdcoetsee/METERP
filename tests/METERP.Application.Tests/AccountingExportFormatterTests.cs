using METERP.Application.Accounting;
using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class AccountingExportFormatterTests
{
    [Fact]
    public void SageCsv_IncludesSalesInvoiceRow_WithVat()
    {
        var invoice = SampleInvoice();
        var csv = AccountingExportFormatter.BuildSageCsv([invoice], "200");

        Assert.Contains("Sales Invoice", csv);
        Assert.Contains("Johannesburg General Hospital", csv);
        Assert.Contains("INV-100", csv);
        Assert.Contains("Travel", csv);
        Assert.Contains("200", csv);
    }

    [Fact]
    public void XeroCsv_UsesOfficialImportHeaders()
    {
        var invoice = SampleInvoice();
        var csv = AccountingExportFormatter.BuildXeroCsv([invoice], "4000");

        Assert.StartsWith("*ContactName,*InvoiceNumber,*InvoiceDate,*DueDate", csv);
        Assert.Contains("INV-100", csv);
        Assert.Contains("4000", csv);
        Assert.Contains("OUTPUT2", csv);
        Assert.Contains("ZAR", csv);
    }

    [Fact]
    public void BuildCsv_None_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AccountingExportFormatter.BuildCsv(AccountingProvider.None, [SampleInvoice()], "200"));
    }

    [Fact]
    public void DraftInvoices_AreOmitted()
    {
        var invoice = SampleInvoice();
        invoice.Status = InvoiceStatus.Draft;
        var csv = AccountingExportFormatter.BuildSageCsv([invoice], "200");
        Assert.DoesNotContain("INV-100", csv);
    }

    private static Invoice SampleInvoice() => new()
    {
        InvoiceNumber = "INV-100",
        InvoiceDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        DueDate = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
        Status = InvoiceStatus.Sent,
        TaxRate = 0.15m,
        Subtotal = 1000m,
        Customer = new Customer { Name = "Johannesburg General Hospital" },
        Lines =
        {
            new InvoiceLine { Description = "Travel", Quantity = 1, UnitPrice = 620m }
        }
    };
}
