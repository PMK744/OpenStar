using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

using OpenStar.Core.Authentication;

namespace OpenStar.Core.BrowserLogin;

/// <summary>
/// Signs in to GM by driving a real Chromium through its Azure AD B2C pages,
/// which gets past the bot protection that blocks plain HTTP calls. The browser
/// session is held open across the MFA round-trip: <see cref="StartAsync"/>
/// submits the credentials, and <see cref="SubmitCodeAsync"/> enters the code
/// the user received. The GM password is used only for the live sign-in and is
/// never stored.
/// </summary>
public sealed class GmBrowserLogin(
    AccountManager accountManager,
    IOptions<OpenStarOptions> options,
    ILogger<GmBrowserLogin> logger) : IAsyncDisposable
{
    private const string AppRedirectScheme = "msauth.com.gm.mychevrolet://";

    private const string EmailSelector = "#logonIdentifier, input[name='logonIdentifier'], input[type='email']";
    private const string PasswordSelector = "#password, input[name='password'], input[type='password']";
    private const string SignInButtonSelector =
        "#next, #continue, button:has-text('Continue'), button:has-text('Log In'), "
        + "button:has-text('Sign in'), button[type='submit'], input[type='submit']";
    private const string CodeInputSelector =
        "input[name='otpCode'], #otpCode, input[name='emailMfa'], input[name='verificationCode'], "
        + "#email_ver_input, #emailVerificationCode, input[autocomplete='one-time-code']";
    private const string SendCodeSelector =
        "#email_ver_but_send, #emailVerificationControl_but_send_code, #sendCode, "
        + "button:has-text('Send code'), button:has-text('Send Code'), [id*='but_send']";
    private const string VerifyButtonSelector =
        "#email_ver_but_verify, #emailVerificationControl_but_verify_code, #continue, #next, "
        + "button:has-text('Verify'), button:has-text('Continue'), button[type='submit']";

    private static readonly string[] LaunchArgs =
    [
        "--disable-blink-features=AutomationControlled",
        "--no-sandbox",
        "--disable-dev-shm-usage",
    ];

    // A normal Chrome UA matching the host OS; GM's headless shell UA says
    // "HeadlessChrome", which Akamai blocks outright.
    private const string UserAgent =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly ConcurrentDictionary<string, BrowserSession> sessions = new();
    private readonly SemaphoreSlim installGate = new(1, 1);
    private IPlaywright? playwright;
    private bool browsersInstalled;

    public async Task<BrowserLoginResult> StartAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return new BrowserLoginResult(BrowserLoginStatus.Failed, Message: "Enter your GM email and password.");
        }

        // Only one sign-in at a time; discard any earlier half-finished one.
        foreach (var stale in sessions.Keys.ToArray())
        {
            await DiscardAsync(stale);
        }

        var login = await accountManager.StartLoginAsync(cancellationToken);

        BrowserSession session;
        try
        {
            session = await OpenSessionAsync(login.State, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not start the sign-in browser");
            return new BrowserLoginResult(BrowserLoginStatus.Failed,
                Message: "Could not start the sign-in browser. Is Chromium installed? See the OpenStar API log.");
        }

        try
        {
            await session.Page.GotoAsync(login.AuthorizationUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

            await session.Page.FillAsync(EmailSelector, email);

            // GM shows email first; the password field exists but stays hidden
            // until Continue is clicked. Reveal it before filling it.
            var passwordField = session.Page.Locator(PasswordSelector).First;
            if (!await passwordField.IsVisibleAsync())
            {
                await SubmitStepAsync(session.Page);
                await passwordField.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 20000,
                });
            }

            // Type the password key by key: GM enables Log In on keystroke events,
            // which FillAsync (a direct value set) doesn't fire.
            await passwordField.ClickAsync();
            await passwordField.PressSequentiallyAsync(password, new LocatorPressSequentiallyOptions { Delay = 40 });
            await session.Page.WaitForTimeoutAsync(400);
            await SubmitStepAsync(session.Page);

            return await AdvanceAsync(session, afterCode: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await FailAsync(session, "Could not submit your GM credentials.", exception);
        }
    }

    public async Task<BrowserLoginResult> SubmitCodeAsync(string state, string code, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(state, out var session))
        {
            return new BrowserLoginResult(BrowserLoginStatus.Failed, Message: "This sign-in expired. Start again.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return new BrowserLoginResult(BrowserLoginStatus.MfaRequired, state, session.MfaType, "Enter the verification code.");
        }

        try
        {
            await session.Page.FillAsync(session.CodeInputSelector ?? CodeInputSelector, code.Trim());
            await ClickFirstAsync(session.Page, VerifyButtonSelector);

            // B2C often splits "verify the code" and "continue" into two buttons.
            await TryClickAsync(session.Page, "#continue, button:has-text('Continue')");

            return await AdvanceAsync(session, afterCode: true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await FailAsync(session, "Could not submit the verification code.", exception);
        }
    }

    public Task CancelAsync(string state) => DiscardAsync(state);

    /// <summary>
    /// Waits for the sign-in to either land on the app redirect (done), reach an
    /// MFA prompt, or stall. On the app redirect it links the account.
    /// </summary>
    private async Task<BrowserLoginResult> AdvanceAsync(BrowserSession session, bool afterCode, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(options.Value.BrowserLoginTimeoutSeconds);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (session.CapturedRedirect.Task.IsCompleted)
            {
                var redirect = await session.CapturedRedirect.Task;
                await accountManager.CompleteLoginAsync(session.State, redirect, cancellationToken);
                await DiscardAsync(session.State);
                return new BrowserLoginResult(BrowserLoginStatus.Linked);
            }

            // After the code, only the redirect means success; don't re-detect the MFA page.
            if (!afterCode && await DetectMfaAsync(session) is { } mfaType)
            {
                session.MfaType = mfaType;
                return new BrowserLoginResult(BrowserLoginStatus.MfaRequired, session.State, mfaType);
            }

            if (await ReadPageErrorAsync(session.Page) is { } pageError)
            {
                return await FailAsync(session, pageError, exception: null);
            }

            await Task.Delay(500, cancellationToken);
        }

        return await FailAsync(session, "GM sign-in timed out. Please try again.", exception: null);
    }

    /// <summary>Returns the MFA kind if a code prompt is showing, else null.</summary>
    private async Task<string?> DetectMfaAsync(BrowserSession session)
    {
        var codeInput = session.Page.Locator(CodeInputSelector).First;
        if (await codeInput.CountAsync() == 0 || !await codeInput.IsVisibleAsync())
        {
            return null;
        }

        session.CodeInputSelector = CodeInputSelector;

        // Trigger delivery once, if the page has a separate "send code" button.
        if (!session.CodeRequested)
        {
            session.CodeRequested = true;
            await TryClickAsync(session.Page, SendCodeSelector);
        }

        var content = (await session.Page.ContentAsync()).ToLowerInvariant();
        if (content.Contains("text") || content.Contains("phone") || content.Contains("sms"))
        {
            return "sms";
        }

        return content.Contains("authenticator") ? "authenticator" : "email";
    }

    private static async Task<string?> ReadPageErrorAsync(IPage page)
    {
        var error = page.Locator(".error.pageLevel p, .error.itemLevel p, [role='alert']").First;
        if (await error.CountAsync() == 0 || !await error.IsVisibleAsync())
        {
            return null;
        }

        var text = (await error.InnerTextAsync()).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Chooses how to run the full Chromium so it looks like a real browser to
    /// GM's bot protection. In order of preference: a display OpenStar already
    /// has, a virtual display (Xvfb) started on demand, or, as a last resort,
    /// Chromium's new headless mode (which GM may still block).
    /// </summary>
    private (BrowserTypeLaunchOptions Launch, XvfbDisplay? Xvfb) BuildLaunchOptions()
    {
        // Always launch the full browser, never the easily-detected headless shell.
        var launch = new BrowserTypeLaunchOptions
        {
            ExecutablePath = playwright!.Chromium.ExecutablePath,
            Headless = false,
            Args = LaunchArgs,
        };

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return (launch, null);
        }

        if (XvfbDisplay.TryStart(logger) is { } xvfb)
        {
            launch.Env = new Dictionary<string, string> { ["DISPLAY"] = xvfb.Display };
            return (launch, xvfb);
        }

        if (!options.Value.BrowserLoginHeadless)
        {
            throw new InvalidOperationException(
                "Set BrowserLoginHeadless=false requires a display. Install Xvfb or run with a DISPLAY set.");
        }

        // No display available: run the full browser in the new headless mode,
        // which hides better than the headless shell but can still be blocked.
        logger.LogInformation("No display or Xvfb found; using Chromium new-headless. Install Xvfb for the most reliable GM sign-in.");
        launch.Args = [.. LaunchArgs, "--headless=new"];
        return (launch, null);
    }

    private async Task<BrowserSession> OpenSessionAsync(string state, CancellationToken cancellationToken)
    {
        await EnsureBrowsersInstalledAsync(cancellationToken);
        playwright ??= await Playwright.CreateAsync();

        var (launch, xvfb) = BuildLaunchOptions();
        var browser = await playwright.Chromium.LaunchAsync(launch);

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = UserAgent,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            Locale = "en-US",
        });

        // Hide the most obvious automation tell before any GM script runs.
        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

        var page = await context.NewPageAsync();
        // Cap every page operation so a stuck page fails fast (and writes a dump)
        // instead of hanging the sign-in indefinitely.
        page.SetDefaultTimeout(15000);
        page.SetDefaultNavigationTimeout(30000);
        var session = new BrowserSession(state, browser, context, page, xvfb);

        // GM ends by redirecting to the app scheme; catch that response's Location.
        page.Response += (_, response) =>
        {
            if (response.Headers.TryGetValue("location", out var location)
                && location.StartsWith(AppRedirectScheme, StringComparison.OrdinalIgnoreCase))
            {
                session.CapturedRedirect.TrySetResult(location);
            }

            // Record the auth-flow requests (not static assets) for failure dumps.
            var url = response.Url;
            if (url.Contains("custlogin.gm.com", StringComparison.OrdinalIgnoreCase)
                && (url.Contains("SelfAsserted", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("/confirmed", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("CombinedSigninAndSignup", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("/authorize", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("/api/", StringComparison.OrdinalIgnoreCase)))
            {
                session.RecordResponse($"{response.Status} {response.Request.Method} {new Uri(url).PathAndQuery.Split('?')[0]}");
            }
        };

        sessions[state] = session;
        return session;
    }

    private async Task<BrowserLoginResult> FailAsync(BrowserSession session, string message, Exception? exception)
    {
        if (exception is not null)
        {
            logger.LogWarning(exception, "Browser sign-in failed: {Message}", message);
        }

        var shot = await TrySaveDiagnosticsAsync(session);
        await DiscardAsync(session.State);

        var detail = shot is null ? message : $"{message} A screenshot and page dump were saved next to {shot}.";
        return new BrowserLoginResult(BrowserLoginStatus.Failed, Message: detail);
    }

    /// <summary>
    /// Saves a screenshot and a text dump (URL, title, visible controls) of the
    /// page where the sign-in stalled, so selectors can be adjusted from the
    /// real page rather than guessed.
    /// </summary>
    private async Task<string?> TrySaveDiagnosticsAsync(BrowserSession session)
    {
        try
        {
            var directory = Path.Combine(Path.GetFullPath(options.Value.DataDirectory), "login-failures");
            Directory.CreateDirectory(directory);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var pngPath = Path.Combine(directory, $"{stamp}.png");
            await session.Page.ScreenshotAsync(new PageScreenshotOptions { Path = pngPath, FullPage = true });

            try
            {
                // Never record input values here — they include the password.
                const string script =
                    "() => { const vis = el => !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length); "
                    + "const field = el => ({ tag: el.tagName.toLowerCase(), id: el.id || null, "
                    + "name: el.getAttribute('name'), type: el.getAttribute('type'), hasValue: !!el.value, visible: vis(el) }); "
                    + "const control = el => ({ tag: el.tagName.toLowerCase(), id: el.id || null, type: el.getAttribute('type'), "
                    + "text: (el.innerText || '').trim().slice(0, 40) || null, visible: vis(el) }); "
                    + "return JSON.stringify({ url: location.href, title: document.title, "
                    + "inputs: [...document.querySelectorAll('input')].map(field), "
                    + "controls: [...document.querySelectorAll('button, input[type=button], input[type=submit], a[role=button], [role=button]')].map(control).filter(x => x.visible), "
                    + "errors: [...document.querySelectorAll('.error, [role=alert]')].map(el => (el.innerText || '').trim()).filter(t => t && vis(el)) }, null, 1); }";
                var dump = await session.Page.EvaluateAsync<string>(script);
                // Append the recorded auth-flow responses so we can see what the
                // credential submit actually returned.
                var network = string.Join("\n", session.Network());
                await File.WriteAllTextAsync(Path.Combine(directory, $"{stamp}.json"),
                    $"{dump}\n\n--- auth network ---\n{network}\n");
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Could not capture a sign-in failure page dump");
            }

            return pngPath;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not capture a sign-in failure screenshot");
            return null;
        }
    }

    private async Task DiscardAsync(string state)
    {
        if (sessions.TryRemove(state, out var session))
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>Installs the Chromium build Playwright needs, once, if it is missing.</summary>
    private async Task EnsureBrowsersInstalledAsync(CancellationToken cancellationToken)
    {
        if (browsersInstalled)
        {
            return;
        }

        await installGate.WaitAsync(cancellationToken);
        try
        {
            if (browsersInstalled)
            {
                return;
            }

            // A no-op when the browser is already present; a one-time download otherwise.
            var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(["install", "chromium"]), cancellationToken);
            if (exitCode != 0)
            {
                logger.LogWarning("Playwright chromium install exited with code {ExitCode}", exitCode);
            }

            browsersInstalled = true;
        }
        finally
        {
            installGate.Release();
        }
    }

    /// <summary>
    /// Advances a GM step by invoking its visible submit button in the page. GM's
    /// single-page flow reuses #continue (email → password → log in) and keeps a
    /// hidden #next; a real Playwright click sometimes doesn't fire the handler,
    /// so call the button's own click directly, then fall back to a UI click.
    /// </summary>
    private async Task SubmitStepAsync(IPage page)
    {
        const string script =
            "() => { for (const sel of ['#continue', '#next', 'button[type=submit]', 'input[type=submit]']) { "
            + "for (const el of document.querySelectorAll(sel)) { "
            + "if (el.offsetWidth || el.offsetHeight || el.getClientRects().length) { el.click(); return true; } } } return false; }";

        if (await page.EvaluateAsync<bool>(script))
        {
            return;
        }

        if (!await TryClickAsync(page, SignInButtonSelector, timeoutMs: 10000))
        {
            throw new InvalidOperationException("Could not find GM's submit button.");
        }
    }

    /// <summary>
    /// Clicks the first *visible* element matching the selector. GM's pages carry
    /// hidden duplicates (e.g. a hidden #next and a visible #continue), so the
    /// first DOM match is often the wrong, invisible one.
    /// </summary>
    private static async Task ClickFirstAsync(IPage page, string selector)
    {
        if (!await TryClickAsync(page, selector, timeoutMs: 10000))
        {
            throw new InvalidOperationException($"No visible element matched '{selector}'.");
        }
    }

    private static async Task<bool> TryClickAsync(IPage page, string selector, int timeoutMs = 5000)
    {
        var matches = page.Locator(selector);
        var count = await matches.CountAsync();

        for (var index = 0; index < count; index++)
        {
            var candidate = matches.Nth(index);
            if (!await candidate.IsVisibleAsync())
            {
                continue;
            }

            try
            {
                await candidate.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs });
                return true;
            }
            catch (Exception exception) when (exception is TimeoutException or PlaywrightException)
            {
                // Try the next visible match.
            }
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var state in sessions.Keys.ToArray())
        {
            await DiscardAsync(state);
        }

        playwright?.Dispose();
    }

    private sealed class BrowserSession(string state, IBrowser browser, IBrowserContext context, IPage page, XvfbDisplay? xvfb) : IAsyncDisposable
    {
        public string State { get; } = state;

        public IPage Page { get; } = page;

        public TaskCompletionSource<string> CapturedRedirect { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? MfaType { get; set; }

        public string? CodeInputSelector { get; set; }

        public bool CodeRequested { get; set; }

        private readonly List<string> network = [];

        public void RecordResponse(string entry)
        {
            lock (network)
            {
                network.Add(entry);
                if (network.Count > 20)
                {
                    network.RemoveAt(0);
                }
            }
        }

        public IReadOnlyList<string> Network()
        {
            lock (network)
            {
                return network.ToArray();
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await context.CloseAsync();
                await browser.CloseAsync();
                await browser.DisposeAsync();
            }
            catch
            {
                // Best-effort teardown.
            }
            finally
            {
                xvfb?.Dispose();
            }
        }
    }

    /// <summary>
    /// A virtual X display started for one sign-in, so the full Chromium can run
    /// "headful" (and thus look like a real browser) on a server with no screen.
    /// </summary>
    private sealed class XvfbDisplay : IDisposable
    {
        private readonly System.Diagnostics.Process process;

        private XvfbDisplay(System.Diagnostics.Process process, string display)
        {
            this.process = process;
            Display = display;
        }

        public string Display { get; }

        public static XvfbDisplay? TryStart(ILogger logger)
        {
            if (!IsOnPath("Xvfb"))
            {
                return null;
            }

            // Pick a high display number unlikely to clash with a real session.
            var display = $":{Random.Shared.Next(90, 999)}";
            try
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "Xvfb",
                    ArgumentList = { display, "-screen", "0", "1280x1024x24", "-nolisten", "tcp" },
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                });

                if (process is null)
                {
                    return null;
                }

                // Give the server a moment to come up; bail if it died immediately.
                Thread.Sleep(700);
                if (process.HasExited)
                {
                    logger.LogWarning("Xvfb exited immediately (code {ExitCode}); falling back", process.ExitCode);
                    return null;
                }

                return new XvfbDisplay(process, display);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not start Xvfb; falling back");
                return null;
            }
        }

        private static bool IsOnPath(string executable)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            return path.Split(Path.PathSeparator)
                .Any(directory => !string.IsNullOrEmpty(directory) && File.Exists(Path.Combine(directory, executable)));
        }

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                process.Dispose();
            }
            catch
            {
                // Best-effort teardown.
            }
        }
    }
}
