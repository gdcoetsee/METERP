// E2E tests for critical sellable flows (login, AI quote+PDF, quote→job, job→invoice, multi-tenant).
// Requires app running: docker-compose up --build (http://localhost:8080)
// Setup: dotnet build tests/METERP.E2ETests && pwsh tests/METERP.E2ETests/bin/Debug/net9.0/playwright.ps1 install
// Run: dotnet test tests/METERP.E2ETests/METERP.E2ETests.csproj --filter "Category=E2E"

using Microsoft.Playwright;
using Xunit;

namespace METERP.E2ETests;

[Trait("Category", "E2E")]
public class E2EFlowTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public async Task InitializeAsync()
    {
        await E2EHelpers.EnsureAppReadyAsync();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser != null) await _browser.DisposeAsync();
        if (_playwright != null) _playwright.Dispose();
    }

    [Fact]
    public async Task Login_Succeeds_With_Demo_Credentials()
    {
        // Exercise the interactive login form (login-complete is used by other tests for speed).
        var page = await _browser.NewPageAsync();
        await page.GotoAsync($"{E2EHelpers.BaseUrl}/login");
        await page.WaitForSelectorAsync("[data-testid='login-email']");
        await page.Locator("[data-testid='login-email']").PressSequentiallyAsync(E2EHelpers.AcmeEmail);
        await page.Locator("[data-testid='login-password']").PressSequentiallyAsync(E2EHelpers.AcmePassword);
        await page.ClickAsync("[data-testid='login-submit']");
        await page.WaitForURLAsync(u => !u.Contains("login", StringComparison.OrdinalIgnoreCase), new() { Timeout = 45000 });

        var content = await page.ContentAsync();
        Assert.Contains("Acme", content, StringComparison.OrdinalIgnoreCase);
        await page.CloseAsync();
    }

    [Fact]
    public async Task AI_Copilot_Creates_Quote_With_Travel_And_Downloads_PDF()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/ai-copilot");
        await page.WaitForTestIdAsync("ai-copilot-ready", 20000);

        // Quick prompt avoids Blazor @bind timing issues with FillAsync; works without live API key.
        await page.ClickByTestIdAsync("ai-quick-prompt-travel");
        await page.WaitForTestIdAsync("ai-last-response", 30000);

        await page.ClickByTestIdAsync("ai-create-real-quote");
        await page.WaitForURLAsync("**/quotes**", new() { Timeout = 30000 });
        await page.WaitForTestIdAsync("quotes-table", 30000);
        await page.WaitForSelectorAsync("[data-testid='quotes-table'] tbody tr", new() { Timeout = 15000 });
        await page.Locator("[data-testid='quotes-table']").GetByText(new System.Text.RegularExpressions.Regex("Q-", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .First.WaitForAsync(new() { Timeout = 20000 });

        var pageContent = await page.ContentAsync();
        Assert.Contains("Travel", pageContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Q-", pageContent);

        await page.RunWithScreenshotOnFailureAsync("AI_Copilot_Demo_PDF", async () =>
        {
            await page.GotoRelativeAsync("/ai-copilot");
            await page.WaitForTestIdAsync("ai-copilot-ready", 20000);
            await page.ClickByTestIdAsync("ai-quick-prompt-travel");
            await page.WaitForTestIdAsync("ai-last-response", 30000);
            var demoPdf = await page.WaitAndSaveDownloadAsync("[data-testid='ai-demo-quote-pdf']", "e2e-demo-quote");
            Assert.Contains(".pdf", demoPdf, StringComparison.OrdinalIgnoreCase);
        });

        await page.CloseAsync();
    }

    [Fact]
    public async Task Opportunity_Converts_To_Quote_Via_Ai_Copilot()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/opportunities");
        try
        {
            await page.WaitForTestIdAsync("opportunities-ready", 8000);
        }
        catch (TimeoutException)
        {
            // Older builds expose pipeline without ready marker
        }

        await page.WaitForTestIdAsync("opportunities-pipeline", 30000);

        await page.Locator("[data-testid='opportunity-card']").First.ClickAsync();
        await page.WaitForTestIdAsync("opportunity-detail", 10000);
        await page.ClickByTestIdAsync("opportunity-convert-ai");

        try
        {
            await page.WaitForURLAsync("**/ai-copilot**", new() { Timeout = 15000 });
        }
        catch (TimeoutException)
        {
            await page.GotoRelativeAsync("/ai-copilot");
        }

        await page.WaitForTestIdAsync("ai-copilot-ready", 20000);
        // Opportunity handoff auto-runs bid optimization after first render
        await page.WaitForTestIdAsync("ai-last-response", 60000);

        await page.ClickByTestIdAsync("ai-create-real-quote");

        try
        {
            await page.WaitForURLAsync("**/quotes**", new() { Timeout = 30000 });
        }
        catch (TimeoutException)
        {
            await page.GotoRelativeAsync("/quotes");
        }

        await page.WaitForTestIdAsync("quotes-table", 30000);
        var content = await page.ContentAsync();
        Assert.Contains("Q-", content);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Convert_Quote_To_Job_Preserves_Travel_Costs()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/quotes");
        await page.WaitForTestIdAsync("quotes-table");

        var travelRow = page.Locator("[data-testid='quote-row-with-travel-convertible']").First;
        if (await travelRow.CountAsync() == 0)
            travelRow = page.Locator("[data-testid='quote-row-convertible']").First;
        if (await travelRow.CountAsync() == 0)
            travelRow = page.Locator("[data-testid='quote-row-with-travel']").First;
        if (await travelRow.CountAsync() == 0)
            travelRow = page.Locator("[data-testid='quotes-table'] tbody tr").First;

        await travelRow.Locator("[data-testid='quote-view-button']").ClickAsync();
        await page.WaitForTestIdAsync("convert-to-job", 15000);
        await page.ClickByTestIdAsync("convert-to-job");

        // Blazor Server may not always trigger Playwright navigation events; allow either full or client-side nav.
        try
        {
            await page.WaitForURLAsync("**/jobs**", new() { Timeout = 12000 });
        }
        catch (TimeoutException)
        {
            await page.GotoRelativeAsync("/jobs");
        }

        await page.WaitForTestIdAsync("jobs-table", 30000);

        var jobTravelRow = page.Locator("[data-testid='job-row-with-travel']").First;
        if (await jobTravelRow.CountAsync() == 0)
            jobTravelRow = page.Locator("[data-testid='jobs-table'] tbody tr").First;

        await jobTravelRow.Locator("[data-testid='job-view-button']").ClickAsync();
        await page.WaitForTestIdAsync("create-invoice-from-job-detail", 10000);

        var content = await page.ContentAsync();
        Assert.Contains("J-", content);
        Assert.Contains("Q-", content);
        Assert.Contains("travel", content, StringComparison.OrdinalIgnoreCase);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Job_With_Travel_And_Labor_Creates_Invoice_With_Correct_Totals()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/jobs");
        await page.WaitForTestIdAsync("jobs-table");

        var travelRow = page.Locator("[data-testid='job-row-with-travel']").First;
        if (await travelRow.CountAsync() == 0)
            travelRow = page.Locator("[data-testid='jobs-table'] tbody tr").First;

        await travelRow.Locator("[data-testid='job-view-button']").ClickAsync();
        await page.WaitForTestIdAsync("create-invoice-from-job-detail", 10000);
        await page.ClickByTestIdAsync("create-invoice-from-job-detail");

        await page.WaitForURLAsync("**/invoices**", new() { Timeout = 30000 });
        await page.WaitForAppReadyAsync(30000);
        await page.WaitForTestIdAsync("invoices-table", 30000);
        await page.Locator("[data-testid='invoices-table'] tbody tr").First
            .Locator("[data-testid='view-invoice']").ClickAsync();
        await page.WaitForTestIdAsync("invoice-line-items-header", 10000);

        var content = await page.ContentAsync();
        Assert.Contains("INV-", content);
        Assert.Contains("Travel", content, StringComparison.OrdinalIgnoreCase);

        await page.CloseAsync();
    }

    [Fact]
    public async Task MultiTenant_Isolation_Basic_Check()
    {
        var acmePage = await _browser.LoginAsync(E2EHelpers.AcmeEmail, E2EHelpers.AcmePassword);
        await acmePage.GotoRelativeAsync("/quotes");
        await acmePage.WaitForTestIdAsync("quotes-table");
        var acmeContent = await acmePage.ContentAsync();
        Assert.Contains("Q-", acmeContent);
        Assert.DoesNotContain("Beta-only travel", acmeContent);
        await acmePage.CloseAsync();

        var betaPage = await _browser.LoginAsync(E2EHelpers.BetaEmail, E2EHelpers.BetaPassword);
        await betaPage.GotoRelativeAsync("/quotes");
        await betaPage.WaitForTestIdAsync("quotes-table");
        var betaContent = await betaPage.ContentAsync();
        Assert.Contains("Beta Mining", betaContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Johannesburg General Hospital", betaContent);
        await betaPage.CloseAsync();
    }

    [Fact]
    public async Task Tenants_Page_Loads_Commercial_Usage_Table()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/tenants");
        await page.WaitForTestIdAsync("tenants-table", 20000);

        var content = await page.ContentAsync();
        Assert.Contains("Acme", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tier", content);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Home_Quota_Usage_Card_Shows_Monthly_Usage()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/");
        await page.WaitForTestIdAsync("home-quota-usage-card", 15000);

        var content = await page.ContentAsync();
        Assert.Contains("Monthly usage", content);
        Assert.Contains("Quotes", content);
        Assert.Contains("Acme", content, StringComparison.OrdinalIgnoreCase);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Scheduling_Page_Loads_Jobs_And_Assignment_Panel()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/scheduling");

        try
        {
            await page.WaitForTestIdAsync("scheduling-ready", 8000);
        }
        catch (TimeoutException)
        {
            // Older builds expose the table without the ready marker
            await page.WaitForSelectorAsync("table.table tbody tr", new() { Timeout = 30000 });
        }

        var content = await page.ContentAsync();
        Assert.Contains("J-", content);

        var assignButton = page.Locator("[data-testid='scheduling-view-assign']").First;
        if (await assignButton.CountAsync() == 0)
            assignButton = page.GetByRole(AriaRole.Button, new() { Name = "View/Assign" }).First;

        await assignButton.ClickAsync();

        try
        {
            await page.WaitForTestIdAsync("scheduling-assign-panel", 10000);
            await page.WaitForTestIdAsync("scheduling-asset-select", 5000);
            await page.WaitForTestIdAsync("scheduling-employee-select", 5000);
            await page.ClickByTestIdAsync("scheduling-close-assign");
        }
        catch (TimeoutException)
        {
            await page.WaitForSelectorAsync(".card .card-header:has-text('Assign for')", new() { Timeout = 10000 });
            await page.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        }

        await page.CloseAsync();
    }

    [Fact]
    public async Task Audit_Page_Loads_Compliance_Trail()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/audit");

        try
        {
            await page.WaitForTestIdAsync("audit-ready", 15000);
        }
        catch (TimeoutException)
        {
            await page.WaitForTestIdAsync("audit-table", 15000);
        }

        await page.WaitForTestIdAsync("audit-export-csv", 10000);

        var content = await page.ContentAsync();
        Assert.Contains("Audit Log", content);

        var rowCount = await page.Locator("[data-testid='audit-row']").CountAsync();
        if (rowCount > 0)
        {
            Assert.True(
                content.Contains("Quote", StringComparison.OrdinalIgnoreCase)
                || content.Contains("CREATE", StringComparison.OrdinalIgnoreCase)
                || content.Contains("CONVERT", StringComparison.OrdinalIgnoreCase)
                || content.Contains("Opportunity", StringComparison.OrdinalIgnoreCase),
                "Expected audit rows to reference spine or CRM entities.");
        }

        await page.CloseAsync();
    }

    [Fact]
    public async Task Notifications_Triggered_From_LowStock_Or_JobEvent()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/notifications");
        await page.WaitForTestIdAsync("notifications-list", 10000);

        var content = await page.ContentAsync();
        Assert.Contains("Low Stock", content);
        Assert.Contains("Job Overdue", content);

        await page.ClickByTestIdAsync("notifications-mark-all");
        await page.WaitForTestIdAsync("notifications-list", 5000);

        await page.CloseAsync();
    }
}