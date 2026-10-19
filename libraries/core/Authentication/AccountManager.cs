using System.Collections.Concurrent;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

using OpenStar.Core.Storage;

namespace OpenStar.Core.Authentication;

/// <summary>
/// Owns the linked GM account: starts and completes the browser login, and
/// hands out a valid GM API token, refreshing the chain when it expires.
/// </summary>
public sealed class AccountManager(GmAuthClient authClient, StateStore store, ILogger<AccountManager> logger)
{
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PendingLogin> pendingLogins = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    public async Task<LoginStart> StartLoginAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (state, login) in pendingLogins)
        {
            if (login.ExpiresAt < DateTimeOffset.UtcNow)
            {
                pendingLogins.TryRemove(state, out _);
            }
        }

        var pending = await authClient.CreateLoginAsync(cancellationToken);
        pendingLogins[pending.State] = pending;
        return new LoginStart(pending.State, pending.AuthorizationUrl, pending.ExpiresAt);
    }

    /// <summary>
    /// Completes a login from what the user copied after signing in: the full
    /// <c>msauth.com.gm.myChevrolet://auth?code=…</c> redirect, its query string,
    /// or the bare code.
    /// </summary>
    public async Task<AccountStatus> CompleteLoginAsync(string state, string redirect, CancellationToken cancellationToken = default)
    {
        var (code, returnedState, error) = ParseRedirect(redirect);

        if (error is not null)
        {
            throw new OpenStarAuthException($"GM returned an error: {error}");
        }

        if (code is null)
        {
            throw new OpenStarAuthException("No authorization code found. Paste the full msauth.com.gm.myChevrolet://auth?code=… URL.");
        }

        if (returnedState is not null && returnedState != state)
        {
            throw new OpenStarAuthException("This redirect belongs to a different login attempt. Start the login again.");
        }

        if (!pendingLogins.TryGetValue(state, out var pending) || pending.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new OpenStarAuthException("This login attempt expired. Start the login again.");
        }

        pendingLogins.TryRemove(state, out _);
        return await LinkAsync(pending, code, cancellationToken);
    }

    /// <summary>
    /// Redeems an authorization code from <paramref name="login"/> and saves the
    /// resulting tokens as the linked account.
    /// </summary>
    internal async Task<AccountStatus> LinkAsync(PendingLogin login, string code, CancellationToken cancellationToken)
    {
        var microsoftTokens = await authClient.RedeemCodeAsync(login, code, cancellationToken);

        var current = await store.GetAsync(cancellationToken);
        var gmTokens = await authClient.ExchangeForGmTokenAsync(microsoftTokens, current.DeviceId, cancellationToken);

        var idToken = Jwt.ReadPayload(microsoftTokens.IdToken) ?? Jwt.ReadPayload(microsoftTokens.AccessToken);
        await store.UpdateAsync(saved =>
        {
            saved.Microsoft = microsoftTokens;
            saved.Gm = gmTokens;
            saved.Email = Jwt.ReadString(idToken, "email") ?? Jwt.ReadString(idToken, "name");
            saved.LinkedAt = DateTimeOffset.UtcNow;
            saved.LastAuthError = null;
        }, cancellationToken);

        logger.LogInformation("Linked GM account {Email} with {VehicleCount} vehicle(s)", current.Email, gmTokens.Vins.Count);
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<AccountStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await store.GetAsync(cancellationToken);
        return new AccountStatus(
            IsLinked: state.Microsoft is not null,
            Email: state.Email,
            AccountNumber: state.Gm?.AccountNumber,
            Vins: state.Gm?.Vins ?? [],
            LinkedAt: state.LinkedAt,
            RefreshTokenExpiresAt: state.Microsoft?.RefreshTokenExpiresAt,
            LastError: state.LastAuthError);
    }

    public Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        return store.UpdateAsync(state =>
        {
            state.Microsoft = null;
            state.Gm = null;
            state.Email = null;
            state.LinkedAt = null;
            state.LastAuthError = null;
        }, cancellationToken);
    }

    public async Task<GmTokenSet> GetGmTokenAsync(CancellationToken cancellationToken = default)
    {
        var state = await store.GetAsync(cancellationToken);
        if (state.Gm is { } cached && cached.ExpiresAt - ExpirySkew > DateTimeOffset.UtcNow)
        {
            return cached;
        }

        return await RefreshAsync(forceMicrosoftRefresh: false, cancellationToken);
    }

    /// <summary>
    /// Re-runs the token chain. With <paramref name="forceMicrosoftRefresh"/> the
    /// Microsoft refresh token is redeemed even if the access token is still
    /// valid, which slides the refresh token's lifetime forward.
    /// </summary>
    public async Task<GmTokenSet> RefreshAsync(bool forceMicrosoftRefresh, CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.GetAsync(cancellationToken);
            var microsoftTokens = state.Microsoft
                ?? throw new OpenStarAuthException("No GM account is linked. Complete setup in the OpenStar web UI.");

            // Another caller may have refreshed while this one waited.
            if (!forceMicrosoftRefresh && state.Gm is { } cached && cached.ExpiresAt - ExpirySkew > DateTimeOffset.UtcNow)
            {
                return cached;
            }

            try
            {
                if (forceMicrosoftRefresh || microsoftTokens.ExpiresAt - ExpirySkew <= DateTimeOffset.UtcNow)
                {
                    microsoftTokens = await authClient.RefreshAsync(microsoftTokens, cancellationToken);
                }

                var gmTokens = await authClient.ExchangeForGmTokenAsync(microsoftTokens, state.DeviceId, cancellationToken);
                await store.UpdateAsync(saved =>
                {
                    saved.Microsoft = microsoftTokens;
                    saved.Gm = gmTokens;
                    saved.LastAuthError = null;
                }, cancellationToken);

                return gmTokens;
            }
            catch (OpenStarAuthException exception)
            {
                await store.UpdateAsync(saved => saved.LastAuthError = exception.Message, cancellationToken);
                throw;
            }
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public static (string? Code, string? State, string? Error) ParseRedirect(string redirect)
    {
        var value = redirect.Trim().Trim('"', '\'');

        // Chrome's console reports "Failed to launch 'msauth…' because …"; pull the URL out.
        var schemeIndex = value.IndexOf("msauth.", StringComparison.OrdinalIgnoreCase);
        if (schemeIndex > 0)
        {
            value = value[schemeIndex..].Split('\'', '"', ' ')[0];
        }

        var queryIndex = value.IndexOf('?');
        if (queryIndex < 0 && !value.Contains('='))
        {
            return (value.Length > 0 ? value : null, null, null);
        }

        var query = QueryHelpers.ParseQuery(queryIndex >= 0 ? value[queryIndex..] : "?" + value);
        string? Read(string name) => query.TryGetValue(name, out var values) ? values.ToString() : null;

        var error = Read("error");
        if (error is not null && Read("error_description") is { } description)
        {
            error = $"{error}: {description}";
        }

        return (Read("code"), Read("state"), error);
    }
}
