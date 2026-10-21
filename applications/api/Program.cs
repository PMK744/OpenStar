using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;

using OpenStar.Api.Authentication;
using OpenStar.Core;
using OpenStar.Core.Authentication;
using OpenStar.Core.OnStar;
using OpenStar.Core.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddOpenStarCore(builder.Configuration);
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Client, policy => policy.RequireRole(nameof(ApiKeyRole.Client), nameof(ApiKeyRole.Admin)))
    .AddPolicy(Policies.Admin, policy => policy.RequireRole(nameof(ApiKeyRole.Admin)));

// Keys travel in headers, never cookies, so any origin on the network may call the API.
builder.Services.AddCors(options =>
{
    options.AddPolicy("NetworkClients", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Location");
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    ExceptionHandler = WriteProblemAsync,
    // GM and account errors are expected outcomes reported to the caller, not crashes.
    SuppressDiagnosticsCallback = context => context.Exception is OpenStarAuthException or OnStarApiException,
});
app.UseCors("NetworkClients");
app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.Services.GetRequiredService<ApiKeyManager>().EnsureAdminKeyAsync();

app.Run();

static async Task WriteProblemAsync(HttpContext context)
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

    var (status, title, detail) = exception switch
    {
        // Bad login input is the caller's fault; elsewhere it means the account needs attention.
        OpenStarAuthException auth when context.Request.Path.StartsWithSegments("/api/setup") =>
            (StatusCodes.Status400BadRequest, "GM sign-in failed", auth.Message),
        OpenStarAuthException auth =>
            (StatusCodes.Status409Conflict, "GM account needs attention", auth.Message),
        // Our own validation errors carry a status but no upstream body.
        OnStarApiException { ResponseBody: null, StatusCode: { } own } api =>
            (own, "Request rejected", api.Message),
        OnStarApiException { StatusCode: StatusCodes.Status429TooManyRequests } api =>
            (StatusCodes.Status429TooManyRequests, "GM rate limit", api.Message),
        OnStarApiException api =>
            (StatusCodes.Status502BadGateway, "GM API error", $"{api.Message} {Truncate(api.ResponseBody)}".Trim()),
        HttpRequestException or TaskCanceledException =>
            (StatusCodes.Status504GatewayTimeout, "GM unreachable", exception.Message),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "See the OpenStar API log for details."),
    };

    context.Response.StatusCode = status;
    await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails = { Status = status, Title = title, Detail = detail },
    });

    static string? Truncate(string? value) => value is { Length: > 500 } ? value[..500] : value;
}
