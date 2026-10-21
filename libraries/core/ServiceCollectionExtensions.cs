using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OpenStar.Core.Authentication;
using OpenStar.Core.Hosting;
using OpenStar.Core.OnStar;
using OpenStar.Core.Security;
using OpenStar.Core.Storage;

namespace OpenStar.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenStarCore(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(OpenStarOptions.SectionName);
        services.Configure<OpenStarOptions>(section);

        var dataDirectory = Path.GetFullPath(section.Get<OpenStarOptions>()?.DataDirectory ?? new OpenStarOptions().DataDirectory);
        services.AddDataProtection()
            .SetApplicationName("OpenStar")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));

        services.AddSingleton<StateStore>();
        services.AddSingleton<ApiKeyManager>();
        services.AddSingleton<AccountManager>();
        services.AddSingleton<CommandTracker>();

        // The auth manager and command tracker are singletons, so their HTTP
        // clients are too; a pooled connection lifetime keeps DNS changes honored.
        services.AddSingleton(provider => ActivatorUtilities.CreateInstance<GmAuthClient>(provider, CreateHttpClient(TimeSpan.FromSeconds(30))));
        services.AddSingleton(provider => ActivatorUtilities.CreateInstance<OnStarClient>(provider, CreateHttpClient(TimeSpan.FromSeconds(60))));

        services.AddHostedService<TokenKeepAliveService>();

        return services;
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };

        return new HttpClient(handler) { Timeout = timeout };
    }
}
