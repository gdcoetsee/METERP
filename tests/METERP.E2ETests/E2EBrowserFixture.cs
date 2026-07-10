using Microsoft.Playwright;
using Xunit;

namespace METERP.E2ETests;

/// <summary>
/// One Chromium instance for the whole E2E collection (avoids host crashes from
/// launch/teardown on every test when using per-test IAsyncLifetime).
/// </summary>
public sealed class E2EBrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public async Task InitializeAsync()
    {
        await E2EHelpers.EnsureAppReadyAsync();
        try { await E2EHelpers.ResetDemoStateAsync(); }
        catch { /* older images */ }

        _playwright = await Playwright.CreateAsync();
        var browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        E2EHelpers.TrackBrowser(_playwright, browser);
    }

    public async Task DisposeAsync()
    {
        try { await E2EHelpers.DisableBetaTwoFactorAsync(); }
        catch { /* ignore */ }

        try
        {
            await E2EHelpers.GetBrowser().DisposeAsync();
        }
        catch (InvalidOperationException)
        {
            /* already disposed / never started */
        }

        _playwright?.Dispose();
    }
}
