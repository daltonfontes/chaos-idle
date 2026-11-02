using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChaosIdle.Content;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Domain.Profile;
using ChaosIdle.Persistence;
using ChaosIdle.Server;
using ChaosIdle.Simulation.Analysis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ChaosIdle.Tests;

public sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 6, 1, 20, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class ServerFactory : WebApplicationFactory<Program>
{
    public ManualTime Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Time);
        });

    public IServerStore Store => Services.GetRequiredService<IServerStore>();
}

public class ServerTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly ServerFactory _factory = new();
    private readonly SaveSerializer _serializer = new();

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> SignedIn()
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsync("/v1/accounts", null);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var account = (await res.Content.ReadFromJsonAsync<CreateAccountResponse>(Web))!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
        return client;
    }

    private static GameState Starter()
    {
        var c = ReferenceBuilds.IronBastion();
        return new GameState(c, new MapProgress(), BuildRelevance.FilterFor(c.ToBuild()));
    }

    private string Text(GameState state, long revision) => _serializer.Serialize(SaveMapper.ToSave(state), revision, _factory.Time.Now.UtcDateTime);

    private static Task<HttpResponseMessage> Put(HttpClient client, string text, long baseRevision, string slot = "main")
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/v1/saves/{slot}") { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Base-Revision", baseRevision.ToString());
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> Claim(HttpClient client, string sessionId, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/offline/{sessionId}/claim");
        if (key.Length > 0) req.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(req);
    }

    private async Task<(GameState State, long Revision)> Pull(HttpClient client, string slot = "main")
    {
        var res = await client.GetAsync($"/v1/saves/{slot}");
        res.EnsureSuccessStatusCode();
        var (data, envelope) = _serializer.Deserialize(await res.Content.ReadAsStringAsync());
        var loaded = SaveMapper.FromSave(data, ContentDatabase.Default);
        Assert.Empty(loaded.Warnings);
        return (loaded.State, envelope.Revision);
    }

    [Fact]
    public async Task Health_is_public()
    {
        var res = await _factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Game_endpoints_need_a_valid_token()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/v1/saves/main")).StatusCode);
        anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/v1/saves/main")).StatusCode);
    }

    [Fact]
    public async Task Tokens_are_stored_only_as_hashes()
    {
        var anon = _factory.CreateClient();
        var account = (await (await anon.PostAsync("/v1/accounts", null)).Content.ReadFromJsonAsync<CreateAccountResponse>(Web))!;
        Assert.Null(_factory.Store.FindAccountByTokenHash(account.Token));
        Assert.Equal(account.AccountId, _factory.Store.FindAccountByTokenHash(Auth.Hash(account.Token))!.Id);
    }

    [Fact]
    public async Task Upload_download_and_optimistic_concurrency()
    {
        var client = await SignedIn();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/saves/main")).StatusCode);

        var state = Starter();
        string v3 = Text(state, 3);
        Assert.Equal(HttpStatusCode.OK, (await Put(client, v3, 0)).StatusCode);

        var get = await client.GetAsync("/v1/saves/main");
        Assert.Equal("\"3\"", get.Headers.ETag!.Tag);
        Assert.Equal(v3, await get.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await Put(client, v3, 0)).StatusCode); // exact re-send is a no-op

        var stale = await Put(client, Text(state, 4), 0);   // another device's view
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("\"serverRevision\":3", await stale.Content.ReadAsStringAsync());

        state.LootFilter = ChaosIdle.Domain.Loot.LootFilter.ShowEverything; // different content, same revision
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, Text(state, 3), 3)).StatusCode); // revision must grow
        Assert.Equal(HttpStatusCode.OK, (await Put(client, Text(state, 7), 3)).StatusCode);
    }

    [Fact]
    public async Task Saves_are_private_to_their_account()
    {
        var a = await SignedIn();
        var b = await SignedIn();
        await Put(a, Text(Starter(), 1), 0);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/v1/saves/main")).StatusCode);
    }

    [Fact]
    public async Task Broken_or_impossible_saves_are_rejected()
    {
        var client = await SignedIn();
        var state = Starter();
        string good = Text(state, 1);

        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, good.Replace("\"passivePoints\":18", "\"passivePoints\":17"), 0)).StatusCode); // checksum
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, "nonsense", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, good, 0, "Bad Slot!")).StatusCode);

        var noHeader = new HttpRequestMessage(HttpMethod.Put, "/v1/saves/main") { Content = new StringContent(good) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noHeader)).StatusCode);

        state.Character.Inventory.TryAdd(ItemTests.Make(ItemBases.IronCap, ("affix.life", 999)) with { Id = "cheat" });
        var impossible = await Put(client, Text(state, 1), 0);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, impossible.StatusCode);
        Assert.Contains("outside", await impossible.Content.ReadAsStringAsync());

        var huge = await Put(client, new string('x', GameService.MaxSaveBytes + 10), 0);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
    }

    [Fact]
    public async Task Offline_session_runs_on_server_time_and_pays_into_the_cloud_save()
    {
        var client = await SignedIn();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/v1/saves/main/offline", new StartOfflineRequest(Maps.SunkenCauseway.Id, null))).StatusCode);
        await Put(client, Text(Starter(), 1), 0);

        var start = await client.PostAsJsonAsync("/v1/saves/main/offline", new StartOfflineRequest(Maps.SunkenCauseway.Id, new[] { MapModifiers.Savage.Id }));
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var started = (await start.Content.ReadFromJsonAsync<OfflineStarted>(Web))!;
        Assert.Equal(2, started.Revision);
        Assert.Equal(_factory.Time.Now, started.StartedAtUtc);

        var (afterStart, _) = await Pull(client);
        Assert.Equal(started.SessionId, afterStart.Offline!.SessionId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/v1/saves/main/offline", new StartOfflineRequest(Maps.SunkenCauseway.Id, null))).StatusCode);

        _factory.Time.Now += TimeSpan.FromHours(30); // longer than the cap
        Assert.Equal(HttpStatusCode.BadRequest, (await Claim(client, started.SessionId, "")).StatusCode);
        var claim = await Claim(client, started.SessionId, "op-1");
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        var claimed = (await claim.Content.ReadFromJsonAsync<OfflineClaimed>(Web))!;

        Assert.Equal(8, claimed.CreditedHours);
        Assert.Equal(3, claimed.Revision);
        Assert.True(claimed.RunsCompleted > 100);
        Assert.NotEmpty(claimed.KeptItemIds);
        Assert.StartsWith("Welcome back.", claimed.Report[0]);

        var (afterClaim, revision) = await Pull(client);
        Assert.Equal(3, revision);
        Assert.Null(afterClaim.Offline);
        Assert.True(afterClaim.IsClaimed(started.SessionId));
        Assert.Equal(claimed.KeptItemIds, afterClaim.Character.Inventory.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Claims_are_idempotent_and_pay_only_once()
    {
        var client = await SignedIn();
        await Put(client, Text(Starter(), 1), 0);
        var started = (await (await client.PostAsJsonAsync("/v1/saves/main/offline", new StartOfflineRequest(Maps.SunkenCauseway.Id, null)))
            .Content.ReadFromJsonAsync<OfflineStarted>(Web))!;
        _factory.Time.Now += TimeSpan.FromHours(2);

        var first = await (await Claim(client, started.SessionId, "op-1")).Content.ReadAsStringAsync();
        var retry = await Claim(client, started.SessionId, "op-1");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(first, await retry.Content.ReadAsStringAsync());

        var other = await Claim(client, started.SessionId, "op-2");
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);

        var (_, revision) = await Pull(client);
        Assert.Equal(3, revision); // paid exactly once
    }

    [Fact]
    public async Task Concurrent_claims_pay_exactly_once()
    {
        var client = await SignedIn();
        await Put(client, Text(Starter(), 1), 0);
        var started = (await (await client.PostAsJsonAsync("/v1/saves/main/offline", new StartOfflineRequest(Maps.SunkenCauseway.Id, null)))
            .Content.ReadFromJsonAsync<OfflineStarted>(Web))!;
        _factory.Time.Now += TimeSpan.FromHours(1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Claim(client, started.SessionId, $"op-{i}")));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var (_, revision) = await Pull(client);
        Assert.Equal(3, revision);
    }

    [Fact]
    public async Task Another_account_cannot_claim_a_session()
    {
        var owner = await SignedIn();
        var thief = await SignedIn();
        await Put(owner, Text(Starter(), 1), 0);
        var started = (await (await owner.PostAsJsonAsync("/v1/saves/main/offline", new StartOfflineRequest(Maps.SunkenCauseway.Id, null)))
            .Content.ReadFromJsonAsync<OfflineStarted>(Web))!;
        Assert.Equal(HttpStatusCode.NotFound, (await Claim(thief, started.SessionId, "x")).StatusCode);
    }

    [Fact]
    public async Task Locked_tiers_and_unknown_maps_are_refused()
    {
        var client = await SignedIn();
        await Put(client, Text(Starter(), 1), 0);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/saves/main/offline", new StartOfflineRequest(Maps.AshenTrail.Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/saves/main/offline", new StartOfflineRequest("map.nowhere", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/saves/main/offline",
            new StartOfflineRequest(Maps.SunkenCauseway.Id, new[] { MapModifiers.Savage.Id, MapModifiers.Savage.Id }))).StatusCode);
    }

    [Fact]
    public async Task Uploads_cannot_drop_or_forge_offline_sessions()
    {
        var client = await SignedIn();
        var state = Starter();
        Assert.Equal(HttpStatusCode.OK, (await Put(client, Text(state, 1), 0)).StatusCode);

        // A device that never pulled tries to overwrite the save while a session runs.
        var started = (await (await client.PostAsJsonAsync("/v1/saves/main/offline", new StartOfflineRequest(Maps.SunkenCauseway.Id, null)))
            .Content.ReadFromJsonAsync<OfflineStarted>(Web))!;
        Assert.Equal(HttpStatusCode.Conflict, (await Put(client, Text(state, 5), started.Revision)).StatusCode);

        // A device that pulled keeps the session and may save.
        var (pulled, rev) = await Pull(client);
        Assert.Equal(HttpStatusCode.OK, (await Put(client, Text(pulled, rev + 1), rev)).StatusCode);

        // A forged, client-made session is refused when none is open on the server.
        var other = await SignedIn();
        var forged = Starter();
        forged.StartOffline(OfflineCheckpoint.Start("forged", forged.Character, new MapInstance { Map = Maps.SunkenCauseway }, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(other, Text(forged, 1), 0)).StatusCode);
    }

    [Fact]
    public async Task Telemetry_is_validated_and_stored()
    {
        var client = await SignedIn();
        var future = _factory.Time.Now.AddDays(3);
        var ok = await client.PostAsJsonAsync("/v1/telemetry", new TelemetryBatch(new[]
        {
            new TelemetryIn("expedition.completed", _factory.Time.Now, new Dictionary<string, string> { ["map"] = "map.sunken_causeway" }),
            new TelemetryIn("app.resumed", future, null),
        }));
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        Assert.Equal(2, _factory.Store.Telemetry.Count);
        Assert.Equal(_factory.Time.Now, _factory.Store.Telemetry[1].At); // future timestamps are clamped

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/telemetry", new TelemetryBatch(new[] { new TelemetryIn("Bad Name!", future, null) }))).StatusCode);
        var tooMany = Enumerable.Range(0, Api.MaxTelemetryEvents + 1).Select(_ => new TelemetryIn("x.y", future, null)).ToList();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/telemetry", new TelemetryBatch(tooMany))).StatusCode);
    }
}

