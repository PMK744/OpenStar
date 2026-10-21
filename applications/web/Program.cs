using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Backend OpenStar API the browser reaches through this app (see the proxy below).
var configuredApi = builder.Configuration["OpenStar:ApiBaseUrl"];
var apiBaseUrl = (string.IsNullOrWhiteSpace(configuredApi) ? "http://localhost:5290" : configuredApi).TrimEnd('/');
builder.Services.AddHttpClient("api", client => client.Timeout = TimeSpan.FromSeconds(120));

// "Managed auth": when the web app knows the shared admin key (e.g. injected by
// the all-in-one container), the proxy adds it to UI requests that carry no key,
// so the setup UI works with no key entry. Anyone who can reach this app is then
// an admin — keep it on a trusted network.
var managedAdminKey = builder.Configuration["OpenStar:AdminKey"];

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// Proxy API calls through this app so the browser only ever talks to one origin
// and port; the backend API port does not need to be reachable from the browser.
app.Map("/api/{**path}", ProxyToApiAsync);
app.Map("/openapi/{**path}", ProxyToApiAsync);

// Serve the login-helper browser extension as a zip the user can download and
// load unpacked, so they don't need the repository checked out. The source
// folder is found whether the app runs from its project or its build output.
var helperDirectory = ResolveHelperDirectory(builder.Configuration, app.Environment);

app.MapGet("/login-helper.zip", () =>
{
    if (!Directory.Exists(helperDirectory))
    {
        return Results.NotFound("The login helper build was not found. Run `bun run build` in tools/login-helper.");
    }

    using var buffer = new MemoryStream();
    using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var file in Directory.EnumerateFiles(helperDirectory, "*", SearchOption.AllDirectories))
        {
            var entryName = Path.GetRelativePath(helperDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, entryName);
        }
    }

    return Results.File(buffer.ToArray(), "application/zip", "openstar-login-helper.zip");
});

static string ResolveHelperDirectory(IConfiguration configuration, IWebHostEnvironment environment)
{
    var configured = configuration["OpenStar:LoginHelperPath"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.GetFullPath(configured, environment.ContentRootPath);
    }

    // Walk up from the content root and the build output to find the extension's
    // transpiled output (tools/login-helper/dist).
    foreach (var start in new[] { environment.ContentRootPath, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "login-helper", "dist");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    // Fall back to the usual repo layout; the endpoint reports if it's missing.
    return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "tools", "login-helper", "dist"));
}

app.UseRouting();

app.UseAuthorization();

app.UseStaticFiles();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();
return;

async Task ProxyToApiAsync(HttpContext context)
{
    var target = $"{apiBaseUrl}{context.Request.Path}{context.Request.QueryString}";
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);

    if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        request.Content = new StreamContent(context.Request.Body);
        if (context.Request.ContentType is { } contentType)
        {
            request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }
    }

    // Forward the API key and Accept; the rest the backend doesn't need.
    foreach (var name in (string[])["X-Api-Key", "Authorization", "Accept"])
    {
        if (context.Request.Headers.TryGetValue(name, out var value))
        {
            request.Headers.TryAddWithoutValidation(name, (IEnumerable<string>)value!);
        }
    }

    // Inject the shared admin key when the caller sent none (managed auth).
    if (!string.IsNullOrEmpty(managedAdminKey))
    {
        var hasKey = context.Request.Headers.TryGetValue("X-Api-Key", out var presented) && !string.IsNullOrWhiteSpace(presented);
        var hasAuth = context.Request.Headers.TryGetValue("Authorization", out var auth) && !string.IsNullOrWhiteSpace(auth);
        if (!hasKey && !hasAuth)
        {
            request.Headers.Remove("X-Api-Key");
            request.Headers.TryAddWithoutValidation("X-Api-Key", managedAdminKey);
        }
    }

    var client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("api");

    try
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        if (response.Content.Headers.ContentType is { } responseType)
        {
            context.Response.ContentType = responseType.ToString();
        }

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(new
        {
            title = "OpenStar API unreachable",
            detail = $"The web app could not reach the OpenStar API at {apiBaseUrl}. Is it running?",
        });
    }
}
