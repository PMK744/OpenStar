using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenStar.Core.Authentication;

namespace OpenStar.Core.Hosting;

/// <summary>
/// Periodically redeems the Microsoft refresh token so its sliding lifetime
/// keeps moving forward while OpenStar runs, avoiding another interactive login.
/// </summary>
public sealed class TokenKeepAliveService(
    AccountManager accountManager,
    IOptions<OpenStarOptions> options,
    ILogger<TokenKeepAliveService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, options.Value.TokenKeepAliveHours)));

        try
        {
            do
            {
                var status = await accountManager.GetStatusAsync(stoppingToken);
                if (!status.IsLinked)
                {
                    continue;
                }

                try
                {
                    await accountManager.RefreshAsync(forceMicrosoftRefresh: true, stoppingToken);
                    logger.LogInformation("Refreshed GM tokens for {Email}", status.Email);
                }
                catch (Exception exception) when (exception is OpenStarAuthException or HttpRequestException)
                {
                    logger.LogWarning(exception, "Keep-alive token refresh failed; the account may need to sign in again");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
