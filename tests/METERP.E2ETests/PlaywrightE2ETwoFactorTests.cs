using Microsoft.Playwright;
using Xunit;

namespace METERP.E2ETests;

/// <summary>
/// Beta 2FA flows isolated so authenticator state cannot leak into the main E2E suite.
/// </summary>
[Trait("Category", "E2E")]
[Collection("E2E")]
public class E2ETwoFactorFlowTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser Browser => E2EHelpers.GetBrowser();

    public async Task InitializeAsync()
    {
        await E2EHelpers.EnsureAppReadyAsync();
        await E2EHelpers.ResetDemoStateAsync();
        await E2EHelpers.DisableBetaTwoFactorAsync();
        _playwright = await Playwright.CreateAsync();
        E2EHelpers.TrackBrowser(_playwright, await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }));
    }

    public async Task DisposeAsync()
    {
        await E2EHelpers.DisableBetaTwoFactorAsync();
        try { await E2EHelpers.GetBrowser().DisposeAsync(); }
        catch (InvalidOperationException) { /* already disposed */ }
        _playwright?.Dispose();
    }

    [Fact]
    public async Task AccountSecurity_Enables_TwoFactor_And_Login_Challenge()
    {
        try
        {
            var secretMaterial = await EnableTwoFactorForBetaAsync();

            var loginPage = await Browser.NewPageAsync();
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
                    if (!loginPage.Url.Contains("login-2fa", StringComparison.OrdinalIgnoreCase))
                        throw;
                }
            }

            Assert.True(loggedIn, "Expected authenticator login to succeed after 2FA enable.");
            var home = await loginPage.ContentAsync();
            Assert.Contains("Beta", home, StringComparison.OrdinalIgnoreCase);
            await loginPage.CloseAsync();
        }
        finally
        {
            await E2EHelpers.DisableBetaTwoFactorAsync();
        }
    }

    [Fact]
    public async Task AccountSecurity_EnableTwoFactor_Captures_Security_Email()
    {
        try
        {
            await E2EHelpers.BeginEmailCaptureAsync();
            await E2EHelpers.DisableBetaTwoFactorAsync();

            var setupPage = await Browser.LoginAsync(E2EHelpers.BetaEmail, E2EHelpers.BetaPassword, resetDemoState: false);
            await setupPage.WaitForAccountReadyAsync("account-security-ready", "/account-security");

            var secretMaterial = await setupPage.BeginTwoFactorSetupAsync();

            var setupCode = TotpHelper.ComputeCurrentCode(secretMaterial);
            await setupPage.Locator("[data-testid='2fa-confirm-code']").PressSequentiallyAsync(setupCode, new() { Delay = 80 });
            await setupPage.ClickByTestIdAsync("2fa-confirm-button");
            await setupPage.WaitForTestIdAsync("2fa-status-enabled", 30000);

            IReadOnlyList<E2EHelpers.CapturedEmailDto> captured = [];
            for (var attempt = 0; attempt < 10; attempt++)
            {
                captured = await E2EHelpers.GetCapturedEmailsAsync();
                if (captured.Any(m => m.Subject.Contains("enabled", StringComparison.OrdinalIgnoreCase)))
                    break;
                await Task.Delay(500);
            }

            Assert.Contains(captured, m =>
                m.To.Contains("beta.demo", StringComparison.OrdinalIgnoreCase)
                && m.Subject.Contains("Two-factor authentication enabled", StringComparison.OrdinalIgnoreCase)
                && m.HtmlBody.Contains("enabled", StringComparison.OrdinalIgnoreCase));

            await setupPage.ClickByTestIdAsync("2fa-disable-button");
            await setupPage.WaitForTestIdAsync("2fa-status-disabled", 20000);
            await setupPage.CloseAsync();
        }
        finally
        {
            await E2EHelpers.ClearEmailCaptureAsync();
            await E2EHelpers.DisableBetaTwoFactorAsync();
        }
    }

    [Fact]
    public async Task AccountSecurity_EnableTwoFactor_Delivers_To_Mailpit_When_Smtp_Configured()
    {
        if (!await E2EHelpers.IsMailpitAvailableAsync())
        {
            if (E2EHelpers.RequireMailpit)
                Assert.Fail("Mailpit is required (METERP_REQUIRE_MAILPIT=true) but is not reachable.");
            return;
        }

        try
        {
            await E2EHelpers.DeleteAllMailpitMessagesAsync();
            await E2EHelpers.DisableBetaTwoFactorAsync();

            var setupPage = await Browser.LoginAsync(E2EHelpers.BetaEmail, E2EHelpers.BetaPassword, resetDemoState: false);
            await setupPage.WaitForAccountReadyAsync("account-security-ready", "/account-security");

            var secretMaterial = await setupPage.BeginTwoFactorSetupAsync();

            var setupCode = TotpHelper.ComputeCurrentCode(secretMaterial);
            await setupPage.Locator("[data-testid='2fa-confirm-code']").PressSequentiallyAsync(setupCode, new() { Delay = 80 });
            await setupPage.ClickByTestIdAsync("2fa-confirm-button");
            await setupPage.WaitForTestIdAsync("2fa-status-enabled", 30000);
            await setupPage.CloseAsync();

            await E2EHelpers.WaitForMailpitSubjectAsync("Two-factor authentication enabled");

            var messages = await E2EHelpers.GetMailpitMessagesAsync();
            Assert.Contains(messages, m =>
                m.Subject.Contains("Two-factor authentication enabled", StringComparison.OrdinalIgnoreCase)
                && m.To.Contains("beta.demo", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await E2EHelpers.DisableBetaTwoFactorAsync();
        }
    }

    private async Task<string> EnableTwoFactorForBetaAsync()
    {
        await E2EHelpers.DisableBetaTwoFactorAsync();
        var setupPage = await Browser.LoginAsync(E2EHelpers.BetaEmail, E2EHelpers.BetaPassword, resetDemoState: false);
        await setupPage.WaitForAccountReadyAsync("account-security-ready", "/account-security");

        var secretMaterial = await setupPage.BeginTwoFactorSetupAsync();

        var setupCode = TotpHelper.ComputeCurrentCode(secretMaterial);
        await setupPage.Locator("[data-testid='2fa-confirm-code']").PressSequentiallyAsync(setupCode, new() { Delay = 80 });
        await setupPage.ClickByTestIdAsync("2fa-confirm-button");
        await setupPage.WaitForTestIdAsync("2fa-status-enabled", 30000);
        await setupPage.CloseAsync();

        return secretMaterial;
    }
}