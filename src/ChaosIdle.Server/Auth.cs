using System.Security.Cryptography;
using System.Text;

namespace ChaosIdle.Server;

/// <summary>
/// Device tokens: 32 random bytes, shown once at account creation; only a SHA-256 hash is
/// stored. A placeholder until platform sign-in (Play Games / Game Center) is added.
/// </summary>
public static class Auth
{
    public const string AccountItem = "account";

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Endpoint filter: resolves the bearer token to an account or answers 401.</summary>
    public static async ValueTask<object?> RequireAccount(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        string header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            return Results.Problem("Missing bearer token.", statusCode: 401);

        var store = http.RequestServices.GetRequiredService<IServerStore>();
        var account = store.FindAccountByTokenHash(Hash(header["Bearer ".Length..].Trim()));
        if (account == null) return Results.Problem("Unknown token.", statusCode: 401);

        http.Items[AccountItem] = account;
        return await next(context);
    }

    public static Account Account(this HttpContext http) => (Account)http.Items[AccountItem]!;
}
