using Microsoft.Playwright;

namespace METERP.E2ETests;

/// <summary>
/// Helper extensions and utilities for reliable Playwright E2E tests.
/// These make tests less brittle by preferring data-testid, adding smart waits,
/// and centralizing common actions like login.
/// </summary>
public static class E2EHelpers
{
    /// <summary>
    /// Default base URL for the running app (matches docker-compose).
    /// Override via environment variable METERP_BASE_URL if testing against a different instance.
    /// </summary>
    public static string BaseUrl => Environment.GetEnvironmentVariable("METERP_BASE_URL") ?? "http://localhost:8080";

    public const string DemoEmail = "admin@acme.demo";
    public const string DemoPassword = "Demo123!";

    /// <summary>
    /// Performs login using the data-testid attributes we added to the login form.
    /// This is more reliable than name or text selectors.
    /// </summary>
    public static async Task<IPage> LoginAsync(this IBrowser browser, string? baseUrl = null)
    {
        var url = baseUrl ?? BaseUrl;
        var page = await browser.NewPageAsync();

        await page.GotoAsync($"{url}/login");

        // Use the data-testid we added to Login.razor for robustness
        await page.FillAsync("[data-testid='login-email']", DemoEmail);
        await page.FillAsync("[data-testid='login-password']", DemoPassword);
        await page.ClickAsync("[data-testid='login-submit']");

        // Wait for successful navigation (home or dashboard)
        await page.WaitForURLAsync(u => u.Contains("/Home") || u.Contains("/"), new() { Timeout = 15000 });

        return page;
    }

    /// <summary>
    /// Waits for a data-testid element to be visible. Prefer this over text-based waits.
    /// </summary>
    public static async Task WaitForTestIdAsync(this IPage page, string testId, int timeoutMs = 10000)
    {
        await page.WaitForSelectorAsync($"[data-testid='{testId}']", new() { Timeout = timeoutMs });
    }

    /// <summary>
    /// Clicks an element by data-testid. More reliable than :has-text().
    /// </summary>
    public static async Task ClickByTestIdAsync(this IPage page, string testId)
    {
        await page.ClickAsync($"[data-testid='{testId}']");
    }

    /// <summary>
    /// Fills an input by data-testid.
    /// </summary>
    public static async Task FillByTestIdAsync(this IPage page, string testId, string value)
    {
        await page.FillAsync($"[data-testid='{testId}']", value);
    }

    /// <summary>
    /// Navigates to a relative path under the base URL.
    /// </summary>
    public static async Task GotoRelativeAsync(this IPage page, string relativePath, string? baseUrl = null)
    {
        var url = baseUrl ?? BaseUrl;
        var cleanPath = relativePath.TrimStart('/');
        await page.GotoAsync($"{url}/{cleanPath}");
    }

    /// <summary>
    /// Takes a screenshot (useful when running in CI or debugging).
    /// Call this in catch blocks or after assertions that might fail.
    /// </summary>
    public static async Task<string> TakeScreenshotAsync(this IPage page, string testName, bool isFailure = false)
    {
        var prefix = isFailure ? "e2e-failure" : "e2e";
        var fileName = $"{prefix}-{testName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png";
        await page.ScreenshotAsync(new() { Path = fileName, FullPage = true });
        return fileName;
    }

    /// <summary>
    /// Helper to run a test action with automatic screenshot on exception.
    /// Usage in a test: await page.RunWithScreenshotOnFailureAsync("MyTestName", async () => { ... });
    /// </summary>
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

    /// <summary>
    /// Waits for a download to start and saves it, returning the filename. Useful for PDF tests.
    /// </summary>
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
    /// Common wait for the app to be ready after navigation (e.g. tables loaded).
    /// </summary>
    public static async Task WaitForAppReadyAsync(this IPage page, int timeoutMs = 10000)
    {
        // Wait for a common element that indicates the shell + data is loaded.
        // We use the main content area or a known table if present.
        try
        {
            await page.WaitForSelectorAsync("main, [data-testid='quotes-table'], [data-testid='jobs-table']", 
                new() { Timeout = timeoutMs });
        }
        catch
        {
            // Fallback - just wait for the body to be interactive
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        }
    }
}
