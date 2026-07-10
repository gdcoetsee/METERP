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
    public const string TechEmail = "tech@acme.demo";
    public const string TechPassword = "Demo123!";
    public const string BetaEmail = "admin@beta.demo";
    public const string BetaPassword = "Demo123!";

    private static int _loginCount;
    private static IPlaywright? _trackedPlaywright;
    private static IBrowser? _trackedBrowser;

    /// <summary>
    /// Enables periodic Chromium recycle during long sequential E2E runs (reduces stale Blazor circuit pressure).
    /// </summary>
    public static void TrackBrowser(IPlaywright playwright, IBrowser browser)
    {
        _trackedPlaywright = playwright;
        _trackedBrowser = browser;
    }

    public static IBrowser GetBrowser()
    {
        if (_trackedBrowser == null)
            throw new InvalidOperationException("E2E browser not initialized — call TrackBrowser during test fixture setup.");
        return _trackedBrowser;
    }

    public static async Task<IPage> LoginAsync(
        this IBrowser browser,
        string? email = null,
        string? password = null,
        string? baseUrl = null,
        bool resetDemoState = false)
    {
        var url = baseUrl ?? BaseUrl;
        var loginEmail = email ?? AcmeEmail;

        // Periodic reset stabilizes long runs without paying full reset cost on every login (~2–3s each).
        var loginNumber = Interlocked.Increment(ref _loginCount);
        if (loginNumber % 8 == 0)
            browser = await MaybeRecycleBrowserAsync(browser);

        if (resetDemoState || loginNumber % 4 == 1)
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
                // Drop prior tenant cookies so multi-tenant E2E never reuses the wrong session.
                try { await page.Context.ClearCookiesAsync(); }
                catch { /* ignore */ }

                await page.GotoAsync(
                    $"{url}/login-complete?email={Uri.EscapeDataString(loginEmail)}&_={DateTime.UtcNow.Ticks}",
                    new() { Timeout = 60000, WaitUntil = WaitUntilState.Load });
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

    private static async Task<IBrowser> MaybeRecycleBrowserAsync(IBrowser browser)
    {
        if (_trackedPlaywright == null || _trackedBrowser == null || !ReferenceEquals(browser, _trackedBrowser))
            return browser;

        await _trackedBrowser.DisposeAsync();
        await Task.Delay(750);
        _trackedBrowser = await _trackedPlaywright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        try { await ResetDemoStateAsync(); }
        catch { /* dev endpoints unavailable on older images */ }

        return _trackedBrowser;
    }

    public static async Task WaitForTestIdAsync(this IPage page, string testId, int timeoutMs = 10000)
    {
        await page.WaitForSelectorAsync($"[data-testid='{testId}']", new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Clicks an AI quick-prompt and waits for ai-last-response (retries if the circuit missed the first click).
    /// </summary>
    /// <summary>Opens the quote create editor via deep link (most reliable on InteractiveServer).</summary>
    public static async Task OpenNewQuoteEditorAsync(this IPage page, int timeoutMs = 30000)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                // Load once to discover a real customer id, then reopen with customer preselected
                // (Playwright select binding is unreliable with Blazor InputSelect/Guid).
                await page.GotoAsync(
                    $"{BaseUrl.TrimEnd('/')}/quotes?create=1",
                    new() { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
                await page.WaitForBlazorReadyAsync(20000);
                await page.WaitForSelectorAsync(
                    "[data-testid='quote-editor']",
                    new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });

                var customerId = await page.Locator("[data-testid='quote-customer-select'] option")
                    .Nth(1)
                    .GetAttributeAsync("value");
                if (!string.IsNullOrWhiteSpace(customerId)
                    && !string.Equals(customerId, Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    await page.GotoAsync(
                        $"{BaseUrl.TrimEnd('/')}/quotes?create=1&customerId={Uri.EscapeDataString(customerId)}",
                        new() { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
                    await page.WaitForBlazorReadyAsync(20000);
                    await page.WaitForSelectorAsync(
                        "[data-testid='quote-editor']",
                        new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
                }

                return;
            }
            catch (Exception) when (attempt < 2)
            {
                await Task.Delay(750 + attempt * 400);
            }
        }

        await page.WaitForTestIdAsync("quote-editor", timeoutMs);
    }

    /// <summary>Opens the jobs list detail panel via ?panel= deep link.</summary>
    public static async Task OpenJobDetailPanelAsync(this IPage page, ILocator jobRow, int timeoutMs = 30000)
    {
        var href = await jobRow.Locator("[data-testid='job-command-center-button'], [data-testid='job-command-center-link']")
            .First
            .GetAttributeAsync("href");
        if (string.IsNullOrWhiteSpace(href))
            throw new InvalidOperationException("Job row has no command-center link to extract job id.");

        var idPart = href.TrimEnd('/').Split('/').LastOrDefault();
        if (!Guid.TryParse(idPart, out var jobId) || jobId == Guid.Empty)
            throw new InvalidOperationException($"Could not parse job id from href '{href}'.");

        await page.GotoAsync(
            $"{BaseUrl.TrimEnd('/')}/jobs?panel={jobId}",
            new() { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await page.WaitForBlazorReadyAsync(20000);
        await page.WaitForTestIdAsync("job-detail-panel", timeoutMs);
    }

    /// <summary>Opens a quote row's editor via Edit/View, falling back to ?open= deep link.</summary>
    public static async Task OpenQuoteRowEditorAsync(this IPage page, ILocator row, int timeoutMs = 30000)
    {
        var quoteId = await row.Locator("[data-testid='quote-view-button']").GetAttributeAsync("data-quote-id");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var edit = row.Locator("[data-testid='quote-edit-button']");
                if (await edit.CountAsync() > 0)
                    await edit.ClickAsync(new() { Force = true, Timeout = 10000 });
                else
                    await row.Locator("[data-testid='quote-view-button']").ClickAsync(new() { Force = true, Timeout = 10000 });

                await page.WaitForSelectorAsync(
                    "[data-testid='quote-editor']",
                    new() { Timeout = Math.Max(8000, timeoutMs / 2), State = WaitForSelectorState.Visible });
                return;
            }
            catch (Exception) when (attempt < 2)
            {
                await Task.Delay(400);
            }
        }

        if (!string.IsNullOrWhiteSpace(quoteId))
        {
            await page.GotoAsync(
                $"{BaseUrl.TrimEnd('/')}/quotes?open={quoteId}",
                new() { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
            await page.WaitForBlazorReadyAsync(20000);
            await page.WaitForTestIdAsync("quote-editor", timeoutMs);
            return;
        }

        await page.WaitForTestIdAsync("quote-editor", timeoutMs);
    }

    public static async Task ClickAiQuickPromptAndWaitAsync(this IPage page, string promptTestId, int timeoutMs = 45000)
    {
        var prompt = page.Locator($"[data-testid='{promptTestId}']").First;
        await prompt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await Microsoft.Playwright.Assertions.Expect(prompt).ToBeEnabledAsync(new() { Timeout = 20000 });

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await prompt.ScrollIntoViewIfNeededAsync();
                await prompt.ClickAsync(new() { Timeout = 10000 });
                await page.WaitForSelectorAsync(
                    "[data-testid='ai-last-response']",
                    new() { Timeout = Math.Max(8000, timeoutMs / 3), State = WaitForSelectorState.Visible });
                return;
            }
            catch (Exception) when (attempt < 2)
            {
                await Task.Delay(750 + attempt * 500);
                try
                {
                    await prompt.ClickAsync(new() { Force = true, Timeout = 5000 });
                }
                catch
                {
                    /* retry outer loop */
                }
            }
        }

        await page.WaitForSelectorAsync(
            "[data-testid='ai-last-response']",
            new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
    }

    /// <summary>Waits for the access-denied page after a forbidden navigation (Blazor authorize redirect).</summary>
    public static async Task WaitForAccessDeniedAsync(this IPage page, int timeoutMs = 20000)
    {
        try
        {
            await page.WaitForSelectorAsync(
                "[data-testid='access-denied-ready'], h3:has-text('Access Denied')",
                new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
        }
        catch (TimeoutException)
        {
            // Fallback: body text (some authorize paths render before test-id is available).
            await page.WaitForFunctionAsync(
                "() => document.body && document.body.innerText.toLowerCase().includes('access denied')",
                null,
                new() { Timeout = timeoutMs });
        }
    }

    public static async Task ClickByTestIdAsync(this IPage page, string testId)
    {
        await ClickByTestIdWhenReadyAsync(page, testId);
    }

    public static async Task ClickByTestIdWhenEnabledAsync(this IPage page, string testId, int timeoutMs = 30000)
    {
        var locator = page.Locator($"[data-testid='{testId}']").First;
        await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        await Microsoft.Playwright.Assertions.Expect(locator).ToBeEnabledAsync(new() { Timeout = timeoutMs });
        await ClickByTestIdWhenReadyAsync(page, testId, timeoutMs);
    }

    /// <summary>
    /// Clicks a test-id after it is visible and scrolls into view (Blazor circuit-safe).
    /// </summary>
    public static async Task ClickByTestIdWhenReadyAsync(this IPage page, string testId, int timeoutMs = 30000)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var locator = page.Locator($"[data-testid='{testId}']").First;
                await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
                await locator.ScrollIntoViewIfNeededAsync();
                await locator.ClickAsync(new() { Timeout = 10000 });
                return;
            }
            catch (PlaywrightException) when (attempt < 3)
            {
                await Task.Delay(500 + attempt * 250);
            }
        }

        var fallback = page.Locator($"[data-testid='{testId}']").First;
        await fallback.ClickAsync(new() { Force = true, Timeout = 10000 });
    }

    public static async Task OpenFirstOpportunityDetailAsync(this IPage page, int timeoutMs = 30000)
    {
        // Prefer seeded opportunities that have a linked customer (manual quote convert needs it).
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var withCustomer = page.Locator("[data-testid='opportunity-card-with-customer']");
                var card = await withCustomer.CountAsync() > 0
                    ? withCustomer.First
                    : page.Locator("[data-testid='opportunity-card'], [data-testid='opportunity-card-with-customer']").First;
                await card.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
                await card.ScrollIntoViewIfNeededAsync();
                await card.ClickAsync(new() { Timeout = 10000 });
                await page.WaitForTestIdAsync("opportunity-detail", timeoutMs);
                return;
            }
            catch (Exception) when (attempt < 3)
            {
                await Task.Delay(500 + attempt * 250);
            }
        }

        var fallback = page.Locator("[data-testid='opportunity-card-with-customer'], [data-testid='opportunity-card']").First;
        await fallback.ClickAsync(new() { Force = true });
        await page.WaitForTestIdAsync("opportunity-detail", timeoutMs);
    }

    public static async Task FillByTestIdAsync(this IPage page, string testId, string value)
    {
        var locator = page.Locator($"[data-testid='{testId}']");
        await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await locator.ClickAsync();
        await locator.ClearAsync();
        if (string.IsNullOrEmpty(value))
        {
            await locator.DispatchEventAsync("input");
            await locator.BlurAsync();
            return;
        }

        // PressSequentially triggers Blazor @oninput / @bind:after reliably on InteractiveServer circuits.
        await locator.PressSequentiallyAsync(value, new() { Delay = 40 });
        await locator.DispatchEventAsync("change");
        await locator.BlurAsync();
    }

    /// <summary>Selects a non-empty option in a Blazor-bound &lt;select&gt; / InputSelect (fires change for the circuit).</summary>
    public static async Task SelectFirstRealOptionAsync(this IPage page, string selectTestId, int timeoutMs = 15000)
    {
        var select = page.Locator($"[data-testid='{selectTestId}']").First;
        await select.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        var options = select.Locator("option");
        var count = await options.CountAsync();
        string? value = null;
        for (var i = 0; i < count; i++)
        {
            var v = await options.Nth(i).GetAttributeAsync("value");
            if (!string.IsNullOrWhiteSpace(v)
                && !string.Equals(v, Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase)
                && v != "0")
            {
                value = v;
                break;
            }
        }

        if (value == null)
            throw new InvalidOperationException($"No selectable option found for {selectTestId}");

        await select.SelectOptionAsync(new[] { value });
        await select.DispatchEventAsync("change");
        await select.DispatchEventAsync("input");
        // Blazor InputSelect listens for change; give the circuit a beat.
        await Task.Delay(200);
    }

    /// <summary>
    /// Waits for the Blazor Web App script to load (InteractiveServer / prerender:false pages).
    /// </summary>
    public static async Task WaitForBlazorReadyAsync(this IPage page, int timeoutMs = 15000)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "() => typeof window.Blazor !== 'undefined'",
                null,
                new() { Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
            // Content selectors below are authoritative when Blazor is already bootstrapped.
        }
    }

    private static async Task WaitForLoadingGoneAsync(IPage page, string? loadingTestId, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(loadingTestId))
            return;

        var loading = page.Locator($"[data-testid='{loadingTestId}']");
        if (await loading.CountAsync() == 0)
            return;

        try
        {
            await loading.First.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
            // Table/content wait below will surface the real failure.
        }
    }

    private static string? InferLoadingTestId(string contentTestId) =>
        contentTestId.EndsWith("-table", StringComparison.Ordinal)
            ? contentTestId.Replace("-table", "-loading", StringComparison.Ordinal)
            : null;

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
        int timeoutMs = 60000)
    {
        await page.WaitForListPageAsync(relativePath, tableTestId, timeoutMs);
    }

    /// <summary>
    /// Waits for a list/table page (SSR or InteractiveServer). Ignores visually-hidden *-ready markers.
    /// </summary>
    public static async Task WaitForListPageAsync(
        this IPage page,
        string relativePath,
        string tableTestId,
        int timeoutMs = 60000)
    {
        var tableSelector = $"[data-testid='{tableTestId}']";
        var loadingTestId = InferLoadingTestId(tableTestId);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await page.GotoRelativeAsync(relativePath, waitForCommit: true);
                await page.WaitForBlazorReadyAsync(Math.Min(timeoutMs / 3, 20000));
                await WaitForLoadingGoneAsync(page, loadingTestId, timeoutMs / 2);
                await page.WaitForSelectorAsync(tableSelector, new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
                await page.Locator($"{tableSelector} tbody tr").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs / 2 });
                return;
            }
            catch (TimeoutException) when (attempt < 2)
            {
                await RetryListPageRecoveryAsync(page, relativePath);
            }
        }

        await page.GotoRelativeAsync(relativePath, waitForCommit: true);
        await page.WaitForBlazorReadyAsync(Math.Min(timeoutMs / 3, 20000));
        await WaitForLoadingGoneAsync(page, loadingTestId, timeoutMs / 2);
        await page.WaitForSelectorAsync(tableSelector, new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
        await page.Locator($"{tableSelector} tbody tr").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs / 2 });
    }

    private static async Task RetryListPageRecoveryAsync(IPage page, string relativePath)
    {
        try { await ResetDemoStateAsync(); }
        catch { /* dev endpoints unavailable */ }

        try
        {
            await page.ReloadAsync(new() { WaitUntil = WaitUntilState.Commit, Timeout = 60000 });
        }
        catch (TimeoutException)
        {
            await page.GotoRelativeAsync(relativePath, waitForCommit: true);
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Waits for an interactive page element (not necessarily a table).
    /// </summary>
    public static async Task WaitForInteractivePageAsync(
        this IPage page,
        string relativePath,
        string readyTestId,
        string contentTestId,
        int timeoutMs = 60000)
    {
        var contentSelector = $"[data-testid='{contentTestId}']";
        var loadingTestId = contentTestId.EndsWith("-loading", StringComparison.Ordinal)
            ? contentTestId
            : readyTestId.Replace("-ready", "-loading", StringComparison.Ordinal);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await page.GotoRelativeAsync(relativePath, waitForCommit: true);
                await page.WaitForBlazorReadyAsync(Math.Min(timeoutMs / 3, 20000));
                await WaitForLoadingGoneAsync(page, loadingTestId, timeoutMs / 2);
                await page.WaitForSelectorAsync(contentSelector, new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
                return;
            }
            catch (TimeoutException) when (attempt < 2)
            {
                await RetryListPageRecoveryAsync(page, relativePath);
            }
        }

        await page.GotoRelativeAsync(relativePath, waitForCommit: true);
        await page.WaitForBlazorReadyAsync(Math.Min(timeoutMs / 3, 20000));
        await page.WaitForSelectorAsync(contentSelector, new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
    }

    public static async Task WaitForJobsReadyAsync(this IPage page, int timeoutMs = 45000)
    {
        await WaitForInteractivePageAsync(page, "/jobs", "jobs-ready", "jobs-table", timeoutMs);
        await WaitForLoadingGoneAsync(page, "jobs-loading", timeoutMs / 2);
        await page.Locator("[data-testid='jobs-table'] tbody tr").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs / 2 });
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
        int timeoutMs = 60000)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
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

                await soRow.Locator("[data-testid='sales-order-view']").ClickAsync(new() { Timeout = 10000 });
                await page.WaitForTestIdAsync("sales-order-detail", timeoutMs);
                return;
            }
            catch (TimeoutException) when (attempt < 2)
            {
                await EnsureConvertibleSalesOrderAsync();
                await page.GotoRelativeAsync("/sales-orders");
                await Task.Delay(1500);
            }
        }

        await page.WaitForTestIdAsync("sales-order-detail", timeoutMs);
    }

    /// <summary>
    /// Fills a search box and waits until a matching table row is visible (Blazor debounce-safe).
    /// </summary>
    public static Task WaitForSalesOrdersReadyAsync(this IPage page, int timeoutMs = 45000) =>
        page.WaitForListPageAsync("/sales-orders", "sales-orders-table", timeoutMs);

    public static Task WaitForQuotesReadyAsync(this IPage page, int timeoutMs = 45000) =>
        page.WaitForListPageAsync("/quotes", "quotes-table", timeoutMs);

    public static Task WaitForTenantsReadyAsync(this IPage page, int timeoutMs = 45000) =>
        page.WaitForListPageAsync("/tenants", "tenants-table", timeoutMs);

    public static async Task FillSearchAndExpectRowAsync(
        this IPage page,
        string searchTestId,
        string tableTestId,
        string searchTerm,
        string expectedRowText,
        int timeoutMs = 20000,
        string? excludeRowText = null)
    {
        var search = page.Locator($"[data-testid='{searchTestId}']").First;
        await search.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        await search.ClickAsync();
        // Clear + type character-by-character so Blazor @oninput handlers fire on the circuit.
        await search.FillAsync("");
        await search.DispatchEventAsync("input");
        await Task.Delay(150);
        await search.PressSequentiallyAsync(searchTerm, new() { Delay = 45 });
        await search.DispatchEventAsync("input");
        await search.DispatchEventAsync("change");
        await search.BlurAsync();
        // Confirm the input retained the typed value (catches non-interactive circuits).
        await Microsoft.Playwright.Assertions.Expect(search).ToHaveValueAsync(searchTerm, new() { Timeout = 5000 });

        // Poll until the expected row is present and optional excluded rows are gone.
        var tableBody = page.Locator($"[data-testid='{tableTestId}'] tbody");
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await WaitForLoadingGoneAsync(page, InferLoadingTestId(tableTestId), 3000);
                var matchCount = await tableBody.Locator("tr").Filter(new() { HasText = expectedRowText }).CountAsync();
                var excludeCount = string.IsNullOrWhiteSpace(excludeRowText)
                    ? 0
                    : await tableBody.Locator("tr").Filter(new() { HasText = excludeRowText }).CountAsync();
                if (matchCount > 0 && excludeCount == 0)
                    return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(400);
        }

        await Microsoft.Playwright.Assertions.Expect(
            tableBody.Locator("tr").Filter(new() { HasText = expectedRowText }))
            .Not.ToHaveCountAsync(0, new() { Timeout = 2000 });
        if (!string.IsNullOrWhiteSpace(excludeRowText))
        {
            await Microsoft.Playwright.Assertions.Expect(
                tableBody.Locator("tr").Filter(new() { HasText = excludeRowText }))
                .ToHaveCountAsync(0, new() { Timeout = 2000 });
        }
        if (last != null)
            throw last;
    }

    /// <summary>Fills a quote line description+price and saves until a matching row appears.</summary>
    public static async Task AddQuoteLineAsync(this IPage page, string description, string? unitPrice = null, int timeoutMs = 20000)
    {
        if (await page.Locator("[data-testid='quote-line-form']").CountAsync() == 0)
        {
            await page.ClickByTestIdAsync("quote-add-line-button");
            await page.WaitForTestIdAsync("quote-line-form", timeoutMs);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await page.FillByTestIdAsync("quote-line-description", description);
            if (!string.IsNullOrEmpty(unitPrice))
                await page.FillByTestIdAsync("quote-line-unit-price", unitPrice);

            var descVal = await page.Locator("[data-testid='quote-line-description']").InputValueAsync();
            if (!string.Equals(descVal, description, StringComparison.Ordinal))
            {
                await Task.Delay(300);
                continue;
            }

            await page.ClickByTestIdAsync("quote-line-save-button");
            try
            {
                await page.Locator($"[data-testid='quote-line-row']:has-text('{description}')")
                    .First.WaitForAsync(new() { Timeout = Math.Max(5000, timeoutMs / 2) });
                return;
            }
            catch (TimeoutException) when (attempt < 2)
            {
                // reopen form if save did not stick
                if (await page.Locator("[data-testid='quote-line-form']").CountAsync() == 0)
                    await page.ClickByTestIdAsync("quote-add-line-button");
            }
        }

        await page.Locator($"[data-testid='quote-line-row']:has-text('{description}')")
            .First.WaitForAsync(new() { Timeout = timeoutMs });
    }

    public static async Task GotoRelativeAsync(this IPage page, string relativePath, string? baseUrl = null, bool waitForCommit = false)
    {
        var url = baseUrl ?? BaseUrl;
        var cleanPath = relativePath.TrimStart('/');
        await page.GotoAsync($"{url}/{cleanPath}", new()
        {
            WaitUntil = waitForCommit ? WaitUntilState.Commit : WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        try
        {
            await page.WaitForLoadStateAsync(LoadState.Load, new() { Timeout = 15000 });
        }
        catch (TimeoutException)
        {
            // Blazor Server may not fire a second Load event on in-app navigation.
        }

        await page.WaitForBlazorReadyAsync(12000);
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

    /// <summary>
    /// Sets Acme AI call quota to 1/1 used (Development endpoint) for quota-exceeded E2E.
    /// </summary>
    public static async Task EnsureAiQuotaExceededAsync(string? baseUrl = null)
    {
        var url = (baseUrl ?? BaseUrl).TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await client.PostAsync($"{url}/e2e/ensure-ai-quota-exceeded", null);
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

    public static async Task WaitForAccountReadyAsync(
        this IPage page,
        string panelTestId,
        string relativePath,
        int timeoutMs = 45000,
        bool resetDemoOnRetry = true)
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

        var loadingTestId = panelTestId switch
        {
            "account-billing-ready" => "account-billing-loading",
            "account-security-ready" => "account-security-loading",
            _ => null
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var url = $"{BaseUrl}/{relativePath.TrimStart('/')}";
                await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Commit, Timeout = 60000 });
                await page.WaitForBlazorReadyAsync(Math.Min(timeoutMs / 3, 20000));
                await WaitForLoadingGoneAsync(page, loadingTestId, timeoutMs);
                await page.WaitForSelectorAsync(loadedSelector, new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
                return;
            }
            catch (TimeoutException) when (attempt < 2)
            {
                if (resetDemoOnRetry)
                {
                    try { await ResetDemoStateAsync(); }
                    catch { /* dev endpoints unavailable */ }
                }

                await Task.Delay(2000);
            }
        }

        var finalUrl = $"{BaseUrl}/{relativePath.TrimStart('/')}";
        await page.GotoAsync(finalUrl, new() { WaitUntil = WaitUntilState.Commit, Timeout = 60000 });
        await page.WaitForSelectorAsync(loadedSelector, new() { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Clicks Enable 2FA and waits for the setup panel (retries click when Blazor circuit is slow).
    /// Returns shared-key material (URI preferred) for TOTP entry.
    /// </summary>
    public static async Task<string> BeginTwoFactorSetupAsync(this IPage page, int timeoutMs = 60000)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await page.ClickByTestIdWhenEnabledAsync("2fa-enable-button", timeoutMs / 4);
            await page.WaitForBlazorReadyAsync(8000);

            try
            {
                await page.WaitForTestIdAsync("2fa-setup-panel", timeoutMs / 4);
                await page.WaitForTestIdAsync("2fa-shared-key", timeoutMs / 4);
                return await ReadTwoFactorSecretMaterialAsync(page);
            }
            catch (TimeoutException) when (attempt < 3)
            {
                if (await page.Locator("[data-testid='2fa-enable-button']").IsVisibleAsync())
                    await Task.Delay(750 + attempt * 500);
            }
        }

        await page.WaitForTestIdAsync("2fa-shared-key", timeoutMs);
        return await ReadTwoFactorSecretMaterialAsync(page);
    }

    public static async Task<string> ReadTwoFactorSecretMaterialAsync(this IPage page)
    {
        var uriText = await page.Locator("[data-testid='2fa-setup-uri']").InnerTextAsync() ?? string.Empty;
        if (uriText.Contains("secret=", StringComparison.OrdinalIgnoreCase))
            return uriText;

        return await page.Locator("[data-testid='2fa-shared-key']").InnerTextAsync() ?? string.Empty;
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