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

    public static async Task<IPage> LoginAsync(this IBrowser browser, string? email = null, string? password = null, string? baseUrl = null)
    {
        var url = baseUrl ?? BaseUrl;
        var page = await browser.NewPageAsync();

        // Use login-complete endpoint (same as production forceLoad path) — reliable vs Blazor @bind in Playwright.
        var loginEmail = email ?? AcmeEmail;
        await page.GotoAsync($"{url}/login-complete?email={Uri.EscapeDataString(loginEmail)}");
        await page.WaitForURLAsync(
            u => !u.Contains("login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 45000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        return page;
    }

    public static async Task WaitForTestIdAsync(this IPage page, string testId, int timeoutMs = 10000)
    {
        await page.WaitForSelectorAsync($"[data-testid='{testId}']", new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
    }

    public static async Task ClickByTestIdAsync(this IPage page, string testId)
    {
        await page.ClickAsync($"[data-testid='{testId}']");
    }

    public static async Task FillByTestIdAsync(this IPage page, string testId, string value)
    {
        await page.FillAsync($"[data-testid='{testId}']", value);
    }

    public static async Task GotoRelativeAsync(this IPage page, string relativePath, string? baseUrl = null)
    {
        var url = baseUrl ?? BaseUrl;
        var cleanPath = relativePath.TrimStart('/');
        await page.GotoAsync($"{url}/{cleanPath}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
    public static async Task EnsureAppReadyAsync(string? baseUrl = null, int maxAttempts = 30, int delayMs = 2000)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var response = await client.GetAsync($"{url}/health/ready");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // App still starting
            }

            await Task.Delay(delayMs);
        }

        throw new InvalidOperationException($"App not ready at {url}/health/ready after {maxAttempts} attempts.");
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