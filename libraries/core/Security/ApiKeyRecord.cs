namespace OpenStar.Core.Security;

public sealed class ApiKeyRecord
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    /// <summary>
    /// SHA-256 of the key. The plaintext key is only shown once at creation.
    /// </summary>
    public required string Hash { get; init; }

    /// <summary>
    /// First characters of the key so users can tell keys apart.
    /// </summary>
    public required string Prefix { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; set; }
}

public sealed record ApiKeySummary(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt)
{
    public static ApiKeySummary From(ApiKeyRecord record) =>
        new(record.Id, record.Name, record.Prefix, record.CreatedAt, record.LastUsedAt);
}

public sealed record CreatedApiKey(ApiKeySummary Key, string Secret);

public enum ApiKeyRole
{
    Client,
    Admin,
}
