using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenStar.Core.Storage;

/// <summary>
/// Loads and saves <see cref="OpenStarState"/> as a single file encrypted with
/// ASP.NET Core data protection. Callers mutate state through <see cref="UpdateAsync"/>
/// so writes are serialized.
/// </summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector protector;
    private readonly ILogger<StateStore> logger;
    private readonly string statePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private OpenStarState? state;

    public StateStore(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<OpenStarOptions> options,
        ILogger<StateStore> logger)
    {
        protector = dataProtectionProvider.CreateProtector("OpenStar.State.v1");
        this.logger = logger;

        var dataDirectory = Path.GetFullPath(options.Value.DataDirectory);
        Directory.CreateDirectory(dataDirectory);
        if (!OperatingSystem.IsWindows())
        {
            // Tokens and data protection keys live here; keep them owner-only.
            File.SetUnixFileMode(dataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        statePath = Path.Combine(dataDirectory, "state.bin");
    }

    public async Task<OpenStarState> GetAsync(CancellationToken cancellationToken = default)
    {
        if (state is not null)
        {
            return state;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            return state ??= await LoadAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T> UpdateAsync<T>(Func<OpenStarState, T> update, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            state ??= await LoadAsync(cancellationToken);
            var result = update(state);
            await SaveAsync(state, cancellationToken);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task UpdateAsync(Action<OpenStarState> update, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(current =>
        {
            update(current);
            return true;
        }, cancellationToken);
    }

    private async Task<OpenStarState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(statePath))
        {
            var fresh = new OpenStarState();
            await SaveAsync(fresh, cancellationToken);
            return fresh;
        }

        var protectedBytes = await File.ReadAllBytesAsync(statePath, cancellationToken);
        try
        {
            var json = protector.Unprotect(protectedBytes);
            return JsonSerializer.Deserialize<OpenStarState>(json, SerializerOptions) ?? new OpenStarState();
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            // The data protection keys no longer match (e.g. the keys folder was
            // deleted). Keep the unreadable file for inspection and start over.
            var backupPath = $"{statePath}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.unreadable";
            File.Move(statePath, backupPath);
            logger.LogError(exception, "Could not decrypt OpenStar state; moved it to {BackupPath} and starting fresh", backupPath);
            return new OpenStarState();
        }
    }

    private async Task SaveAsync(OpenStarState current, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(current, SerializerOptions);
        var temporaryPath = $"{statePath}.tmp";
        await File.WriteAllBytesAsync(temporaryPath, protector.Protect(json), cancellationToken);
        File.Move(temporaryPath, statePath, overwrite: true);
    }
}
