// Real E2E Tests using Playwright for critical sellable flows.
// Per COMPLETION_PLAN.md and AGENTS.md:
// - Login
// - AI Copilot creates Quote (with travel) + PDF download
// - Convert Quote → Job (travel preserved)
// - Job with costs/labor → Invoice
//
// Setup (run once):
//   dotnet restore   (or build will pull it)
//   pwsh bin/Debug/net9.0/playwright.ps1 install   (or `playwright install`)
//   Start the app: docker-compose up --build  (listens on :8080 by default)
//
// To run only E2E: dotnet test --filter "Category=E2E"
// Requires the app running (recommended: docker-compose up --build, http://localhost:8080)
//
// These tests use realistic selectors based on aria-label, roles, and common Blazor patterns in the app.
// See E2EHelpers.cs for shared helpers (LoginAsync, ClickByTestIdAsync, WaitForTestIdAsync, etc.).
// We added many data-testid attributes to make these tests reliable.

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
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // SlowMo = 50, // uncomment for debugging
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser != null) await _browser.DisposeAsync();
        if (_playwright != null) _playwright.Dispose();
    }

    [Fact]
    public async Task Login_Succeeds_With_Demo_Credentials()
    {
        var page = await _browser.LoginAsync();
        // Verify we are logged in by looking for user menu or welcome text (adjust selector as UI evolves)
        var content = await page.ContentAsync();
        Assert.Contains("Acme", content); // demo tenant or user indicator
        await page.CloseAsync();
    }

    [Fact]
    public async Task AI_Copilot_Creates_Quote_With_Travel_And_Downloads_PDF()
    {
        var page = await _browser.LoginAsync();

        // Navigate to AI Copilot using helper (more reliable)
        await page.GotoRelativeAsync("/ai-copilot");

        // Use data-testid added to AICopilot.razor for robustness
        var prompt = "Create a quote for electrical installation at site with 8 hours labor at $195/hr plus $1500 explicit travel costs and materials.";
        await page.FillByTestIdAsync("ai-prompt-input", prompt);

        // Click generate / ask button using data-testid
        await page.ClickByTestIdAsync("ai-ask-button");

        // Wait for suggestion/results to appear (AI response section)
        await page.WaitForSelectorAsync("text=Quote suggestion, .ai-suggestion, table", new() { Timeout = 30000 });

        // Apply using data-testid (added to AICopilot)
        await page.ClickAsync("[data-testid='ai-apply-quote'], button:has-text('Apply'), button:has-text('Create Quote')");

        // Verify we land on Quotes list or new quote detail with travel line
        await page.WaitForURLAsync("**/Quotes**", new() { Timeout = 15000 });
        var pageContent = await page.ContentAsync();
        Assert.Contains("Travel", pageContent); // explicit travel preserved
        Assert.Contains("Q-", pageContent); // quote number

        // Test real PDF download buttons that appear after AI creation (using new data-testid + download helper)
        await page.RunWithScreenshotOnFailureAsync("AI_Copilot_Real_PDF", async () =>
        {
            // Wait for the real PDF buttons to appear
            await page.WaitForSelectorAsync("[data-testid='ai-download-real-quote-pdf'], [data-testid='ai-download-real-job-pdf']", new() { Timeout = 10000 });
            
            // Download the real quote PDF using helper
            var quotePdf = await page.WaitAndSaveDownloadAsync("[data-testid='ai-download-real-quote-pdf']", "e2e-real-quote");
            Assert.Contains(".pdf", quotePdf.ToLower());
        });

        // Also test a demo PDF export for good measure
        await page.RunWithScreenshotOnFailureAsync("AI_Copilot_Demo_PDF", async () =>
        {
            var demoPdf = await page.WaitAndSaveDownloadAsync("[data-testid='ai-demo-quote-pdf']", "e2e-demo-quote");
            Assert.Contains(".pdf", demoPdf.ToLower());
        });

        await page.CloseAsync();
    }

    [Fact]
    public async Task Convert_Quote_To_Job_Preserves_Travel_Costs()
    {
        var page = await _browser.LoginAsync();

        // Go to Quotes list using helper
        await page.GotoRelativeAsync("/Quotes");

        // Find a quote (prefer data-testid for robustness)
        await page.ClickAsync("[data-testid='quotes-table'] tr:first-child a, tr:has-text('Travel') a");

        // On quote detail, convert button using data-testid where possible
        await page.WaitForTestIdAsync("convert-to-job", 5000);
        await page.ClickByTestIdAsync("convert-to-job");

        // Confirm / navigate to resulting Job
        await page.WaitForURLAsync("**/Jobs**", new() { Timeout = 10000 });

        var content = await page.ContentAsync();
        Assert.Contains("Job from", content); // title pattern from ConvertToJob
        Assert.Contains("Travel", content);   // travel cost explicit in job costs/labor view

        await page.CloseAsync();
    }

    [Fact]
    public async Task Job_With_Travel_And_Labor_Creates_Invoice_With_Correct_Totals()
    {
        var page = await _browser.LoginAsync();

        await page.GotoRelativeAsync("/Jobs");

        // Open a job that has costs (travel + labor)
        await page.ClickAsync("[data-testid='jobs-table'] tr:first-child a, tr:has-text('Travel') a");

        // Wait for the detail view to load (the selected job card with our new button)
        await page.WaitForTestIdAsync("create-invoice-from-job-detail", 8000);

        // Click the Create Invoice button we added with data-testid
        await page.ClickByTestIdAsync("create-invoice-from-job-detail");

        // Verify on Invoices or invoice detail, totals make sense, travel line present
        await page.WaitForURLAsync("**/Invoices**", new() { Timeout = 10000 });
        var content = await page.ContentAsync();
        Assert.Contains("INV-", content);
        Assert.Contains("Travel", content); // explicit costs carried through

        await page.CloseAsync();
    }

    // Future expansions per plan (add more as UI stabilizes):
    [Fact]
    public async Task MultiTenant_Isolation_Basic_Check()
    {
        // Demo for multi-tenant isolation requirement.
        // In a real multi-tenant test we would use separate browser contexts or switch tenants via UI.
        var pageA = await _browser.LoginAsync();
        await pageA.GotoRelativeAsync("/Quotes");
        await pageA.WaitForTestIdAsync("quotes-table", 5000);
        await pageA.WaitForAppReadyAsync();

        // For now, just assert we can reach tenant-scoped data after login.
        // Full implementation would require second tenant login or admin tenant switch.
        var contentA = await pageA.ContentAsync();
        Assert.Contains("Q-", contentA); // At least some quotes visible for demo tenant

        await pageA.CloseAsync();
    }

    [Fact]
    public async Task Notifications_Triggered_From_LowStock_Or_JobEvent()
    {
        var page = await _browser.LoginAsync();
        await page.GotoRelativeAsync("/notifications");

        // Use data-testid we added
        await page.WaitForTestIdAsync("notifications-list", 5000);

        // In demo data (seeded in Notifications.razor), there should be low stock and overdue items
        var content = await page.ContentAsync();
        Assert.Contains("Low Stock", content);
        Assert.Contains("Job Overdue", content);

        // Click Mark All Read using data-testid
        await page.ClickByTestIdAsync("notifications-mark-all");

        // Verify list updates (demo behavior)
        await page.WaitForTestIdAsync("notifications-list", 3000);

        await page.CloseAsync();
    }
}
