using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ChaosIdle.Server;
using Xunit;

namespace ChaosIdle.Tests;

/// <summary>Sign-in over HTTP; part of <see cref="ServerTests"/>, so it also runs on PostgreSQL.</summary>
public partial class ServerTests
{
    private string GoogleToken(string subject, string? email = null, bool verified = true) =>
        TestIdentityProvider.Token(_factory.Time.Now, subject, email, verified);

    private async Task<HttpResponseMessage> SignIn(string idToken, string provider = "google") =>
        await _factory.CreateClient().PostAsJsonAsync($"/v1/auth/{provider}", new SignInRequest(idToken));

    private async Task<SignedIn> SignInOk(string idToken)
    {
        var res = await SignIn(idToken);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<SignedIn>(Web))!;
    }

    private HttpClient Device(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<AccountView> Me(HttpClient client) =>
        (await client.GetFromJsonAsync<AccountView>("/v1/account", Web))!;

    [SkippableFact]
    public async Task Google_sign_in_creates_an_account_and_finds_it_again_on_another_device()
    {
        var first = await SignIn(GoogleToken("g-100", "ana@example.com"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var phone = (await first.Content.ReadFromJsonAsync<SignedIn>(Web))!;
        Assert.True(phone.Created);
        Assert.Equal(new[] { "google" }, phone.SignIns);

        var tablet = await SignInOk(GoogleToken("g-100"));
        Assert.False(tablet.Created);
        Assert.Equal(phone.AccountId, tablet.AccountId);
        Assert.NotEqual(phone.Token, tablet.Token); // one token per device

        var me = await Me(Device(tablet.Token));
        Assert.False(me.IsGuest);
        Assert.Equal("ana@example.com", Assert.Single(me.SignIns).Email);
        Assert.Equal(phone.AccountId, (await Me(Device(phone.Token))).AccountId);
    }

    [SkippableFact]
    public async Task A_guest_links_google_and_gets_the_same_progress_after_reinstalling()
    {
        var guest = await SignedIn();
        Assert.True((await Me(guest)).IsGuest);
        Assert.Equal(HttpStatusCode.OK, (await Put(guest, Text(Starter(), 1), 0)).StatusCode);

        var link = await guest.PostAsJsonAsync("/v1/account/link/google", new SignInRequest(GoogleToken("g-200")));
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        var view = (await link.Content.ReadFromJsonAsync<AccountView>(Web))!;
        Assert.False(view.IsGuest);

        // New phone: sign in with Google, get the same account and the cloud save.
        var reinstalled = await SignInOk(GoogleToken("g-200"));
        Assert.Equal(view.AccountId, reinstalled.AccountId);
        Assert.False(reinstalled.Created);
        var (_, revision) = await Pull(Device(reinstalled.Token));
        Assert.Equal(1, revision);

        // Linking the same identity again is a no-op.
        Assert.Equal(HttpStatusCode.OK, (await guest.PostAsJsonAsync("/v1/account/link/google", new SignInRequest(GoogleToken("g-200")))).StatusCode);
    }

    [SkippableFact]
    public async Task Only_valid_tokens_for_our_app_are_accepted()
    {
        var now = _factory.Time.Now;
        string[] bad =
        {
            "",
            "not-a-jwt",
            TestIdentityProvider.Token(now, "g-1", audience: "someone-elses-app"),
            TestIdentityProvider.Token(now, "g-1", issuer: "https://evil.example.com"),
            TestIdentityProvider.Token(now.AddHours(-3), "g-1"),                      // expired
            TestIdentityProvider.Token(now.AddHours(1), "g-1"),                       // not valid yet
            TestIdentityProvider.Token(now, "g-1", key: TestIdentityProvider.OtherKey), // forged signature
        };
        foreach (var token in bad)
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(token)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SignIn(GoogleToken("g-1"), "apple")).StatusCode);    // not configured
        Assert.Equal(HttpStatusCode.NotFound, (await SignIn(GoogleToken("g-1"), "myspace")).StatusCode); // unknown
        Assert.Equal(HttpStatusCode.Created, (await SignIn(GoogleToken("g-1"))).StatusCode);
    }

    [SkippableFact]
    public async Task Unverified_emails_are_not_kept()
    {
        var s = await SignInOk(GoogleToken("g-300", "maybe@example.com", verified: false));
        Assert.Null(Assert.Single((await Me(Device(s.Token))).SignIns).Email);
    }

    [SkippableFact]
    public async Task Linking_refuses_identities_of_other_accounts()
    {
        var owner = await SignInOk(GoogleToken("g-400"));
        var guest = await SignedIn();

        var taken = await guest.PostAsJsonAsync("/v1/account/link/google", new SignInRequest(GoogleToken("g-400")));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Contains("identity_in_use", await taken.Content.ReadAsStringAsync());
        Assert.True((await Me(guest)).IsGuest);

        var second = await Device(owner.Token).PostAsJsonAsync("/v1/account/link/google", new SignInRequest(GoogleToken("g-401")));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("provider_already_linked", await second.Content.ReadAsStringAsync());

        var forged = await guest.PostAsJsonAsync("/v1/account/link/google",
            new SignInRequest(TestIdentityProvider.Token(_factory.Time.Now, "g-402", key: TestIdentityProvider.OtherKey)));
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
    }

    [SkippableFact]
    public async Task Sign_out_revokes_this_device_or_every_device_but_never_strands_a_guest()
    {
        var guest = await SignedIn();
        var refused = await guest.PostAsync("/v1/account/sign-out", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("guest_account", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/v1/account")).StatusCode);

        var phone = Device((await SignInOk(GoogleToken("g-500"))).Token);
        var tablet = Device((await SignInOk(GoogleToken("g-500"))).Token);
        var laptop = Device((await SignInOk(GoogleToken("g-500"))).Token);

        Assert.Equal(HttpStatusCode.OK, (await phone.PostAsync("/v1/account/sign-out", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/v1/account")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tablet.GetAsync("/v1/account")).StatusCode);

        var all = await tablet.PostAsync("/v1/account/sign-out-everywhere", null);
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.Equal(2, (await all.Content.ReadFromJsonAsync<SignedOut>(Web))!.TokensRevoked);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tablet.GetAsync("/v1/account")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetAsync("/v1/account")).StatusCode);

        // Signing in again still works and finds the same account.
        Assert.False((await SignInOk(GoogleToken("g-500"))).Created);
    }

    [SkippableFact]
    public async Task First_sign_ins_at_the_same_time_end_on_one_account()
    {
        string token = GoogleToken("g-600");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SignInOk(token)));
        Assert.Single(results.Select(r => r.AccountId).Distinct());
        Assert.Equal(1, results.Count(r => r.Created));
        foreach (var r in results) Assert.Equal(HttpStatusCode.OK, (await Device(r.Token).GetAsync("/v1/account")).StatusCode);
    }
}
