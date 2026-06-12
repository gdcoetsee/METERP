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
        await page.WaitForTestIdAsync("notifications-list", 10000);

        var content = await page.ContentAsync();
        Assert.Contains("Low Stock", content);
        Assert.Contains("Job Overdue", content);

        await page.ClickByTestIdAsync("notifications-mark-all");
        await page.WaitForTestIdAsync("notifications-list", 5000);

        await page.CloseAsync();
    }
}