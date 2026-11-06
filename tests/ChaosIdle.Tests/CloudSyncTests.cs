using System.Text.Json;
using ChaosIdle.App;
using ChaosIdle.Content;
using ChaosIdle.Persistence;
using Xunit;

namespace ChaosIdle.Tests;

/// <summary>
/// The game's cloud sync (<see cref="GameSession"/>) against the real server. Part of
/// <see cref="ServerTests"/>, so it also runs on PostgreSQL.
/// </summary>
public partial class ServerTests
{
    private readonly List<string> _deviceDirs = new();
    private ulong _deviceSeed = 1;

    private void DeleteDeviceDirs()
    {
        foreach (var d in _deviceDirs)
            if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private DateTime Now => _factory.Time.Now.UtcDateTime;

    /// <summary>A phone with its own save folder, talking to the test server.</summary>
    private GameSession Phone(string? dir = null, HttpClient? http = null)
    {
        dir ??= NewDeviceDir();
        var s = GameSession.LoadOrCreate(dir, Now, () => _deviceSeed++);
        s.UseCloud(http ?? _factory.CreateClient());
        return s;
    }

    private string NewDeviceDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "chaos-idle-phone-" + Guid.NewGuid().ToString("N"));
        _deviceDirs.Add(dir);
        return dir;
    }

    /// <summary>A second phone signed in to the same cloud account but never synced (like after a sign-in).</summary>
    private GameSession SecondPhone(string firstPhoneDir)
    {
        var link = JsonSerializer.Deserialize<CloudLink>(File.ReadAllText(Path.Combine(firstPhoneDir, GameSession.CloudFile)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        string dir = NewDeviceDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, GameSession.CloudFile),
            JsonSerializer.Serialize(new CloudLink { AccountId = link.AccountId, Token = link.Token }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return Phone(dir);
    }

    private static string Checksum(GameSession s) => SaveSerializer.PayloadChecksum(SaveMapper.ToSave(s.State));

    private async Task<string> CloudChecksum(GameSession s)
    {
        var link = JsonSerializer.Deserialize<CloudLink>(File.ReadAllText(_linkPaths[s]), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var (data, _) = _serializer.Deserialize(await (await Device(link.Token).GetAsync("/v1/saves/main")).Content.ReadAsStringAsync());
        return SaveSerializer.PayloadChecksum(data);
    }

    private readonly Dictionary<GameSession, string> _linkPaths = new();

    private async Task<(GameSession Session, string Dir)> ConnectedPhone()
    {
        string dir = NewDeviceDir();
        var s = Phone(dir);
        var status = await s.ConnectCloudAsync(Now);
        Assert.Equal(CloudState.Synced, status.State);
        _linkPaths[s] = Path.Combine(dir, GameSession.CloudFile);
        return (s, dir);
    }

    private void Play(GameSession s) => s.RunExpedition(Maps.SunkenCauseway.Id, Now);

    [SkippableFact]
    public async Task Connecting_uploads_this_phone_and_another_phone_can_take_it()
    {
        var (first, dir) = await ConnectedPhone();
        Play(first);
        Assert.Equal(CloudState.Synced, (await first.SyncAsync(Now)).State);
        Assert.Equal(Checksum(first), await CloudChecksum(first));

        var second = SecondPhone(dir);
        _linkPaths[second] = _linkPaths[first];
        var status = await second.SyncAsync(Now);
        Assert.Equal(CloudState.Conflict, status.State); // its own new game vs the cloud: the player chooses
        Assert.Contains("Cloud:", second.PendingConflict!.Cloud);

        Assert.Equal(CloudState.Synced, (await second.ResolveConflictAsync(keepThisDevice: false, Now)).State);
        Assert.Equal(Checksum(first), Checksum(second));
        Assert.Null(second.PendingConflict);
    }

    [SkippableFact]
    public async Task Changes_flow_both_ways_when_only_one_side_changed()
    {
        var (a, dir) = await ConnectedPhone();
        var b = SecondPhone(dir);
        _linkPaths[b] = _linkPaths[a];
        await b.SyncAsync(Now);
        await b.ResolveConflictAsync(false, Now);

        Play(a);
        Assert.Equal(CloudState.Synced, (await a.SyncAsync(Now)).State);
        var pulled = await b.SyncAsync(Now);
        Assert.Equal(CloudState.Synced, pulled.State);
        Assert.Contains("newer save", pulled.Message);
        Assert.Equal(Checksum(a), Checksum(b));

        Play(b);
        await b.SyncAsync(Now);
        await a.SyncAsync(Now);
        Assert.Equal(Checksum(b), Checksum(a));
        Assert.Equal(a.State.Revision, b.State.Revision); // local and cloud revisions agree
    }

    [SkippableFact]
    public async Task When_both_sides_changed_nothing_is_overwritten_until_the_player_chooses()
    {
        var (a, dir) = await ConnectedPhone();
        var b = SecondPhone(dir);
        _linkPaths[b] = _linkPaths[a];
        await b.SyncAsync(Now);
        await b.ResolveConflictAsync(false, Now);

        Play(a);
        await a.SyncAsync(Now);
        string aGame = Checksum(a);
        Play(b);
        string bGame = Checksum(b);

        Assert.Equal(CloudState.Conflict, (await b.SyncAsync(Now)).State);
        Assert.Equal(aGame, await CloudChecksum(a)); // cloud untouched
        Assert.Equal(bGame, Checksum(b));             // phone untouched

        Assert.Equal(CloudState.Synced, (await b.ResolveConflictAsync(keepThisDevice: true, Now)).State);
        Assert.Equal(bGame, await CloudChecksum(a));
        await a.SyncAsync(Now); // a had no new changes: it takes b's game
        Assert.Equal(bGame, Checksum(a));
    }

    [SkippableFact]
    public async Task Pausing_moves_offline_progress_to_the_server_and_resuming_collects_it()
    {
        var (phone, _) = await ConnectedPhone();
        int before = phone.Character.Inventory.Items.Count;

        await phone.OnPauseAsync(Now);
        Assert.NotNull(phone.State.Offline);
        Assert.StartsWith("off_", phone.State.Offline!.SessionId); // the server's session, not a local one

        // A quick app switch keeps the session running.
        _factory.Time.Now = _factory.Time.Now.AddSeconds(30);
        Assert.Empty(await phone.OnResumeAsync(Now));
        Assert.NotNull(phone.State.Offline);

        _factory.Time.Now = _factory.Time.Now.AddHours(3);
        var report = await phone.OnResumeAsync(Now);
        Assert.NotEmpty(report);
        Assert.Null(phone.State.Offline);
        Assert.True(phone.Character.Inventory.Items.Count >= before);
        Assert.Equal(CloudState.Synced, phone.Cloud.State);
        Assert.Equal(Checksum(phone), await CloudChecksum(phone));
    }

    [SkippableFact]
    public async Task Offline_progress_collected_on_one_phone_is_not_paid_again_on_another()
    {
        var (a, dir) = await ConnectedPhone();
        await a.OnPauseAsync(Now); // the server session is now in the cloud save

        var b = SecondPhone(dir);
        _linkPaths[b] = _linkPaths[a];
        await b.SyncAsync(Now);
        await b.ResolveConflictAsync(false, Now);
        Assert.Equal(a.State.Offline!.SessionId, b.State.Offline!.SessionId);

        _factory.Time.Now = _factory.Time.Now.AddHours(3);
        Assert.NotEmpty(await b.OnResumeAsync(Now)); // b collects
        string paid = Checksum(b);

        var lines = await a.OnResumeAsync(Now);       // a must not pay it again
        Assert.Contains("already collected", Assert.Single(lines));
        Assert.Null(a.State.Offline);
        Assert.Equal(paid, Checksum(a));
    }

    [SkippableFact]
    public async Task Without_a_connection_offline_progress_falls_back_to_this_phone_and_syncs_later()
    {
        var (phone, dir) = await ConnectedPhone();
        var unreachable = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/"), Timeout = TimeSpan.FromSeconds(2) };
        phone.UseCloud(unreachable);

        await phone.OnPauseAsync(Now);
        Assert.StartsWith("local_", phone.State.Offline!.SessionId);
        Assert.Equal(CloudState.Pending, (await phone.SyncAsync(Now)).State); // can't upload a local session

        _factory.Time.Now = _factory.Time.Now.AddHours(2);
        Assert.NotEmpty(await phone.OnResumeAsync(Now)); // resolved on the phone
        Assert.Null(phone.State.Offline);
        Assert.Equal(CloudState.Pending, phone.Cloud.State);

        phone.UseCloud(_factory.CreateClient()); // back online
        Assert.Equal(CloudState.Synced, (await phone.SyncAsync(Now)).State);
        Assert.Equal(Checksum(phone), await CloudChecksum(phone));
    }

    [SkippableFact]
    public async Task A_signed_out_phone_keeps_its_save_and_forgets_the_token()
    {
        var (phone, dir) = await ConnectedPhone();
        var link = JsonSerializer.Deserialize<CloudLink>(File.ReadAllText(Path.Combine(dir, GameSession.CloudFile)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        _factory.Store.RevokeAllTokens(link.AccountId, _factory.Time.Now);
        string game = Checksum(phone);

        Assert.Equal(CloudState.SignedOut, (await phone.SyncAsync(Now)).State);
        Assert.False(phone.HasCloudAccount);
        Assert.False(File.Exists(Path.Combine(dir, GameSession.CloudFile)));
        Assert.Equal(game, Checksum(phone));

        Assert.Equal(CloudState.Synced, (await phone.ConnectCloudAsync(Now)).State); // a new guest account
    }

    [SkippableFact]
    public async Task No_connection_is_a_pending_state_not_an_error()
    {
        var phone = Phone(http: new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/"), Timeout = TimeSpan.FromSeconds(2) });
        var status = await phone.ConnectCloudAsync(Now);
        Assert.Equal(CloudState.NotConnected, status.State);
        Assert.Contains("No connection", status.Message);
        Assert.False(phone.HasCloudAccount);
    }
}
