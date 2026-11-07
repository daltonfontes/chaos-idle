using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace ChaosIdle.Server;

/// <summary>
/// What the server needs to run on the internet behind a reverse proxy (deploy/: Caddy terminates
/// HTTPS and forwards to this server on a private network):
/// <list type="bullet">
/// <item>forwarded headers, so the client's real IP and "https" are seen (only when the server is
/// reachable from the proxy alone: <c>ForwardedHeaders__Enabled=true</c>);</item>
/// <item>rate limits: per IP for creating accounts and signing in, per account for everything else.</item>
/// </list>
/// </summary>
public static class Hosting
{
    public const string AnonymousPolicy = "anonymous";
    public const string AccountPolicy = "account";

    public static void AddHosting(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // The proxy's address changes between container restarts; the server must then only be
            // reachable through the proxy (no published port), which deploy/docker-compose.yml ensures.
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
            o.ForwardLimit = 1;
        });

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };

            // Creating guest accounts and signing in: per client IP.
            o.AddPolicy(AnonymousPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + ClientIp(http), _ => Window(Limit(http, "RateLimits:AnonymousPerMinute", 20))));

            // Everything signed in: per device token (the hash), so one busy phone can't starve others.
            o.AddPolicy(AccountPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "token:" + BearerKey(http), _ => Window(Limit(http, "RateLimits:AccountPerMinute", 120))));
        });
    }

    public static void UseHosting(this WebApplication app)
    {
        if (app.Configuration.GetValue("ForwardedHeaders:Enabled", false)) app.UseForwardedHeaders();
        app.UseRateLimiter();
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static int Limit(HttpContext http, string key, int fallback) =>
        Math.Max(1, http.RequestServices.GetRequiredService<IConfiguration>().GetValue(key, fallback));

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();

    private static string BearerKey(HttpContext http)
    {
        string header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? Auth.Hash(header["Bearer ".Length..].Trim()) : "none:" + ClientIp(http);
    }
}
