using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ChaosIdle.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ChaosIdle.Tests;

/// <summary>Running behind the HTTPS proxy: rate limits, forwarded client IPs, and the health check.</summary>
public class HostingTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static ServerFactory Server(params (string Key, string Value)[] settings) =>
        new(settings.ToDictionary(s => s.Key, s => s.Value));

    private static HttpRequestMessage NewAccount(string? forwardedFor = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/accounts");
        if (forwardedFor != null) req.Headers.Add("X-Forwarded-For", forwardedFor);
        return req;
    }

    [Fact]
    public async Task Account_creation_is_limited_per_client_ip()
    {
        using var server = Server(("RateLimits:AnonymousPerMinute", "3"));
        var client = server.CreateClient();
        for (int i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(NewAccount())).StatusCode);

        var refused = await client.SendAsync(NewAccount());
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("60", refused.Headers.GetValues("Retry-After").Single());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode); // health is never limited
    }

    [Fact]
    public async Task Behind_the_proxy_each_real_client_ip_has_its_own_limit()
    {
        using var server = Server(("RateLimits:AnonymousPerMinute", "2"), ("ForwardedHeaders:Enabled", "true"));
        var client = server.CreateClient();
        for (int i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(NewAccount("203.0.113.7"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(NewAccount("203.0.113.7"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(NewAccount("198.51.100.9"))).StatusCode);
    }

    [Fact]
    public async Task Without_the_proxy_setting_a_forged_forwarded_ip_does_not_dodge_the_limit()
    {
        using var server = Server(("RateLimits:AnonymousPerMinute", "2"));
        var client = server.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(NewAccount("203.0.113.1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(NewAccount("203.0.113.2"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(NewAccount("203.0.113.3"))).StatusCode);
    }

    [Fact]
    public async Task Signed_in_requests_are_limited_per_device()
    {
        using var server = Server(("RateLimits:AccountPerMinute", "3"));
        async Task<HttpClient> Device()
        {
            var account = (await (await server.CreateClient().PostAsync("/v1/accounts", null)).Content.ReadFromJsonAsync<CreateAccountResponse>(Web))!;
            var c = server.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
            return c;
        }

        var busy = await Device();
        var other = await Device();
        for (int i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await busy.GetAsync("/v1/account")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await busy.GetAsync("/v1/account")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/v1/account")).StatusCode);
    }

    [Fact]
    public async Task Health_reports_a_database_that_does_not_answer()
    {
        using var server = new DownStoreFactory();
        var res = await server.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Contains("unavailable", await res.Content.ReadAsStringAsync());
    }

    private sealed class DownStoreFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<IServerStore>();
                s.AddSingleton<IServerStore>(new DownStore());
            });
    }

    /// <summary>A store whose database is gone: every call fails.</summary>
    private sealed class DownStore : IServerStore
    {
        private static Exception Down() => new InvalidOperationException("database down");
        public void Ping() => throw Down();
        public void AddAccount(Account account) => throw Down();
        public Account? FindAccount(string accountId) => throw Down();
        public void AddToken(DeviceToken token) => throw Down();
        public Account? FindAccountByTokenHash(string tokenHash) => throw Down();
        public void RevokeToken(string tokenHash, DateTimeOffset at) => throw Down();
        public int RevokeAllTokens(string accountId, DateTimeOffset at) => throw Down();
        public LinkedIdentity? FindIdentity(string provider, string subject) => throw Down();
        public IReadOnlyList<LinkedIdentity> IdentitiesOf(string accountId) => throw Down();
        public bool TryLinkIdentity(LinkedIdentity identity) => throw Down();
        public CloudSave? GetSave(string accountId, string slot) => throw Down();
        public void PutSave(CloudSave save) => throw Down();
        public void AddSession(OfflineSessionRecord session) => throw Down();
        public OfflineSessionRecord? GetSession(string sessionId) => throw Down();
        public OfflineSessionRecord? GetOpenSession(string accountId, string slot) => throw Down();
        public void UpdateSession(OfflineSessionRecord session) => throw Down();
        public void AddTelemetry(IEnumerable<TelemetryEvent> events) => throw Down();
        public IReadOnlyList<TelemetryEvent> Telemetry => throw Down();
        public T WithAccountLock<T>(string accountId, Func<T> action) => throw Down();
    }
}
