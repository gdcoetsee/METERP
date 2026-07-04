using Microsoft.Playwright;

namespace METERP.E2ETests;

/// <summary>
/// Helper extensions and utilities for reliable Playwright E2E tests.
/// </summary>
public static class E2EHelpers
{
    public static string BaseUrl => Environment.GetEnvironmentVariable("METERP_BASE_URL") ?? "http://localhost:8080";

    public const string AcmeEmail = "admin@acme.demo";
    public const string AcmePassword = "Demo123!";
    public const string BetaEmail = "admin@beta.demo";
    public const string BetaPassword = "Demo123!";

    public static async Task<IPage> LoginAsync(
        this IBrowser browser,
        string? email = null,
        string? password = null,
        string? baseUrl = null,
        bool resetDemoState = true)
    {
        var url = baseUrl ?? BaseUrl;
        var loginEmail = email ?? AcmeEmail;

        if (resetDemoState)
        {
            try { await ResetDemoStateAsync(url); }
            catch { /* dev endpoints unavailable on older images */ }
        }

        // Beta 2FA tests can leave authenticator enabled — reset via dev API before UI login.
        if (string.Equals(loginEmail, BetaEmail, StringComparison.OrdinalIgnoreCase))
        {
            try { await DisableBetaTwoFactorAsync(url); }
            catch { /* endpoint may be unavailable on older images */ }
        }

        Exception? lastError = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var page = await browser.NewPageAsync();
            page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();

            try
            {
                await page.GotoAsync($"{url}/login-complete?email={Uri.EscapeDataString(loginEmail)}", new() { Timeout = 60000 });
                await page.WaitForURLAsync(
                    u => !u.Contains("login", StringComparison.OrdinalIgnoreCase),
                    new() { Timeout = 60000 });
                // Blazor Server keeps SignalR open — NetworkIdle never settles reliably.
                await page.WaitForLoadStateAsync(LoadState.Load, new() { Timeout = 30000 });
                await page.WaitForAppReadyAsync(20000);
                return page;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await page.CloseAsync();
                if (attempt < 2)
                    await Task.Delay(1500);
            }
        }

