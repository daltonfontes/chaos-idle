using System.Security.Claims;
using System.Security.Cryptography;
using ChaosIdle.Server;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ChaosIdle.Tests;

/// <summary>
/// Stands in for Google in tests: real RS256 ID tokens signed with a key made here, checked by
/// the real <see cref="OidcIdentityVerifier"/> (with the key given instead of fetched).
/// </summary>
public static class TestIdentityProvider
{
    public const string ClientId = "test-client.apps.googleusercontent.com";
    public const string Issuer = "https://accounts.google.com";

    public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };
    public static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "test-key" }; // same kid, wrong key

    public static OidcIdentityVerifier Verifier(TimeProvider time)
    {
        var google = SignInProviderOptions.Google();
        google.ClientIds.Add(ClientId);
        google.SigningKeys = new List<SecurityKey> { Key };
        var apple = SignInProviderOptions.Apple(); // no client id: off
        return new OidcIdentityVerifier(new Dictionary<string, SignInProviderOptions> { ["google"] = google, ["apple"] = apple }, time);
    }

    public static string Token(DateTimeOffset now, string subject, string? email = null, bool emailVerified = true,
        string audience = ClientId, string issuer = Issuer, TimeSpan? expiresIn = null, SecurityKey? key = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject };
        if (email != null)
        {
            claims["email"] = email;
            claims["email_verified"] = emailVerified;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + (expiresIn ?? TimeSpan.FromHours(1))).UtcDateTime,
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
