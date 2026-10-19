using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenStar.Core.OnStar;

public sealed class CommandRecord
{
    public required string Id { get; init; }

    public required string Vin { get; init; }

    public required VehicleCommand Command { get; init; }

    public CommandStatus Status { get; set; } = CommandStatus.InProgress;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Error { get; set; }

    /// <summary>Latest raw GM response for the command.</summary>
    public JsonElement? Response { get; set; }

    internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Submits vehicle commands and polls GM in the background until each
/// finishes, so API clients can either wait or check back by id.
/// </summary>
public sealed class CommandTracker(
    OnStarClient client,
    IOptions<OpenStarOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<CommandTracker> logger)
{
    private const int MaxRecords = 200;

    private readonly ConcurrentDictionary<string, CommandRecord> records = new();

    public async Task<CommandRecord> StartAsync(string vin, VehicleCommand command, CommandOptions commandOptions, CancellationToken cancellationToken)
    {
        await client.EnsureVinAuthorizedAsync(vin, cancellationToken);
        var progress = await client.SubmitCommandAsync(vin, command, commandOptions, cancellationToken);

        var record = new CommandRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Vin = vin.ToUpperInvariant(),
            Command = command,
            Response = progress.Response,
        };
        records[record.Id] = record;
        Trim();

        Apply(record, progress);
        if (record.Status == CommandStatus.InProgress)
        {
            _ = Task.Run(() => PollAsync(record, progress.PollUrl!), CancellationToken.None);
        }

        return record;
    }

    public CommandRecord? Get(string id) => records.GetValueOrDefault(id);

    public IReadOnlyList<CommandRecord> List() => records.Values.OrderByDescending(record => record.CreatedAt).ToList();

    public async Task WaitAsync(CommandRecord record, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await record.Completion.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            // The caller gets the in-progress record and can keep polling by id.
        }
    }

    private async Task PollAsync(CommandRecord record, string pollUrl)
    {
        var interval = TimeSpan.FromSeconds(options.Value.CommandPollingIntervalSeconds);
        var deadline = record.CreatedAt.AddSeconds(options.Value.CommandPollingTimeoutSeconds);
        var stopping = lifetime.ApplicationStopping;

        try
        {
            while (record.Status == CommandStatus.InProgress)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    Finish(record, CommandStatus.Timeout, "GM did not report completion before the polling timeout.");
                    return;
                }

                await Task.Delay(interval, stopping);
                var progress = await client.PollCommandAsync(pollUrl, stopping);
                pollUrl = progress.PollUrl ?? pollUrl;
                Apply(record, progress);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            Finish(record, CommandStatus.Timeout, "OpenStar shut down while the command was in progress.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Polling {Command} for {Vin} failed", record.Command, record.Vin);
            Finish(record, CommandStatus.Failure, exception.Message);
        }
    }

    private static void Apply(CommandRecord record, CommandProgress progress)
    {
        record.Response = progress.Response;
        if (progress.Status != CommandStatus.InProgress)
        {
            Finish(record, progress.Status, progress.Status == CommandStatus.Failure ? "GM reported the command failed." : null);
        }
        else if (progress.PollUrl is null)
        {
            Finish(record, CommandStatus.Failure, "GM accepted the command but returned no status URL.");
        }
    }

    private static void Finish(CommandRecord record, CommandStatus status, string? error)
    {
        record.Status = status;
        record.Error = error;
        record.CompletedAt = DateTimeOffset.UtcNow;
        record.Completion.TrySetResult();
    }

    private void Trim()
    {
        if (records.Count <= MaxRecords)
        {
            return;
        }

        foreach (var stale in records.Values.OrderBy(record => record.CreatedAt).Take(records.Count - MaxRecords))
        {
            if (stale.Status != CommandStatus.InProgress)
            {
                records.TryRemove(stale.Id, out _);
            }
        }
    }
}
