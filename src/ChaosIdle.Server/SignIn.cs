using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ChaosIdle.Server;

/// <summary>
/// One OpenID Connect provider whose ID tokens the game accepts. The app signs in with the
/// platform's own SDK (Google Credential Manager on Android, Sign in with Apple on iOS), gets an
/// ID token, and sends it to the server, which checks it here: signature (the provider's public
/// keys), issuer, audience (our client ids), and expiry.
/// </summary>
public sealed class SignInProviderOptions
{
    /// <summary>Our OAuth client ids at the provider. The provider is off while this is empty.</summary>
    public List<string> ClientIds { get; set; } = new();

    public List<string> Issuers { get; set; } = new();

    /// <summary>The provider's discovery document; its signing keys are fetched and cached from here.</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>For tests: fixed signing keys instead of fetching them.</summary>
    public IList<SecurityKey>? SigningKeys { get; set; }

    public bool Enabled => ClientIds.Count > 0;

    public static SignInProviderOptions Google() => new()
    {
        Issuers = { "https://accounts.google.com", "accounts.google.com" },
        MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration",
    };

    public static SignInProviderOptions Apple() => new()
    {
        Issuers = { "https://appleid.apple.com" },
        MetadataAddress = "https://appleid.apple.com/.well-known/openid-configuration",
    };
}

/// <summary>Who the provider says signed in. <see cref="Subject"/> is stable for that user and our app.</summary>
public sealed record VerifiedIdentity(string Provider, string Subject, string? Email);

/// <summary>The provider's signing keys could not be fetched; the token was not judged either way.</summary>
public sealed class SignInUnavailableException(string provider, Exception inner)
    : Exception($"Could not fetch the signing keys for '{provider}'.", inner);

public interface IIdentityVerifier
{
    bool IsEnabled(string provider);

    /// <returns>The identity, or null when the token is not valid for us (bad signature, wrong audience, expired...).</returns>
    /// <exception cref="SignInUnavailableException">The provider's keys could not be fetched.</exception>
    Task<VerifiedIdentity?> VerifyAsync(string provider, string idToken, CancellationToken cancel = default);
}

public sealed class OidcIdentityVerifier : IIdentityVerifier
{
    private readonly IReadOnlyDictionary<string, SignInProviderOptions> _providers;
    private readonly Dictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _metadata = new();
    private readonly TimeProvider _time;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    public OidcIdentityVerifier(IReadOnlyDictionary<string, SignInProviderOptions> providers, TimeProvider time)
    {
        _providers = providers;
        _time = time;
        foreach (var (name, p) in providers)
        {
            if (p.Enabled && p.SigningKeys == null && p.MetadataAddress != null)
                _metadata[name] = new ConfigurationManager<OpenIdConnectConfiguration>(
                    p.MetadataAddress, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = true });
        }
    }

    /// <summary>Reads Auth:Google and Auth:Apple from configuration (e.g. Auth__Google__ClientIds__0).</summary>
    public static OidcIdentityVerifier FromConfiguration(IConfiguration config, TimeProvider time)
    {
        var google = SignInProviderOptions.Google();
        config.GetSection("Auth:Google").Bind(google);
        var apple = SignInProviderOptions.Apple();
        config.GetSection("Auth:Apple").Bind(apple);
        return new OidcIdentityVerifier(new Dictionary<string, SignInProviderOptions> { ["google"] = google, ["apple"] = apple }, time);
    }

    public bool IsEnabled(string provider) => _providers.TryGetValue(provider, out var p) && p.Enabled;

    public async Task<VerifiedIdentity?> VerifyAsync(string provider, string idToken, CancellationToken cancel = default)
    {
        if (!_providers.TryGetValue(provider, out var p) || !p.Enabled || string.IsNullOrWhiteSpace(idToken)) return null;

        IEnumerable<SecurityKey> keys;
        try
        {
            keys = p.SigningKeys ?? (await _metadata[provider].GetConfigurationAsync(cancel)).SigningKeys;
        }
        catch (Exception fetchError) when (fetchError is not OperationCanceledException)
        {
            throw new SignInUnavailableException(provider, fetchError);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var parameters = new TokenValidationParameters
        {
            ValidIssuers = p.Issuers,
            ValidAudiences = p.ClientIds,
            IssuerSigningKeys = keys,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256 },
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            LifetimeValidator = (notBefore, expires, _, v) =>
                expires != null && now <= expires.Value + v.ClockSkew && (notBefore == null || now >= notBefore.Value - v.ClockSkew),
        };

        var result = await _handler.ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid) return null;

        string? subject = result.Claims.TryGetValue("sub", out var sub) ? sub as string : null;
        if (string.IsNullOrEmpty(subject)) return null;

        // Keep the email only when the provider says it is verified.
        string? email = result.Claims.TryGetValue("email", out var e) ? e as string : null;
        bool verified = result.Claims.TryGetValue("email_verified", out var ev) && (ev is true || (ev is string s && s == "true"));
        return new VerifiedIdentity(provider, subject, verified ? email : null);
    }
}