public class SaveValidatorTests
{
    private static SaveData Save(Action<ChaosIdle.Domain.Characters.PlayerCharacter>? edit = null)
    {
        var c = ReferenceBuilds.IronBastion();
        edit?.Invoke(c);
        return SaveMapper.ToSave(new GameState(c, new MapProgress(), BuildRelevance.FilterFor(c.ToBuild())));
    }

    private static IReadOnlyList<string> Check(SaveData s) => SaveValidator.Validate(s, ContentDatabase.Default);

    [Fact]
    public void Real_builds_and_drops_are_valid()
    {
        Assert.Empty(Check(Save()));
        Assert.Empty(Check(SaveMapper.ToSave(new GameState(StarterCharacters.NewGuardian(), new MapProgress(), ChaosIdle.Domain.Loot.LootFilter.ShowEverything))));

        var gen = OfflineTests.Loot;
        var rng = new ChaosIdle.Simulation.Random.SplitMix64Random(5);
        var save = Save(c =>
        {
            for (int i = 0; i < 20; i++) c.Inventory.TryAdd(gen.Generate(12, true, rng));
        });
        Assert.Empty(Check(save));
    }

    [Fact]
    public void Impossible_items_are_caught()
    {
        Assert.Contains(Check(Save(c => c.Inventory.TryAdd(ItemTests.Make(ItemBases.IronCap, ("affix.life", 999))))), e => e.Contains("outside"));
        Assert.Contains(Check(Save(c => c.Inventory.TryAdd(ItemTests.Make(ItemBases.IronCap, ("affix.life", 40)) with { ItemLevel = 3 }))), e => e.Contains("needs item level"));
        Assert.Contains(Check(Save(c => c.Inventory.TryAdd(ItemTests.Make(ItemBases.IronCap, ("affix.local_inc_phys", 30))))), e => e.Contains("cannot roll"));
        Assert.Contains(Check(Save(c => c.Inventory.TryAdd(ItemTests.Make(ItemBases.IronCap, ("affix.life", 30), ("affix.fire_res", 10), ("affix.cold_res", 10)) with { Rarity = ItemRarity.Magic }))), e => e.Contains("too many affixes"));
        Assert.Contains(Check(Save(c => c.Inventory.TryAdd(ItemTests.Make(ItemBases.IronCap) with { ItemLevel = 50 }))), e => e.Contains("item level 50"));

        var tamperedUnique = Uniques.StillwaterHelm.Roll("u", 10, () => 0.5);
        tamperedUnique = tamperedUnique with { Affixes = tamperedUnique.Affixes.Take(1).ToList() };
        Assert.Contains(Check(Save(c => c.Inventory.TryAdd(tamperedUnique))), e => e.Contains("modifiers differ"));
    }

    [Fact]
    public void Budgets_and_progress_are_checked()
    {
        var s = Save();
        s.Character.Loadout.PassivePoints = 99;
        s.MapProgress.HighestUnlockedTier = 3;
        s.Character.Inventory.Add(s.Character.Loadout.Equipment["MainHand"]);
        var errors = Check(s);

        Assert.Contains(errors, e => e.Contains("Passive points"));
        Assert.Contains(errors, e => e.Contains("More tiers unlocked"));
        Assert.Contains(errors, e => e.Contains("appears more than once"));
    }
}
