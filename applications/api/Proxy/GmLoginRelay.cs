using System.Net;

using OpenStar.Core.Authentication;

namespace OpenStar.Api.Proxy;

/// <summary>
/// A transparent reverse proxy to GM's sign-in host (<see cref="GmAuthConstants.LoginHost"/>).
///
/// The browser does the whole GM sign-in through this relay, so GM's final
/// redirect to the myChevrolet app scheme (<c>msauth.com.gm.myChevrolet://…</c>)
/// comes back to OpenStar instead of a dead tab. The relay forwards bytes
/// unchanged in both directions; it does not read or alter the sign-in form, the
/// password, or the page. It only relabels GM's cookies so the browser keeps
/// sending them to the relay, and watches response redirects for the app-scheme
/// URL, which it intercepts to finish linking the account.
/// </summary>
public sealed class GmLoginRelay(
    HttpClient httpClient,
    AccountManager accountManager,
    ILogger<GmLoginRelay> logger)
{
    private const string AppRedirectScheme = "msauth.com.gm.mychevrolet://";
    private const string DonePath = "/_openstar/done";

    private static readonly string GmOrigin = $"https://{GmAuthConstants.LoginHost}";

    // Headers managed per-hop or by the transport, not copied between the two connections.
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host",
    };

    /// <summary>Replaces the origin of a GM URL with the relay's, keeping it relative.</summary>
    private static string RewriteToGm(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            ? GmOrigin + absolute.PathAndQuery
            : url;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == DonePath)
        {
            await WriteDonePageAsync(context);
            return;
        }

        var cancellationToken = context.RequestAborted;
        using var upstreamRequest = BuildUpstreamRequest(context.Request);

        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "GM login relay could not reach {Host}", GmAuthConstants.LoginHost);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("Could not reach GM sign-in. Check this host's internet connection and try again.", cancellationToken);
            return;
        }

        using (upstreamResponse)
        {
            var location = upstreamResponse.Headers.Location?.ToString();
            if (location is not null && location.StartsWith(AppRedirectScheme, StringComparison.OrdinalIgnoreCase))
            {
                await CompleteLoginAsync(context, location, cancellationToken);
                return;
            }

            await CopyResponseAsync(context, upstreamResponse, cancellationToken);
        }
    }

    private static HttpRequestMessage BuildUpstreamRequest(HttpRequest request)
    {
        var targetUri = new UriBuilder
        {
            Scheme = Uri.UriSchemeHttps,
            Host = GmAuthConstants.LoginHost,
            Path = request.Path.Value ?? "/",
            Query = request.QueryString.Value ?? string.Empty,
        }.Uri;

        var message = new HttpRequestMessage(new HttpMethod(request.Method), targetUri);

        // Methods without a body (GET/HEAD) carry no content.
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            message.Content = new StreamContent(request.Body);
        }

        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            // GM's bot protection rejects sensitive POSTs whose Origin/Referer
            // point at the relay host instead of GM's, so present GM's own origin.
            var values = header.Key switch
            {
                _ when string.Equals(header.Key, "Origin", StringComparison.OrdinalIgnoreCase) =>
                    [GmOrigin],
                _ when string.Equals(header.Key, "Referer", StringComparison.OrdinalIgnoreCase) =>
                    [RewriteToGm(header.Value.ToString())],
                _ => (IEnumerable<string>)header.Value,
            };

            if (!message.Headers.TryAddWithoutValidation(header.Key, values))
            {
                message.Content?.Headers.TryAddWithoutValidation(header.Key, values);
            }
        }

        message.Headers.Host = GmAuthConstants.LoginHost;
        return message;
    }

    private async Task CopyResponseAsync(HttpContext context, HttpResponseMessage upstream, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)upstream.StatusCode;

        foreach (var header in upstream.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) || string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Keep GM-host redirects on the relay so cookies stay first-party.
            if (string.Equals(header.Key, "Location", StringComparison.OrdinalIgnoreCase)
                && upstream.Headers.Location is { IsAbsoluteUri: true } location
                && string.Equals(location.Host, GmAuthConstants.LoginHost, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.Location = location.PathAndQuery;
                continue;
            }

            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in upstream.Content.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
            {
                continue;
            }

            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        // Rewrite cookies so the browser keeps sending them to the relay host
        // over plain HTTP: GM scopes them to its own domain and marks them Secure.
        if (upstream.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            context.Response.Headers.SetCookie = cookies.Select(RelabelCookie).ToArray();
        }

        // Let Kestrel frame the body; the upstream length may not match after header changes.
        context.Response.Headers.Remove("Content-Length");

        await using var upstreamBody = await upstream.Content.ReadAsStreamAsync(cancellationToken);
        await upstreamBody.CopyToAsync(context.Response.Body, cancellationToken);
    }

    /// <summary>
    /// Drops the <c>Domain</c> and <c>Secure</c> attributes and relaxes
    /// <c>SameSite</c> so the cookie belongs to the relay host and survives a
    /// plain-HTTP, same-site sign-in.
    /// </summary>
    internal static string RelabelCookie(string setCookie)
    {
        var parts = setCookie.Split(';');
        var kept = new List<string> { parts[0] };

        foreach (var attribute in parts.Skip(1))
        {
            var name = attribute.Trim().Split('=', 2)[0].Trim();
            if (name.Equals("Domain", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Secure", StringComparison.OrdinalIgnoreCase)
                || name.Equals("SameSite", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kept.Add(attribute.Trim());
        }

        kept.Add("SameSite=Lax");
        return string.Join("; ", kept);
    }

    private async Task CompleteLoginAsync(HttpContext context, string appRedirect, CancellationToken cancellationToken)
    {
        var (_, state, _) = AccountManager.ParseRedirect(appRedirect);
        if (state is null)
        {
            await WriteDonePageAsync(context, "This sign-in could not be matched to a pending request. Start setup again.");
            return;
        }

        try
        {
            await accountManager.CompleteLoginAsync(state, appRedirect, cancellationToken);
            // Redirect to the relay's own done page so no GM-scoped URL is exposed.
            context.Response.Redirect(DonePath);
        }
        catch (OpenStarAuthException exception)
        {
            logger.LogWarning(exception, "GM login relay failed to complete sign-in");
            await WriteDonePageAsync(context, exception.Message);
        }
    }

    private static Task WriteDonePageAsync(HttpContext context, string? error = null)
    {
        var heading = error is null ? "Account linked" : "Sign-in not completed";
        var body = error is null
            ? "Your GM account is now linked to OpenStar. You can close this tab and return to the setup page."
            : WebUtility.HtmlEncode(error);

        context.Response.StatusCode = error is null ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync(
            $$"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>OpenStar</title>
            <style>body{font-family:system-ui,sans-serif;background:#111;color:#eee;display:grid;place-items:center;min-height:100vh;margin:0}
            main{max-width:28rem;padding:2rem;text-align:center}h1{font-size:1.4rem}p{color:#aaa;line-height:1.5}</style></head>
            <body><main><h1>{{heading}}</h1><p>{{body}}</p></main></body>
            </html>
            """,
            context.RequestAborted);
    }
}
