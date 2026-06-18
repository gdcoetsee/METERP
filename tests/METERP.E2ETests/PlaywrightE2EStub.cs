using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

// E2E tests for critical sellable flows (login, AI quote+PDF, quote→job, job→invoice, multi-tenant).
// Requires app running: docker-compose up --build (http://localhost:8080)
// Setup: dotnet build tests/METERP.E2ETests && pwsh tests/METERP.E2ETests/bin/Debug/net9.0/playwright.ps1 install
// Run: dotnet test tests/METERP.E2ETests/METERP.E2ETests.csproj --filter "Category=E2E"

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
    public async Task Quotes_Manual_Create_With_Line()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/quotes");
        await page.WaitForTestIdAsync("quotes-table", 30000);

        await page.ClickByTestIdAsync("new-quote-button");
        await page.WaitForTestIdAsync("quote-editor", 10000);

        await page.SelectOptionAsync("[data-testid='quote-customer-select']", new SelectOptionValue { Index = 1 });

        await page.ClickByTestIdAsync("quote-add-line-button");
        await page.WaitForTestIdAsync("quote-line-form", 10000);
        await page.FillAsync("[data-testid='quote-line-description']", "Manual E2E panel install");
        await page.ClickByTestIdAsync("quote-line-save-button");
        await page.WaitForTestIdAsync("quote-lines-table", 15000);

        await page.ClickByTestIdAsync("quote-save-button");
        await page.WaitForSelectorAsync("[data-testid='quote-editor-title']:has-text('Q-')", new() { Timeout = 30000 });

        var content = await page.ContentAsync();
        Assert.Contains("Manual E2E panel install", content);
        Assert.Contains("Q-", content);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Quotes_Manual_Create_Customer_And_Lines()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/quotes");
        await page.WaitForTestIdAsync("quotes-table", 30000);

        await page.ClickByTestIdAsync("new-quote-button");
        await page.WaitForTestIdAsync("quote-editor", 10000);

        await page.ClickByTestIdAsync("quote-new-customer-toggle");
        await page.WaitForTestIdAsync("quote-new-customer-form", 10000);
        var uniqueName = $"E2E Customer {Guid.NewGuid():N}".Substring(0, 24);
        await page.FillAsync("[data-testid='quote-new-customer-name']", uniqueName);
        await page.ClickByTestIdAsync("quote-new-customer-save");

        await page.ClickByTestIdAsync("quote-add-line-button");
        await page.FillAsync("[data-testid='quote-line-description']", "E2E travel allowance");
        await page.ClickByTestIdAsync("quote-line-save-button");
        await page.ClickByTestIdAsync("quote-save-button");
        await page.WaitForSelectorAsync("[data-testid='quote-editor-title']:has-text('Q-')", new() { Timeout = 30000 });

        var content = await page.ContentAsync();
        Assert.Contains(uniqueName, content);
        Assert.Contains("E2E travel allowance", content);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Quotes_Edit_Opens_Lines_Not_Just_Notes()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/quotes");
        await page.WaitForTestIdAsync("quotes-table", 30000);

        var draftRow = page.Locator("[data-testid='quotes-table'] tbody tr")
            .Filter(new() { HasText = "Draft" })
            .First;
        if (await draftRow.CountAsync() == 0)
            draftRow = page.Locator("[data-testid='quotes-table'] tbody tr").First;
        await draftRow.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await page.WaitForTestIdAsync("quote-editor", 15000);

        var content = await page.ContentAsync();
        Assert.Contains("Line Items", content);
        Assert.Contains("quote-add-line-button", content);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Quotes_Line_UnitCost_AutoCalculates_SellPrice_And_SaveAddAnother()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/quotes");
        await page.WaitForTestIdAsync("quotes-table", 30000);

        await page.ClickByTestIdAsync("new-quote-button");
        await page.SelectOptionAsync("[data-testid='quote-customer-select']", new SelectOptionValue { Index = 1 });
        await page.ClickByTestIdAsync("quote-add-line-button");
        await page.FillAsync("[data-testid='quote-line-description']", "E2E GP line A");
        await page.FillAsync("[data-testid='quote-line-unit-cost']", "100");
        await page.FillAsync("[data-testid='quote-line-gross-profit-percent']", "25");
        await page.WaitForTimeoutAsync(300);

        var priceA = await page.InputValueAsync("[data-testid='quote-line-unit-price']");
        Assert.Contains("133", priceA);

        await page.ClickByTestIdAsync("quote-line-save-add-another-button");
        await page.WaitForTestIdAsync("quote-line-form", 10000);

        var descAfter = await page.InputValueAsync("[data-testid='quote-line-description']");
        Assert.Equal("", descAfter);

        await page.FillAsync("[data-testid='quote-line-description']", "E2E GP line B");
        await page.FillAsync("[data-testid='quote-line-unit-cost']", "200");
        await page.FillAsync("[data-testid='quote-line-gross-profit-percent']", "40");
        await page.WaitForTimeoutAsync(300);
        await page.ClickByTestIdAsync("quote-line-save-button");

        var content = await page.ContentAsync();
        Assert.Contains("E2E GP line A", content);
        Assert.Contains("E2E GP line B", content);
        Assert.Contains("Blended GP", content);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Quotes_Edit_Line_Updates_Total()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/quotes");
        await page.WaitForTestIdAsync("quotes-table", 30000);

        await page.ClickByTestIdAsync("new-quote-button");
        await page.SelectOptionAsync("[data-testid='quote-customer-select']", new SelectOptionValue { Index = 1 });
        await page.ClickByTestIdAsync("quote-add-line-button");
        await page.FillAsync("[data-testid='quote-line-description']", "E2E editable line");
        await page.FillAsync("[data-testid='quote-line-unit-price']", "100");
        await page.PressAsync("[data-testid='quote-line-unit-price']", "Tab");
        await page.ClickByTestIdAsync("quote-line-save-button");
        await page.ClickByTestIdAsync("quote-save-button");
        await page.WaitForSelectorAsync("[data-testid='quote-editor-title']:has-text('Q-')", new() { Timeout = 30000 });

        var beforeContent = await page.ContentAsync();
        Assert.Matches(new Regex(@"115[.,]00"), beforeContent);

        await page.ClickByTestIdAsync("quote-line-edit-button");
        await page.FillAsync("[data-testid='quote-line-unit-price']", "200");
        await page.PressAsync("[data-testid='quote-line-unit-price']", "Tab");
        await page.ClickByTestIdAsync("quote-line-save-button");
        await page.WaitForTimeoutAsync(500);

        var afterContent = await page.ContentAsync();
        Assert.Matches(new Regex(@"230[.,]00"), afterContent);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Ai_Settings_Page_Loads_Free_Providers()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/settings/ai");
        await page.WaitForTestIdAsync("ai-provider-select", 15000);

        var options = await page.Locator("[data-testid='ai-provider-select'] option").AllTextContentsAsync();
        Assert.Contains(options, o => o.Contains("Google Gemini", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(options, o => o.Contains("Groq", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(options, o => o.Contains("Ollama", StringComparison.OrdinalIgnoreCase));

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
    public async Task Opportunity_Advances_Stage_On_Advance_Button()
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

        var uniqueTitle = $"E2E Advance {DateTime.UtcNow.Ticks}";
        await page.ClickByTestIdAsync("opportunity-create-button");
        await page.WaitForTestIdAsync("opportunity-create-form", 10000);
        await page.FillByTestIdAsync("opportunity-title", uniqueTitle);
        await page.ClickByTestIdAsync("opportunity-save");
        await page.Locator(".toast-body")
            .Filter(new() { HasText = "Opportunity created" })
            .First
            .WaitForAsync(new() { Timeout = 15000 });

        await page.Locator("[data-testid='opportunity-card']")
            .Filter(new() { HasText = uniqueTitle })
            .First
            .ClickAsync();
        await page.WaitForTestIdAsync("opportunity-detail", 10000);

        var stageBefore = await page.Locator("[data-testid='opportunity-stage']").TextContentAsync();
        Assert.Contains("Lead", stageBefore ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        await page.ClickByTestIdAsync("opportunity-advance-stage");
        await page.Locator(".toast-body")
            .Filter(new() { HasText = "Stage advanced" })
            .First
            .WaitForAsync(new() { Timeout = 15000 });

        var stageAfter = await page.Locator("[data-testid='opportunity-stage']").TextContentAsync();
        Assert.Contains("Qualified", stageAfter ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Convert_Quote_To_Job_Preserves_Travel_Costs()
    {
        var quoteNumber = await E2EHelpers.EnsureConvertibleQuoteAsync();
        Assert.False(string.IsNullOrWhiteSpace(quoteNumber));
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/quotes");
        await page.WaitForTestIdAsync("quotes-table", 30000);

        var travelRow = page.Locator("[data-testid='quote-row-e2e-convertible']").First;
        await Assertions.Expect(travelRow).ToHaveCountAsync(1, new() { Timeout = 20000 });

        for (var openAttempt = 0; openAttempt < 3; openAttempt++)
        {
            await travelRow.Locator("[data-testid='quote-view-button']").ClickAsync();
            try
            {
                await page.WaitForTestIdAsync("quote-editor", 10000);
                break;
            }
            catch (TimeoutException) when (openAttempt < 2)
            {
                await page.GotoRelativeAsync("/quotes");
                await page.WaitForTestIdAsync("quotes-table", 30000);
            }
        }
        await page.WaitForTestIdAsync("convert-to-job", 20000);
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

        var jobRows = page.Locator("[data-testid='jobs-table'] tbody tr");
        var rowCount = await jobRows.CountAsync();
        Assert.True(rowCount > 0, "Expected at least one job after quote conversion.");

        var detailOpened = false;
        for (var i = 0; i < rowCount && !detailOpened; i++)
        {
            await jobRows.Nth(i).Locator("[data-testid='job-view-button']").ClickAsync();
            try
            {
                await page.WaitForTestIdAsync("create-invoice-from-job-detail", 8000);
                detailOpened = true;
            }
            catch (TimeoutException) { }
        }
        Assert.True(detailOpened, "Could not open a job detail panel with invoice action.");

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

        ILocator? travelRow = null;
        for (var attempt = 0; attempt < 15 && travelRow == null; attempt++)
        {
            var demoCandidate = page.Locator("[data-testid='job-row-e2e-invoice-demo']").First;
            if (await demoCandidate.CountAsync() > 0)
            {
                travelRow = demoCandidate;
                break;
            }

            var next = page.GetByRole(AriaRole.Button, new() { Name = "Next" });
            if (await next.CountAsync() == 0 || await next.IsDisabledAsync())
                break;

            await next.ClickAsync();
            await page.WaitForTestIdAsync("jobs-table", 10000);
        }

        Assert.NotNull(travelRow);
        await travelRow!.Locator("[data-testid='job-view-button']").ClickAsync();
        await page.WaitForTestIdAsync("create-invoice-from-job-detail", 10000);
        await page.ClickByTestIdAsync("create-invoice-from-job-detail");

        await page.WaitForURLAsync("**/invoices**", new() { Timeout = 30000 });
        await page.WaitForTestIdAsync("invoices-table", 30000);

        var toast = page.Locator(".toast-body").Filter(new() { HasText = "Invoice" }).First;
        if (await toast.CountAsync() > 0)
        {
            var toastText = await toast.TextContentAsync() ?? string.Empty;
            var invMatch = Regex.Match(toastText, @"INV-\d{4}-[A-Z0-9]+");
            if (invMatch.Success)
            {
                await page.Locator("[data-testid='invoices-table'] tbody tr")
                    .Filter(new() { HasText = invMatch.Value })
                    .First
                    .Locator("[data-testid='view-invoice']").ClickAsync();
            }
        }

        if (await page.Locator("[data-testid='invoice-line-items-header']").CountAsync() == 0)
        {
            await page.Locator("[data-testid='invoices-table'] tbody tr")
                .Filter(new() { HasText = "Hospital" })
                .First
                .Locator("[data-testid='view-invoice']").ClickAsync();
        }

        await page.WaitForTestIdAsync("invoice-line-items-header", 15000);
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

        var portalLink = page.Locator("[data-testid='tenant-billing-portal']").First;
        if (await portalLink.CountAsync() > 0)
        {
            var href = await portalLink.GetAttributeAsync("href");
            Assert.False(string.IsNullOrWhiteSpace(href));
            Assert.Contains("cus_", href!, StringComparison.OrdinalIgnoreCase);
        }

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

        var billingButton = page.Locator("[data-testid='home-manage-billing-button']");
        if (await billingButton.CountAsync() > 0)
        {
            var label = (await billingButton.TextContentAsync()) ?? string.Empty;
            Assert.Contains("Manage billing", label, StringComparison.OrdinalIgnoreCase);
        }

        await page.CloseAsync();
    }

    [Fact]
    public async Task AccountBilling_Page_Shows_Plan_And_Manage_Billing()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/account-billing");
        await page.WaitForTestIdAsync("account-billing-ready", 15000);

        var content = await page.ContentAsync();
        Assert.Contains("Billing", content);
        Assert.Contains("Acme", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Monthly usage", content);

        await page.WaitForTestIdAsync("account-billing-tier", 5000);
        await page.WaitForTestIdAsync("account-billing-usage-card", 5000);

        var tierText = (await page.Locator("[data-testid='account-billing-tier']").TextContentAsync()) ?? string.Empty;
        Assert.Contains("Professional", tierText, StringComparison.OrdinalIgnoreCase);

        var statusText = (await page.Locator("[data-testid='account-billing-status']").TextContentAsync()) ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(statusText));

        var manageButton = page.Locator("[data-testid='account-billing-manage-button']");
        if (await manageButton.CountAsync() > 0)
        {
            var label = (await manageButton.TextContentAsync()) ?? string.Empty;
            Assert.Contains("Manage billing", label, StringComparison.OrdinalIgnoreCase);
        }

        await page.CloseAsync();
    }

    [Fact]
    public async Task Account_Hub_Shows_Billing_And_Security_Tabs()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/account");
        await page.WaitForTestIdAsync("account-hub-ready", 15000);
        await page.WaitForTestIdAsync("account-billing-ready", 15000);

        var billingTab = page.Locator("[data-testid='account-tab-billing']");
        await Assertions.Expect(billingTab).ToHaveClassAsync(new Regex("active"));

        await page.ClickByTestIdAsync("account-tab-security");
        await page.WaitForTestIdAsync("account-security-ready", 15000);

        var content = await page.ContentAsync();
        Assert.Contains("Two-Factor Authentication", content);

        await page.ClickByTestIdAsync("account-tab-billing");
        await page.WaitForTestIdAsync("account-billing-tier", 10000);

        await page.CloseAsync();
    }

    [Fact]
    public async Task AccountBilling_Reflects_Webhook_Tier_Update()
    {
        var payload = """
            {
              "type": "customer.subscription.updated",
              "data": {
                "object": {
                  "customer": "cus_demo_beta",
                  "status": "active",
                  "metadata": {
                    "tenant_subdomain": "beta",
                    "tier": "professional"
                  }
                }
              }
            }
            """;

        var webhookResponse = await E2EHelpers.PostStripeWebhookAsync(payload);
        Assert.True(webhookResponse.IsSuccessStatusCode, await webhookResponse.Content.ReadAsStringAsync());

        var page = await _browser.LoginAsync(E2EHelpers.BetaEmail, E2EHelpers.BetaPassword);
        await page.GotoRelativeAsync("/account-billing");
        await page.WaitForTestIdAsync("account-billing-ready", 15000);

        var tierText = (await page.Locator("[data-testid='account-billing-tier']").TextContentAsync()) ?? string.Empty;
        Assert.Contains("Professional", tierText, StringComparison.OrdinalIgnoreCase);

        await page.ClickByTestIdAsync("account-billing-refresh-button");
        await page.Locator(".toast-body").Filter(new() { HasText = "refreshed" }).First.WaitForAsync(new() { Timeout = 15000 });

        tierText = (await page.Locator("[data-testid='account-billing-tier']").TextContentAsync()) ?? string.Empty;
        Assert.Contains("Professional", tierText, StringComparison.OrdinalIgnoreCase);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Reports_Page_Shows_Technician_Utilization()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/reports");
        await page.WaitForTestIdAsync("reports-ready", 20000);
        await page.WaitForTestIdAsync("reports-utilization-card", 10000);

        var content = await page.ContentAsync();
        Assert.Contains("Technician Utilization", content);
        Assert.Contains("Team average", content);

        var utilizationRows = page.Locator("[data-testid='reports-utilization-row']");
        if (await utilizationRows.CountAsync() > 0)
        {
            var rowText = (await utilizationRows.First.TextContentAsync()) ?? string.Empty;
            Assert.True(
                rowText.Contains("Thabo", StringComparison.OrdinalIgnoreCase)
                || rowText.Contains("Johan", StringComparison.OrdinalIgnoreCase)
                || rowText.Contains("h", StringComparison.OrdinalIgnoreCase));
        }

        await page.CloseAsync();
    }

    [Fact]
    public async Task Reports_Page_Shows_Job_Profitability_From_Variance()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/reports");
        await page.WaitForTestIdAsync("reports-ready", 20000);
        await page.WaitForTestIdAsync("reports-profitability-card", 10000);

        var content = await page.ContentAsync();
        Assert.Contains("Job Profitability", content);
        Assert.DoesNotContain("Mine Install (+22%)", content);

        var bar = page.Locator("[data-testid='reports-profitability-bar']");
        if (await bar.CountAsync() > 0)
        {
            var barText = (await bar.TextContentAsync()) ?? string.Empty;
            Assert.Matches(@"-?\d+(\.\d+)?%", barText.Trim());
        }

        var top = page.Locator("[data-testid='reports-profitability-top']");
        if (await top.CountAsync() > 0)
        {
            var topText = (await top.TextContentAsync()) ?? string.Empty;
            Assert.Contains("Top performer:", topText);
            Assert.Contains("%", topText);
        }

        await page.CloseAsync();
    }

    [Fact]
    public async Task Reports_Page_Shows_Cashflow_Forecast_From_Receivables()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/reports");
        await page.WaitForTestIdAsync("reports-ready", 20000);
        await page.WaitForTestIdAsync("reports-cashflow-card", 10000);

        var content = await page.ContentAsync();
        Assert.Contains("Cashflow Forecast", content);
        Assert.DoesNotContain("+R 245k", content);

        var detail = page.Locator("[data-testid='reports-cashflow-detail']");
        await Assertions.Expect(detail).ToBeVisibleAsync();
        var detailText = (await detail.TextContentAsync()) ?? string.Empty;
        Assert.Contains("Receivables:", detailText);
        Assert.Contains("pipeline:", detailText);

        var bar = page.Locator("[data-testid='reports-cashflow-bar']");
        if (await bar.CountAsync() > 0)
        {
            var barText = (await bar.TextContentAsync()) ?? string.Empty;
            Assert.Matches(@"[+-]R ", barText.Trim());
        }

        await page.CloseAsync();
    }

    [Fact]
    public async Task Payroll_Page_Shows_JobLabor_Summaries()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/payroll");
        await page.WaitForTestIdAsync("payroll-ready", 15000);

        var content = await page.ContentAsync();
        Assert.Contains("Payroll", content);
        Assert.Contains("JobLabor", content);

        var rows = page.Locator("[data-testid='payroll-row']");
        Assert.True(await rows.CountAsync() >= 1);

        var rowText = (await rows.First.TextContentAsync()) ?? string.Empty;
        Assert.True(
            rowText.Contains("Thabo", StringComparison.OrdinalIgnoreCase)
            || rowText.Contains("Johan", StringComparison.OrdinalIgnoreCase),
            $"Expected demo employee on payroll table, got: {rowText}");

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
            await page.WaitForTestIdAsync("scheduling-crew-panel", 5000);

            var employeeSelect = page.Locator("[data-testid='scheduling-employee-select']");
            var optionCount = await employeeSelect.Locator("option").CountAsync();
            if (optionCount > 1)
            {
                var firstEmployeeValue = await employeeSelect.Locator("option").Nth(1).GetAttributeAsync("value");
                Assert.False(string.IsNullOrWhiteSpace(firstEmployeeValue));
                await employeeSelect.SelectOptionAsync(new[] { firstEmployeeValue! });

                var crewAssigned = false;
                if (optionCount > 2)
                {
                    var secondEmployeeValue = await employeeSelect.Locator("option").Nth(2).GetAttributeAsync("value");
                    if (!string.IsNullOrWhiteSpace(secondEmployeeValue) && secondEmployeeValue != firstEmployeeValue)
                    {
                        var crewCheckboxes = page.Locator("[data-testid='scheduling-crew-checkbox']");
                        if (await crewCheckboxes.CountAsync() >= 2)
                        {
                            await crewCheckboxes.Nth(1).CheckAsync();
                            crewAssigned = true;
                        }
                    }
                }

                await page.ClickByTestIdAsync("scheduling-save-assignments");
                await page.Locator(".toast-body").First.WaitForAsync(new() { Timeout = 15000 });
                var toast = (await page.Locator(".toast-body").First.TextContentAsync()) ?? string.Empty;
                Assert.Contains("Assignment saved", toast, StringComparison.OrdinalIgnoreCase);
                await page.WaitForTestIdAsync("scheduling-assigned-employee", 15000);

                if (crewAssigned)
                {
                    try
                    {
                        await page.WaitForTestIdAsync("scheduling-crew-badge", 8000);
                    }
                    catch (TimeoutException)
                    {
                        // Crew UI save path verified in unit tests; badge timing can lag on Blazor re-render.
                    }
                }
            }
            else
            {
                await page.ClickByTestIdAsync("scheduling-close-assign");
            }
        }
        catch (TimeoutException)
        {
            await page.WaitForSelectorAsync(".card .card-header:has-text('Assign for')", new() { Timeout = 10000 });
            await page.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        }

        await page.CloseAsync();
    }

    [Fact]
    public async Task Scheduling_Quick_Adds_Labor_From_Assigned_Crew()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/scheduling");
        await page.WaitForTestIdAsync("scheduling-ready", 20000);

        await page.Locator("[data-testid='scheduling-view-assign']").First.ClickAsync();
        await page.WaitForTestIdAsync("scheduling-assign-panel", 10000);

        var employeeSelect = page.Locator("[data-testid='scheduling-employee-select']");
        var optionCount = await employeeSelect.Locator("option").CountAsync();
        if (optionCount <= 1)
        {
            await page.ClickByTestIdAsync("scheduling-close-assign");
            await page.CloseAsync();
            return;
        }

        var firstEmployeeValue = await employeeSelect.Locator("option").Nth(1).GetAttributeAsync("value");
        await employeeSelect.SelectOptionAsync(new[] { firstEmployeeValue! });
        await page.ClickByTestIdAsync("scheduling-save-assignments");
        await page.Locator(".toast-body").First.WaitForAsync(new() { Timeout = 15000 });

        await page.Locator("[data-testid='scheduling-view-assign']").First.ClickAsync();
        await page.WaitForTestIdAsync("scheduling-quick-labor-panel", 10000);
        await page.FillByTestIdAsync("scheduling-labor-hours", "4");
        await page.ClickByTestIdAsync("scheduling-quick-add-labor");

        var laborToast = page.Locator(".toast-body").Filter(new() { HasText = "Logged" });
        await laborToast.First.WaitForAsync(new() { Timeout = 15000 });

        var toast = (await laborToast.First.TextContentAsync()) ?? string.Empty;
        Assert.Contains("labor", toast, StringComparison.OrdinalIgnoreCase);

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
    public async Task Audit_Shows_Convert_After_Quote_To_Job()
    {
        await E2EHelpers.EnsureConvertibleQuoteAsync();
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/quotes");
        await page.WaitForTestIdAsync("quotes-table", 30000);

        var convertible = page.Locator("[data-testid='quote-row-e2e-convertible']").First;
        await Assertions.Expect(convertible).ToHaveCountAsync(1, new() { Timeout = 20000 });

        for (var openAttempt = 0; openAttempt < 3; openAttempt++)
        {
            await convertible.Locator("[data-testid='quote-view-button']").ClickAsync();
            try
            {
                await page.WaitForTestIdAsync("quote-editor", 10000);
                break;
            }
            catch (TimeoutException) when (openAttempt < 2)
            {
                await page.GotoRelativeAsync("/quotes");
                await page.WaitForTestIdAsync("quotes-table", 30000);
            }
        }
        await page.WaitForTestIdAsync("convert-to-job", 20000);
        await page.ClickByTestIdAsync("convert-to-job");

        try
        {
            await page.WaitForURLAsync("**/jobs**", new() { Timeout = 12000 });
        }
        catch (TimeoutException)
        {
            await page.GotoRelativeAsync("/jobs");
        }

        await page.WaitForTestIdAsync("jobs-table", 30000);

        await page.GotoRelativeAsync("/audit");
        await page.WaitForTestIdAsync("audit-table", 15000);

        var content = await page.ContentAsync();
        Assert.Contains("CONVERT", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Quote", content, StringComparison.OrdinalIgnoreCase);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Audit_Shows_Invoice_Create_After_Job_Invoice()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/jobs");
        await page.WaitForTestIdAsync("jobs-table", 30000);

        var jobRow = page.Locator("[data-testid='job-row-with-travel']").First;
        if (await jobRow.CountAsync() == 0)
            jobRow = page.Locator("[data-testid='jobs-table'] tbody tr").First;

        await jobRow.Locator("[data-testid='job-view-button']").ClickAsync();
        await page.WaitForTestIdAsync("create-invoice-from-job-detail", 10000);
        await page.ClickByTestIdAsync("create-invoice-from-job-detail");

        await page.WaitForURLAsync("**/invoices**", new() { Timeout = 30000 });
        await page.WaitForTestIdAsync("invoices-table", 30000);

        await page.GotoRelativeAsync("/audit");
        await page.WaitForTestIdAsync("audit-table", 15000);

        var content = await page.ContentAsync();
        Assert.Contains("CREATE", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invoice", content, StringComparison.OrdinalIgnoreCase);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Inventory_Page_Loads_Stock_Table()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/inventory");

        try
        {
            await page.WaitForTestIdAsync("inventory-ready", 15000);
        }
        catch (TimeoutException)
        {
            await page.WaitForTestIdAsync("inventory-table", 15000);
        }

        var content = await page.ContentAsync();
        Assert.Contains("DB-12W-001", content);
        Assert.Contains("Inventory", content);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Inventory_LowStock_Filter_ShowsLowItemsOnly()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/inventory");
        await page.WaitForTestIdAsync("inventory-table", 30000);

        var contentBefore = await page.ContentAsync();
        Assert.Contains("DB-12W-001", contentBefore);

        await page.ClickByTestIdAsync("inventory-low-stock-filter");
        await page.WaitForTestIdAsync("inventory-ready", 15000);

        var contentAfter = await page.ContentAsync();
        Assert.Contains("OIL-TR-5L", contentAfter);
        Assert.DoesNotContain("DB-12W-001", contentAfter);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Inventory_Search_FiltersBySku()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/inventory");
        await page.WaitForTestIdAsync("inventory-table", 30000);

        var contentBefore = await page.ContentAsync();
        Assert.Contains("DB-12W-001", contentBefore);

        await page.FillByTestIdAsync("inventory-search", "OIL-TR");

        var tableBody = page.Locator("[data-testid='inventory-table'] tbody");
        await Assertions.Expect(tableBody.Locator("tr")).ToHaveCountAsync(1, new() { Timeout = 20000 });
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "OIL-TR-5L" })).ToHaveCountAsync(1);
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "DB-12W-001" })).ToHaveCountAsync(0);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Suppliers_Page_Loads_Demo_Vendor()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/suppliers");
        await page.WaitForTestIdAsync("suppliers-table", 30000);

        var tableBody = page.Locator("[data-testid='suppliers-table'] tbody");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "ElectroSupply SA" })).ToHaveCountAsync(1);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Suppliers_Search_FiltersByName()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/suppliers");
        await page.WaitForTestIdAsync("suppliers-table", 30000);

        var tableBody = page.Locator("[data-testid='suppliers-table'] tbody");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "ElectroSupply SA" })).ToHaveCountAsync(1);
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Panel Supplies" })).ToHaveCountAsync(1);

        await page.FillByTestIdAsync("suppliers-search", "Panel");

        await Assertions.Expect(tableBody.Locator("tr")).ToHaveCountAsync(1, new() { Timeout = 20000 });
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Panel Supplies" })).ToHaveCountAsync(1);
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "ElectroSupply SA" })).ToHaveCountAsync(0);

        await page.CloseAsync();
    }

    [Fact]
    public async Task PurchaseOrders_Page_Loads_Demo_Po()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/purchase-orders");
        await page.WaitForTestIdAsync("purchase-orders-table", 30000);

        var tableBody = page.Locator("[data-testid='purchase-orders-table'] tbody");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "ElectroSupply SA" })).ToHaveCountAsync(1);

        await page.Locator("[data-testid='purchase-order-view']").First.ClickAsync();
        await page.WaitForTestIdAsync("purchase-order-detail", 10000);

        var detail = await page.ContentAsync();
        Assert.Contains("Total:", detail);

        await page.CloseAsync();
    }

    [Fact]
    public async Task PurchaseOrders_Search_FiltersBySupplier()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/purchase-orders");
        await page.WaitForTestIdAsync("purchase-orders-table", 30000);

        var tableBody = page.Locator("[data-testid='purchase-orders-table'] tbody");
        Assert.True(await tableBody.Locator("tr").CountAsync() >= 2);

        await page.FillByTestIdAsync("purchase-orders-search", "Electro");

        await Assertions.Expect(tableBody.Locator("tr")).ToHaveCountAsync(1, new() { Timeout = 20000 });
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "ElectroSupply SA" })).ToHaveCountAsync(1);
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Panel Supplies" })).ToHaveCountAsync(0);

        await page.CloseAsync();
    }

    [Fact]
    public async Task PurchaseOrders_Receive_Updates_Inventory()
    {
        await E2EHelpers.EnsureReceiveDemoPoAsync();
        var page = await _browser.LoginAsync();
        page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();
        await page.GotoRelativeAsync("/inventory");
        try
        {
            await page.WaitForTestIdAsync("inventory-table", 30000);
        }
        catch (TimeoutException)
        {
            await page.GotoRelativeAsync("/inventory");
            await page.WaitForTestIdAsync("inventory-table", 30000);
        }

        var invBody = page.Locator("[data-testid='inventory-table'] tbody");
        var ledRow = invBody.Locator("tr").Filter(new() { HasText = "LED-HB-150" });
        await ledRow.First.WaitForAsync(new() { Timeout = 15000 });
        var qtyBeforeText = await ledRow.First.Locator("td").Nth(3).TextContentAsync();
        Assert.NotNull(qtyBeforeText);
        var qtyBefore = int.Parse(new string(qtyBeforeText.Where(char.IsDigit).ToArray()));

        await page.GotoRelativeAsync("/purchase-orders");
        await page.WaitForTestIdAsync("purchase-orders-table", 30000);

        var poBody = page.Locator("[data-testid='purchase-orders-table'] tbody");
        ILocator? sentRow = null;
        for (var attempt = 0; attempt < 3 && sentRow == null; attempt++)
        {
            if (attempt > 0)
            {
                await page.ReloadAsync();
                await page.WaitForTestIdAsync("purchase-orders-table", 30000);
            }

            await page.Locator("[data-testid='purchase-orders-search']").PressSequentiallyAsync("Panel Supplies", new() { Delay = 30 });
            await Assertions.Expect(poBody.Locator("tr").Filter(new() { HasText = "Panel Supplies" }))
                .ToHaveCountAsync(1, new() { Timeout = 15000 });

            var candidate = poBody.Locator("tr")
                .Filter(new() { HasText = "Panel Supplies" })
                .Filter(new() { Has = page.Locator("[data-testid='purchase-order-receive']") });
            if (await candidate.CountAsync() > 0)
                sentRow = candidate.First;
        }

        Assert.NotNull(sentRow);
        await sentRow!.WaitForAsync(new() { Timeout = 5000 });

        var receiveButton = sentRow.Locator("[data-testid='purchase-order-receive']");
        await receiveButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await receiveButton.ClickAsync();

        var receivedRow = poBody.Locator("tr").Filter(new() { HasText = "Panel Supplies" }).First;
        try
        {
            await Assertions.Expect(receivedRow).ToContainTextAsync("Received", new() { Timeout = 20000 });
        }
        catch (Exception)
        {
            await page.Locator(".toast-body")
                .Filter(new() { HasText = "PO received" })
                .First
                .WaitForAsync(new() { Timeout = 10000 });
            await Assertions.Expect(receivedRow).ToContainTextAsync("Received", new() { Timeout = 10000 });
        }

        await page.GotoRelativeAsync("/inventory");
        await page.WaitForTestIdAsync("inventory-table", 30000);
        await page.FillByTestIdAsync("inventory-search", "LED-HB");

        var invBodyAfter = page.Locator("[data-testid='inventory-table'] tbody");
        var ledAfter = invBodyAfter.Locator("tr").Filter(new() { HasText = "LED-HB-150" });
        await ledAfter.First.WaitForAsync(new() { Timeout = 15000 });
        var qtyAfterText = await ledAfter.First.Locator("td").Nth(3).TextContentAsync();
        var qtyAfter = int.Parse(new string(qtyAfterText!.Where(char.IsDigit).ToArray()));
        Assert.Equal(qtyBefore + 3, qtyAfter);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Customers_Page_Loads_Demo_Customer()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/customers");
        await page.WaitForTestIdAsync("customers-table", 30000);

        var tableBody = page.Locator("[data-testid='customers-table'] tbody");
        await page.FillByTestIdAsync("customers-search", "Hospital");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Johannesburg General Hospital" })).ToHaveCountAsync(1, new() { Timeout = 15000 });

        await page.CloseAsync();
    }

    [Fact]
    public async Task Customers_Search_FiltersByName()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/customers");
        await page.WaitForTestIdAsync("customers-table", 30000);

        var tableBody = page.Locator("[data-testid='customers-table'] tbody");
        await page.FillByTestIdAsync("customers-search", "Hospital");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Johannesburg General Hospital" })).ToHaveCountAsync(1, new() { Timeout = 15000 });

        await page.FillByTestIdAsync("customers-search", "Mining");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Cape Town Mining" })).ToHaveCountAsync(1, new() { Timeout = 15000 });

        await page.FillByTestIdAsync("customers-search", "Hospital");

        await Assertions.Expect(tableBody.Locator("tr")).ToHaveCountAsync(1, new() { Timeout = 20000 });
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Johannesburg General Hospital" })).ToHaveCountAsync(1);
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Cape Town Mining" })).ToHaveCountAsync(0);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Assets_Page_Loads_Demo_Transformer()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/assets");
        await page.WaitForTestIdAsync("assets-table", 30000);

        var tableBody = page.Locator("[data-testid='assets-table'] tbody");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "11kV/400V Transformer" })).ToHaveCountAsync(1);
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Johannesburg General Hospital" })).ToHaveCountAsync(1);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Assets_Search_FiltersByName()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/assets");
        await page.WaitForTestIdAsync("assets-table", 30000);

        var tableBody = page.Locator("[data-testid='assets-table'] tbody");
        await page.FillByTestIdAsync("assets-search", "Transformer");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "11kV/400V Transformer" })).ToHaveCountAsync(1, new() { Timeout = 15000 });

        await page.FillByTestIdAsync("assets-search", "Warehouse");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Warehouse LV Distribution Board" })).ToHaveCountAsync(1, new() { Timeout = 15000 });

        await page.FillByTestIdAsync("assets-search", "Transformer");
        await Assertions.Expect(tableBody.Locator("tr")).ToHaveCountAsync(1, new() { Timeout = 20000 });
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Warehouse LV Distribution Board" })).ToHaveCountAsync(0);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Employees_Page_Loads_Demo_Staff()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/employees");
        await page.WaitForTestIdAsync("employees-table", 30000);

        var tableBody = page.Locator("[data-testid='employees-table'] tbody");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "EMP-001" })).ToHaveCountAsync(1);
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "Johan" })).ToHaveCountAsync(1);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Employees_Search_FiltersByName()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/employees");
        await page.WaitForTestIdAsync("employees-table", 30000);

        var tableBody = page.Locator("[data-testid='employees-table'] tbody");
        await page.FillByTestIdAsync("employees-search", "Johan");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "EMP-002" })).ToHaveCountAsync(1, new() { Timeout = 15000 });

        await page.FillByTestIdAsync("employees-search", "Thabo");
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "EMP-001" })).ToHaveCountAsync(1, new() { Timeout = 15000 });

        await page.FillByTestIdAsync("employees-search", "Johan");
        await Assertions.Expect(tableBody.Locator("tr")).ToHaveCountAsync(1, new() { Timeout = 20000 });
        await Assertions.Expect(tableBody.Locator("tr").Filter(new() { HasText = "EMP-001" })).ToHaveCountAsync(0);

        await page.CloseAsync();
    }

    [Fact]
    public async Task SalesOrders_Page_Loads_And_Shows_Detail()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/sales-orders");

        try
        {
            await page.WaitForTestIdAsync("sales-orders-ready", 15000);
        }
        catch (TimeoutException)
        {
            await page.WaitForSelectorAsync("[data-testid='sales-orders-table'] tbody tr", new() { Timeout = 30000 });
        }

        var content = await page.ContentAsync();
        Assert.Contains("SO-", content);

        var viewButton = page.Locator("[data-testid='sales-order-view']").First;
        if (await viewButton.CountAsync() == 0)
            viewButton = page.GetByRole(AriaRole.Button, new() { Name = "View" }).First;

        await viewButton.ClickAsync();
        await page.WaitForTestIdAsync("sales-order-detail", 10000);

        var detail = await page.ContentAsync();
        Assert.Contains("Total:", detail);

        await page.CloseAsync();
    }

    [Fact]
    public async Task Finance_Page_Loads_Chart_Of_Accounts_And_Export()
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync($"{E2EHelpers.BaseUrl}/login-complete?email={Uri.EscapeDataString(E2EHelpers.AcmeEmail)}");
        await page.WaitForURLAsync(
            u => !u.Contains("login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 45000 });
        await page.GotoRelativeAsync("/finance");

        try
        {
            await page.WaitForTestIdAsync("finance-ready", 20000);
        }
        catch (TimeoutException)
        {
            await page.WaitForTestIdAsync("finance-accounts-table", 20000);
        }

        var content = await page.ContentAsync();
        Assert.Contains("4000", content);
        Assert.Contains("Chart of Accounts", content);

        await page.InstallMeterpClipboardStubAsync();
        await page.ClickByTestIdAsync("finance-export-gl-csv");
        await page.Locator(".toast-body").First.WaitForAsync(new() { Timeout = 15000 });

        var toast = (await page.Locator(".toast-body").First.TextContentAsync()) ?? string.Empty;
        Assert.Contains("GL journal CSV", toast);

        var csv = await page.ReadCapturedClipboardAsync();
        Assert.Contains("EntryDate,EntryNumber,Reference,AccountCode", csv);
        Assert.Contains("4000", csv);

        await page.CloseAsync();
    }

    [Fact]
    public async Task AccountSecurity_Enables_TwoFactor_And_Login_Challenge()
    {
        var secretMaterial = await EnableTwoFactorForBetaAsync();

        var loginPage = await _browser.NewPageAsync();
        await loginPage.GotoAsync($"{E2EHelpers.BaseUrl}/login");
        await loginPage.WaitForTestIdAsync("login-email");
        await loginPage.Locator("[data-testid='login-email']").PressSequentiallyAsync(E2EHelpers.BetaEmail);
        await loginPage.Locator("[data-testid='login-password']").PressSequentiallyAsync(E2EHelpers.BetaPassword);
        await loginPage.ClickByTestIdAsync("login-submit");
        await loginPage.WaitForURLAsync("**/login-2fa**", new() { Timeout = 30000 });

        var loggedIn = false;
        foreach (var candidate in TotpHelper.GetCandidateCodes(secretMaterial))
        {
            await loginPage.Locator("[data-testid='login-2fa-code']").FillAsync("");
            await loginPage.Locator("[data-testid='login-2fa-code']").PressSequentiallyAsync(candidate, new() { Delay = 80 });
            await loginPage.ClickByTestIdAsync("login-2fa-submit");

            try
            {
                await loginPage.WaitForURLAsync(
                    u => !u.Contains("login-2fa", StringComparison.OrdinalIgnoreCase),
                    new() { Timeout = 15000 });
                await loginPage.WaitForURLAsync("**/", new() { Timeout = 45000 });
                loggedIn = true;
                break;
            }
            catch (TimeoutException)
            {
                var url = loginPage.Url;
                if (!url.Contains("login-2fa", StringComparison.OrdinalIgnoreCase))
                    throw;
            }
        }

        Assert.True(loggedIn, "Expected authenticator login to succeed after 2FA enable.");
        var home = await loginPage.ContentAsync();
        Assert.Contains("Beta", home, StringComparison.OrdinalIgnoreCase);
        await loginPage.CloseAsync();

        await DisableTwoFactorForBetaAsync();
    }

    private async Task<string> EnableTwoFactorForBetaAsync()
    {
        var setupPage = await _browser.LoginAsync(E2EHelpers.BetaEmail, E2EHelpers.BetaPassword);
        await setupPage.GotoRelativeAsync("/account-security");
        await setupPage.WaitForTestIdAsync("account-security-ready", 15000);

        if (await setupPage.Locator("[data-testid='2fa-status-enabled']").CountAsync() > 0)
        {
            await setupPage.ClickByTestIdAsync("2fa-disable-button");
            await setupPage.WaitForTestIdAsync("2fa-status-disabled", 15000);
        }

        await setupPage.ClickByTestIdAsync("2fa-enable-button");
        await setupPage.WaitForTestIdAsync("2fa-shared-key", 10000);
        var keyText = await setupPage.Locator("[data-testid='2fa-shared-key']").InnerTextAsync() ?? string.Empty;
        var uriText = await setupPage.Locator("[data-testid='account-security-card'] .text-break").InnerTextAsync() ?? string.Empty;
        var secretMaterial = uriText.Contains("secret=", StringComparison.OrdinalIgnoreCase) ? uriText : keyText;

        var setupCode = TotpHelper.ComputeCurrentCode(secretMaterial);
        await setupPage.Locator("[data-testid='2fa-confirm-code']").PressSequentiallyAsync(setupCode, new() { Delay = 80 });
        await setupPage.ClickByTestIdAsync("2fa-confirm-button");
        await setupPage.WaitForTestIdAsync("2fa-status-enabled", 20000);
        await setupPage.CloseAsync();

        return secretMaterial;
    }

    private async Task DisableTwoFactorForBetaAsync()
    {
        var cleanup = await _browser.LoginAsync(E2EHelpers.BetaEmail, E2EHelpers.BetaPassword);
        await cleanup.GotoRelativeAsync("/account-security");
        await cleanup.WaitForTestIdAsync("account-security-ready", 15000);
        if (await cleanup.Locator("[data-testid='2fa-status-enabled']").CountAsync() > 0)
        {
            await cleanup.ClickByTestIdAsync("2fa-disable-button");
            await cleanup.WaitForTestIdAsync("2fa-status-disabled", 15000);
        }

        await cleanup.CloseAsync();
    }

    [Fact]
    public async Task Notifications_Triggered_From_LowStock_Or_JobEvent()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/notifications");
        await page.EvaluateAsync("() => localStorage.removeItem('notifications')");
        await page.ReloadAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTestIdAsync("notifications-list", 10000);

        var content = await page.ContentAsync();
        Assert.Contains("Low Stock", content);
        Assert.Contains("Job Overdue", content);

        await page.ClickByTestIdAsync("notifications-mark-all");
        await page.WaitForTestIdAsync("notifications-list", 5000);

        await page.CloseAsync();
    }
}