        throw new InvalidOperationException($"Login failed for {loginEmail} after 3 attempts.", lastError);
    }

    public static async Task WaitForTestIdAsync(this IPage page, string testId, int timeoutMs = 10000)
    {
        await page.WaitForSelectorAsync($"[data-testid='{testId}']", new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
    }

    public static async Task ClickByTestIdAsync(this IPage page, string testId)
    {
        await ClickByTestIdWhenReadyAsync(page, testId);
    }

    /// <summary>
    /// Clicks a test-id after it is visible and scrolls into view (Blazor circuit-safe).
    /// </summary>
    public static async Task ClickByTestIdWhenReadyAsync(this IPage page, string testId, int timeoutMs = 30000)
    {
        var locator = page.Locator($"[data-testid='{testId}']").First;
        await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        await locator.ScrollIntoViewIfNeededAsync();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await locator.ClickAsync(new() { Timeout = 10000 });
                return;
            }
            catch (PlaywrightException) when (attempt < 2)
            {
                await Task.Delay(750);
            }
        }

        await locator.ClickAsync(new() { Force = true, Timeout = 10000 });
    }

    public static async Task FillByTestIdAsync(this IPage page, string testId, string value)
    {
        var locator = page.Locator($"[data-testid='{testId}']");
        await locator.FillAsync(value);
        // Blazor @oninput handlers need an explicit input event after FillAsync (Playwright can miss it on SSR).
        await locator.DispatchEventAsync("input");
        await locator.DispatchEventAsync("change");
    }

    /// <summary>
    /// Waits for a list page to finish loading (data-testid="{pageKey}-ready").
    /// </summary>
    public static async Task WaitForListReadyAsync(this IPage page, string pageKey, int timeoutMs = 30000)
    {
        try
        {
            await page.WaitForTestIdAsync($"{pageKey}-ready", timeoutMs);
        }
        catch (TimeoutException)
        {
            // Older images may not expose *-ready; caller already waited for table.
        }
    }

    /// <summary>
    /// Pages with InteractiveServer prerender:false need the Blazor circuit before *-ready / tables appear.
    /// </summary>
    public static async Task WaitForInteractiveListAsync(
        this IPage page,
        string relativePath,
        string pageKey,
        string tableTestId,
        int timeoutMs = 45000)
    {
        await WaitForInteractivePageAsync(page, relativePath, $"{pageKey}-ready", tableTestId, timeoutMs);
    }

    /// <summary>
    /// Waits for InteractiveServer prerender:false pages (empty shell until the circuit loads data).
    /// </summary>
    public static async Task WaitForInteractivePageAsync(
        this IPage page,
        string relativePath,
        string readyTestId,
        string contentTestId,
        int timeoutMs = 45000)
    {
        var readySelector = $"[data-testid='{readyTestId}']";
        var contentSelector = $"[data-testid='{contentTestId}']";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await page.GotoRelativeAsync(relativePath);
                await page.WaitForSelectorAsync(
                    $"{readySelector}, {contentSelector}",
                    new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
                await page.WaitForSelectorAsync(contentSelector, new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
                return;
            }
            catch (TimeoutException) when (attempt < 2)
            {
                await Task.Delay(2000);
            }
        }

        await page.GotoRelativeAsync(relativePath);
        await page.WaitForSelectorAsync(contentSelector, new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
    }

    public static async Task WaitForJobsReadyAsync(this IPage page, int timeoutMs = 45000)
    {
        await WaitForInteractivePageAsync(page, "/jobs", "jobs-ready", "jobs-table", timeoutMs);
    }

    public static async Task OpenJobDetailAsync(
        this IPage page,
        string searchMarker,
        string rowTestId = "job-row-e2e-invoice-demo",
        int timeoutMs = 45000)
    {
        await page.WaitForJobsReadyAsync(timeoutMs);
        await page.FillByTestIdAsync("jobs-search", searchMarker);
        await page.WaitForJobsReadyAsync(timeoutMs / 2);

        var jobRow = page.Locator($"[data-testid='{rowTestId}']").First;
        if (await jobRow.CountAsync() == 0)
            jobRow = page.Locator("[data-testid='job-row-with-travel']").First;
        if (await jobRow.CountAsync() == 0)
            jobRow = page.Locator("[data-testid='jobs-table'] tbody tr").First;

        await jobRow.Locator("[data-testid='job-view-button']").ClickAsync();
        await page.WaitForTestIdAsync("job-detail-panel", timeoutMs);
    }

    public static async Task OpenSalesOrderDetailAsync(
        this IPage page,
        string? searchMarker = null,
        string rowTestId = "sales-order-row-e2e-convertible",
        int timeoutMs = 45000)
    {
        await page.WaitForSalesOrdersReadyAsync(timeoutMs);

        if (!string.IsNullOrWhiteSpace(searchMarker))
        {
            await page.FillByTestIdAsync("sales-orders-search", searchMarker);
            await page.WaitForSalesOrdersReadyAsync(timeoutMs / 2);
        }

        var soRow = page.Locator($"[data-testid='{rowTestId}']").First;
        if (await soRow.CountAsync() == 0)
            soRow = page.Locator("[data-testid='sales-orders-table'] tbody tr").First;

        await soRow.Locator("[data-testid='sales-order-view']").ClickAsync();
        await page.WaitForTestIdAsync("sales-order-detail", timeoutMs);
    }

    /// <summary>
    /// Fills a search box and waits until a matching table row is visible (Blazor debounce-safe).
    /// </summary>
    public static async Task WaitForSalesOrdersReadyAsync(this IPage page, int timeoutMs = 45000)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await page.WaitForTestIdAsync("sales-orders-ready", timeoutMs);
                return;
            }
            catch (TimeoutException) when (attempt < 2)
            {
                await EnsureConvertibleSalesOrderAsync();
                await page.GotoRelativeAsync("/sales-orders");
            }
        }
    }

    public static async Task FillSearchAndExpectRowAsync(
        this IPage page,
        string searchTestId,
        string tableTestId,
        string searchTerm,
        string expectedRowText,
        int timeoutMs = 20000)
    {
        await page.FillByTestIdAsync(searchTestId, searchTerm);
        var tableBody = page.Locator($"[data-testid='{tableTestId}'] tbody");
        await Microsoft.Playwright.Assertions.Expect(
            tableBody.Locator("tr").Filter(new() { HasText = expectedRowText }))
            .ToHaveCountAsync(1, new() { Timeout = timeoutMs });
    }

    public static async Task GotoRelativeAsync(this IPage page, string relativePath, string? baseUrl = null)
    {
        var url = baseUrl ?? BaseUrl;
        var cleanPath = relativePath.TrimStart('/');
        await page.GotoAsync($"{url}/{cleanPath}", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await page.WaitForLoadStateAsync(LoadState.Load, new() { Timeout = 30000 });
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 8000 });
        }
        catch (TimeoutException)
        {
            // Blazor Server keeps sockets open — load + test-id waits are enough.
        }
    }

    public static async Task<string> TakeScreenshotAsync(this IPage page, string testName, bool isFailure = false)
    {
        var prefix = isFailure ? "e2e-failure" : "e2e";
        var fileName = $"{prefix}-{testName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png";
        await page.ScreenshotAsync(new() { Path = fileName, FullPage = true });
        return fileName;
    }

    public static async Task RunWithScreenshotOnFailureAsync(this IPage page, string testName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            await page.TakeScreenshotAsync(testName, isFailure: true);
            throw;
        }
    }

    public static async Task<string> WaitAndSaveDownloadAsync(this IPage page, string selector, string outputPrefix = "e2e-download")
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.ClickAsync(selector);
        var download = await downloadTask;
        var fileName = $"{outputPrefix}-{Guid.NewGuid()}.pdf";
        await download.SaveAsAsync(fileName);
        return fileName;
    }

    /// <summary>
    /// Polls /health/ready before E2E runs (docker-compose or local dotnet run).
    /// </summary>
    public static async Task<HttpResponseMessage> PostStripeWebhookAsync(string payload, string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        return await client.PostAsync($"{url}/webhooks/stripe", content);
    }

    /// <summary>
    /// Resets the Sent receive-demo PO (Development endpoint). Makes repeated E2E runs stable.
    /// </summary>
    public static async Task EnsureReceiveDemoPoAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/ensure-receive-demo-po", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Resets a Sent convertible quote with travel (Development endpoint).
    /// </summary>
    public static async Task<string?> EnsureConvertibleQuoteAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/ensure-convertible-quote", null);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("quoteNumber", out var prop) ? prop.GetString() : null;
    }

    /// <summary>
    /// Resets the demo invoice job (Development endpoint). Makes job→invoice E2E stable across runs.
    /// </summary>
    public static async Task<(string JobNumber, Guid JobId)?> EnsureDemoInvoiceJobAsync(string? baseUrl = null, int maxAttempts = 1)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        Exception? lastError = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var response = await client.PostAsync($"{url}/e2e/ensure-demo-invoice-job", null);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("jobNumber", out var numProp) || !root.TryGetProperty("jobId", out var idProp))
                    return null;
                var jobNumber = numProp.GetString();
                if (string.IsNullOrWhiteSpace(jobNumber) || !Guid.TryParse(idProp.GetString(), out var jobId))
                    return null;
                return (jobNumber, jobId);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < maxAttempts - 1)
                    await Task.Delay(3000);
            }
        }

        if (lastError != null)
            throw new InvalidOperationException("Demo invoice job endpoint not ready.", lastError);

        return null;
    }

    /// <summary>
    /// Resets a Confirmed convertible sales order with travel (Development endpoint).
    /// </summary>
    public static async Task<string?> EnsureConvertibleSalesOrderAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/ensure-convertible-sales-order", null);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("soNumber", out var prop) ? prop.GetString() : null;
    }

    /// <summary>
    /// Sets Acme quote quota to 1/1 used (Development endpoint) for quota-exceeded E2E.
    /// </summary>
    public static async Task EnsureQuoteQuotaExceededAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/ensure-quote-quota-exceeded", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Restores Acme demo monthly quotas after quota E2E (Development endpoint).
    /// </summary>
    public static async Task EnsureJobQuotaExceededAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/ensure-job-quota-exceeded", null);
        response.EnsureSuccessStatusCode();
    }

    public static async Task EnsureInvoiceQuotaExceededAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/ensure-invoice-quota-exceeded", null);
        response.EnsureSuccessStatusCode();
    }

    public static async Task ResetDemoQuotasAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/reset-demo-quotas", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Resets quotas, spine demo entities, and beta 2FA — stabilizes full sequential E2E runs.
    /// </summary>
    public static async Task ResetDemoStateAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var response = await client.PostAsync($"{url}/e2e/reset-demo-state", null);
        response.EnsureSuccessStatusCode();
    }

    public static async Task DisableBetaTwoFactorAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/disable-beta-two-factor", null);
        response.EnsureSuccessStatusCode();
    }

    public static async Task WaitForAccountReadyAsync(this IPage page, string panelTestId, string relativePath, int timeoutMs = 45000)
    {
        var loadedSelector = panelTestId switch
        {
            "account-hub-ready" =>
                "[data-testid='account-tab-billing'], [data-testid='account-tab-security']",
            "account-billing-ready" =>
                "[data-testid='account-billing-tier'], [data-testid='account-billing-no-tenant'], [data-testid='account-billing-quota-exceeded-banner']",
            "account-security-ready" =>
                "[data-testid='2fa-enable-button'], [data-testid='2fa-status-enabled'], [data-testid='2fa-status-disabled']",
            _ => $"[data-testid='{panelTestId}']"
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await page.GotoRelativeAsync(relativePath);
                await page.WaitForTestIdAsync(panelTestId, timeoutMs);

                var loadingSelector = panelTestId switch
                {
                    "account-billing-ready" => "[data-testid='account-billing-loading']",
                    "account-security-ready" => "[data-testid='account-security-loading']",
                    _ => null
                };

                if (loadingSelector != null)
                {
                    var loading = page.Locator(loadingSelector);
                    if (await loading.CountAsync() > 0)
                        await loading.First.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
                }

                await page.WaitForSelectorAsync(loadedSelector, new() { Timeout = timeoutMs });
                return;
            }
            catch (TimeoutException) when (attempt < 2)
            {
                await Task.Delay(2000);
            }
        }

        await page.GotoRelativeAsync(relativePath);
        await page.WaitForTestIdAsync(panelTestId, timeoutMs);
        await page.WaitForSelectorAsync(loadedSelector, new() { Timeout = timeoutMs });
    }

    public static async Task BeginEmailCaptureAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/begin-email-capture", null);
        response.EnsureSuccessStatusCode();
    }

    public static async Task ClearEmailCaptureAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/clear-email-capture", null);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<IReadOnlyList<CapturedEmailDto>> GetCapturedEmailsAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.GetAsync($"{url}/e2e/captured-emails");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<List<CapturedEmailDto>>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    public sealed record CapturedEmailDto(string To, string Subject, string HtmlBody, DateTimeOffset CapturedAtUtc);

    public static string MailpitApiUrl =>
        Environment.GetEnvironmentVariable("METERP_MAILPIT_API_URL") ?? "http://localhost:8025";

    public static bool RequireMailpit =>
        string.Equals(Environment.GetEnvironmentVariable("METERP_REQUIRE_MAILPIT"), "true", StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> IsMailpitAvailableAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync($"{MailpitApiUrl.TrimEnd('/')}/api/v1/info");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task DeleteAllMailpitMessagesAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var response = await client.DeleteAsync($"{MailpitApiUrl.TrimEnd('/')}/api/v1/messages");
        response.EnsureSuccessStatusCode();
    }

    public static async Task<IReadOnlyList<MailpitMessageSummary>> GetMailpitMessagesAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var response = await client.GetAsync($"{MailpitApiUrl.TrimEnd('/')}/api/v1/messages");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("messages", out var messages))
            return [];

        var list = new List<MailpitMessageSummary>();
        foreach (var item in messages.EnumerateArray())
        {
            var subject = item.TryGetProperty("Subject", out var subj) ? subj.GetString() ?? "" : "";
            var to = item.TryGetProperty("To", out var toEl)
                ? string.Join(", ", toEl.EnumerateArray().Select(FormatMailpitAddress))
                : "";
            list.Add(new MailpitMessageSummary(subject, to));
        }

        return list;
    }

    public static async Task WaitForMailpitSubjectAsync(string subjectFragment, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(20);
        var deadline = DateTime.UtcNow + timeout.Value;
        while (DateTime.UtcNow < deadline)
        {
            var messages = await GetMailpitMessagesAsync();
            if (messages.Any(m => m.Subject.Contains(subjectFragment, StringComparison.OrdinalIgnoreCase)))
                return;
            await Task.Delay(500);
        }

        throw new TimeoutException($"Mailpit did not receive a message containing '{subjectFragment}' within {timeout.Value.TotalSeconds}s.");
    }

    public sealed record MailpitMessageSummary(string Subject, string To);

    private static string FormatMailpitAddress(System.Text.Json.JsonElement address)
    {
        if (address.ValueKind == System.Text.Json.JsonValueKind.String)
            return address.GetString() ?? "";

        if (address.TryGetProperty("Address", out var addr))
            return addr.GetString() ?? "";

        return "";
    }

    public static async Task EnsureAppReadyAsync(string? baseUrl = null, int maxAttempts = 30, int delayMs = 2000)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var response = await client.GetAsync($"{url}/health/ready");
                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(delayMs);
                    continue;
                }

                // Health can pass before DatabaseSeeder finishes — poll E2E setup until demo data exists.
                if (await TryPrepareE2EDemoDataAsync(url))
                {
                    await Task.Delay(1000);
                    return;
                }
            }
            catch
            {
                // App still starting
            }

            await Task.Delay(delayMs);
        }

        throw new InvalidOperationException($"App not ready at {url} after {maxAttempts} attempts (health + E2E seed).");
    }

    private static async Task<bool> TryPrepareE2EDemoDataAsync(string url)
    {
        try
        {
            await ResetDemoStateAsync(url);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Task InstallMeterpClipboardStubAsync(this IPage page) =>
        page.EvaluateAsync(@"() => {
            window.__meterpClipboard = '';
            window.meterpClipboard = {
                write: async (text) => { window.__meterpClipboard = text; }
            };
        }");

    public static Task<string> ReadCapturedClipboardAsync(this IPage page) =>
        page.EvaluateAsync<string>("() => window.__meterpClipboard ?? ''");

    public static async Task WaitForAppReadyAsync(this IPage page, int timeoutMs = 15000)
    {
        try
        {
            await page.WaitForSelectorAsync("main, [data-testid='quotes-table'], [data-testid='jobs-table'], [data-testid='invoices-table']",
                new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
        }
        catch
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        }
    }
}