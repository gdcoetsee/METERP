using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace METERP.Web.Tests;

/// <summary>
/// The office card and /help name the same cash-desk screens and rules.
/// Neither copy tells staff to seed or reset METERP_Dev.
/// </summary>
public class HelpCashDeskGuideTests : IClassFixture<MeterpWebApplicationFactory>
{
    private static readonly string[] CashDeskRules =
    [
        "Tick Includes VAT to type the gross figure: at 15%, R 115.00 stores ex-VAT R 100.00 and VAT R 15.00, and travel uses the same tick.",
        "The register From and To include those days, and the VAT column is the tax stored on the quote.",
        "The printed quote shows our VAT number and the customer VAT number when both are set. A blank customer VAT number is left off, and the quote still prints.",
        "Converting a quote sets the job quoted total to the VAT-inclusive quote total once, travel stays a job cost, and converting the same quote again is refused.",
        "Payment terms (days) sets the invoice due date (blank or 0 stays 30 days), and search matches the VAT number as well as the name, email, and phone.",
        "Export CSV uses the lines on the screen and the last balance is the closing balance; draft, proforma, and cancelled documents stay off it.",
        "Issue credit note on a draft so the VAT-inclusive credit reduces what the customer owes; a credit left in Draft does not.",
        "Credit this amount credits a VAT-inclusive part that cannot exceed the balance due and needs a reason, while Create credit note still credits the whole invoice.",
        "customers whose open credit notes exceed open invoices show in credit and are left off the overdue list. Draft credits are left out.",
        "Allocate one receipt splits one EFT across that customer's open invoices, the amounts must add up to the bank amount, and draft, proforma, cancelled, and credit notes are left off.",
        "Reverse needs a reason and puts the receipt back on the balance, and a receipt cannot be reversed twice.",
        "issuing a full credit of the only deposit clears the deposit flag while the job is still open, and a partial credit that still covers the deposit does not.",
        "Reversing a deposit receipt clears that flag only when the deposit is no longer fully paid and no other deposit still covers it; a closed job is left unchanged.",
        "Chasing an overdue invoice states the net still owed after receipts and open credit notes.",
        "A paid invoice, or one already covered by credit, is not chased.",
        "The job card shows TRFid, job card number, team, region, and customer order, and a missing value is a dash.",
        "The customer order number saves on the job; blank stays empty and does not change quote or invoice totals.",
        "The register shows on hand, reorder level, and shortfall. Inactive items stay off the list.",
        "On Purchase orders, Still to receive lists open lines with ordered, received, and outstanding. Fully received and cancelled orders are left off.",
        "On GRV Register, the delivery note saved with the goods receipt is shown. A GRV with no note shows a dash.",
        "the weekly timesheet is Monday to Sunday, hours sit on the posted day, and closed-job hours stay. The total equals the cells.",
        "On the customer portal, the outstanding balance matches that customer's statement as at today.",
        "Output VAT uses the tax already stored, credit notes reduce the total, and the CSV total matches the screen."
    ];

    private static readonly string[] SeedInstructions =
    [
        "METERP_SEED_DEMO",
        "METERP_SEED_RESET",
        "METERP_Dev",
        "EnsureDeleted",
        "Seed:Demo",
        "Seed:ForceReset"
    ];

    private readonly HttpClient _client;

    public HelpCashDeskGuideTests(MeterpWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public void OfficeCard_AndHelpPage_NameTheSameCashDeskRules_AndDoNotSeed()
    {
        var guide = File.ReadAllText(Path.Combine(RepoRoot, "docs", "USER_GUIDE.md"));
        var help = File.ReadAllText(Path.Combine(RepoRoot, "src", "METERP.Web", "Components", "Pages", "Help.razor"));

        Assert.Contains("## Cash desk rules", guide, StringComparison.Ordinal);
        Assert.Contains("id=\"cash-rules\"", help, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"help-cash-rules\"", help, StringComparison.Ordinal);

        foreach (var rule in CashDeskRules)
        {
            Assert.Contains(rule, guide, StringComparison.Ordinal);
            Assert.Contains(rule, help, StringComparison.Ordinal);
        }

        foreach (var forbidden in SeedInstructions)
        {
            Assert.DoesNotContain(forbidden, guide, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, help, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Help_LoadsTheCashDeskRules_WithoutAskingToSeed()
    {
        var response = await _client.GetAsync("/help");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("help-ready", body, StringComparison.Ordinal);
        Assert.Contains("help-cash-rules", body, StringComparison.Ordinal);
        Assert.Contains("Tick Includes VAT to type the gross figure", body, StringComparison.Ordinal);
        Assert.Contains("Issue credit note on a draft", body, StringComparison.Ordinal);
        Assert.Contains("outstanding balance matches that customer's statement", body, StringComparison.Ordinal);

        foreach (var forbidden in SeedInstructions)
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
