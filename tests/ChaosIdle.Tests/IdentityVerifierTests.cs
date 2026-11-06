using ChaosIdle.Server;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChaosIdle.Tests;

public class IdentityVerifierTests
{
    private static readonly ManualTime Time = new();

    [Fact]
    public void Providers_are_off_until_a_client_id_is_configured()
    {
        var none = OidcIdentityVerifier.FromConfiguration(new ConfigurationBuilder().Build(), Time);
        Assert.False(none.IsEnabled("google"));
        Assert.False(none.IsEnabled("apple"));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Google:ClientIds:0"] = "123-abc.apps.googleusercontent.com",
        }).Build();
        var google = OidcIdentityVerifier.FromConfiguration(config, Time);
        Assert.True(google.IsEnabled("google"));
        Assert.False(google.IsEnabled("apple"));
    }

    [Fact]
    public async Task Tokens_are_checked_against_the_configured_client_id()
    {
        var verifier = TestIdentityProvider.Verifier(Time);
        var ok = await verifier.VerifyAsync("google", TestIdentityProvider.Token(Time.Now, "g-1", "a@example.com"));
        Assert.Equal(new VerifiedIdentity("google", "g-1", "a@example.com"), ok);
        Assert.Null(await verifier.VerifyAsync("google", TestIdentityProvider.Token(Time.Now, "g-1", audience: "other")));
        Assert.Null(await verifier.VerifyAsync("apple", TestIdentityProvider.Token(Time.Now, "g-1")));
    }

    [Fact]
    public async Task Unreachable_keys_mean_unavailable_not_invalid()
    {
        var google = SignInProviderOptions.Google();
        google.ClientIds.Add(TestIdentityProvider.ClientId);
        google.MetadataAddress = "https://127.0.0.1:1/.well-known/openid-configuration"; // nothing listens there
        var verifier = new OidcIdentityVerifier(new Dictionary<string, SignInProviderOptions> { ["google"] = google }, Time);

        await Assert.ThrowsAsync<SignInUnavailableException>(() =>
            verifier.VerifyAsync("google", TestIdentityProvider.Token(Time.Now, "g-1")));
    }
}
