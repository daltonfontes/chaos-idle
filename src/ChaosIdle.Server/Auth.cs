using System.Security.Cryptography;
using System.Text;

namespace ChaosIdle.Server;

/// <summary>
/// Device tokens: 32 random bytes, shown once when issued (guest account or sign-in); only a
/// SHA-256 hash is stored. Each device has its own token; see <see cref="AccountService"/>.
/// </summary>
public static class Auth
{
    public const string AccountItem = "account";
    public const string TokenHashItem = "tokenHash";

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
        string hash = Hash(header["Bearer ".Length..].Trim());
        var account = store.FindAccountByTokenHash(hash);
        if (account == null) return Results.Problem("Unknown or signed-out token.", statusCode: 401);

        http.Items[AccountItem] = account;
        http.Items[TokenHashItem] = hash;
        return await next(context);
    }

    public static Account Account(this HttpContext http) => (Account)http.Items[AccountItem]!;

    public static string TokenHash(this HttpContext http) => (string)http.Items[TokenHashItem]!;
}
