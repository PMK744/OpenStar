using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenStar.Core.Storage;

namespace OpenStar.Core.Security;

/// <summary>
/// Issues and validates API keys. Client keys call vehicle routes; the single
/// admin key additionally manages the linked account and client keys.
/// </summary>
public sealed class ApiKeyManager(StateStore store, IOptions<OpenStarOptions> options, ILogger<ApiKeyManager> logger)
{
    private const string ClientPrefix = "osk_";
    private const string AdminPrefix = "osa_";

    /// <summary>
    /// Creates the admin key on first run and logs it once, Jupyter-style.
    /// </summary>
    public async Task EnsureAdminKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.AdminKey))
        {
            return;
        }

        var state = await store.GetAsync(cancellationToken);
        if (state.AdminKeyHash is not null)
        {
            return;
        }

        var adminKey = await ResetAdminKeyAsync(cancellationToken);
        logger.LogWarning(
            "Generated OpenStar admin key (shown once, enter it in the setup UI): {AdminKey}",
            adminKey);
    }

    public async Task<string> ResetAdminKeyAsync(CancellationToken cancellationToken = default)
    {
        var adminKey = GenerateSecret(AdminPrefix);
        await store.UpdateAsync(state => state.AdminKeyHash = Hash(adminKey), cancellationToken);
        return adminKey;
    }

    public async Task<ApiKeyRole?> ValidateAsync(string presentedKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedKey))
        {
            return null;
        }

        var configuredAdminKey = options.Value.AdminKey;
        if (!string.IsNullOrWhiteSpace(configuredAdminKey) && FixedTimeEquals(presentedKey, configuredAdminKey))
        {
            return ApiKeyRole.Admin;
        }

        var hash = Hash(presentedKey);
        var state = await store.GetAsync(cancellationToken);

        if (state.AdminKeyHash is not null && FixedTimeEquals(hash, state.AdminKeyHash))
        {
            return ApiKeyRole.Admin;
        }

        var record = state.ApiKeys.FirstOrDefault(key => FixedTimeEquals(hash, key.Hash));
        if (record is null)
        {
            return null;
        }

        // Only persist usage at most once a minute to avoid a disk write per request.
        var now = DateTimeOffset.UtcNow;
        if (record.LastUsedAt is null || now - record.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            await store.UpdateAsync(_ => record.LastUsedAt = now, cancellationToken);
        }

        return ApiKeyRole.Client;
    }

    public async Task<IReadOnlyList<ApiKeySummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var state = await store.GetAsync(cancellationToken);
        return state.ApiKeys.Select(ApiKeySummary.From).ToList();
    }

    public async Task<CreatedApiKey> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var secret = GenerateSecret(ClientPrefix);
        var record = new ApiKeyRecord
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed key" : name.Trim(),
            Hash = Hash(secret),
            Prefix = secret[..(ClientPrefix.Length + 6)],
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await store.UpdateAsync(state => state.ApiKeys.Add(record), cancellationToken);
        return new CreatedApiKey(ApiKeySummary.From(record), secret);
    }

    public Task<bool> RevokeAsync(string id, CancellationToken cancellationToken = default)
    {
        return store.UpdateAsync(state => state.ApiKeys.RemoveAll(key => key.Id == id) > 0, cancellationToken);
    }

    private static string GenerateSecret(string prefix)
    {
        return prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
    }

    private static string Hash(string value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }
}
