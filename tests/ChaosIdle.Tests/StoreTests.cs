using ChaosIdle.Server;
using Npgsql;
using Xunit;

namespace ChaosIdle.Tests;

/// <summary>Every HTTP API test from <see cref="ServerTests"/>, on a fresh PostgreSQL database.</summary>
public class PostgresServerTests : ServerTests
{
    public PostgresServerTests() : base(new ServerFactory(new PostgresTestDatabase())) { }
}

/// <summary>What any <see cref="IServerStore"/> must do; run on memory and on PostgreSQL.</summary>
public abstract class StoreContractTests : IDisposable
{
    protected static readonly DateTimeOffset T0 = new(2026, 6, 1, 20, 0, 0, TimeSpan.Zero);

    protected abstract IServerStore Store { get; }

    public virtual void Dispose() { }

    protected Account NewAccount(string id = "acc_1")
    {
        var a = new Account(id, "hash_" + id, T0);
        Store.AddAccount(a);
        return a;
    }

    [SkippableFact]
    public void Accounts_are_found_by_token_hash_only()
    {
        var a = NewAccount();
        Assert.Equal(a, Store.FindAccountByTokenHash(a.TokenHash));
        Assert.Null(Store.FindAccountByTokenHash("nope"));
    }

    [SkippableFact]
    public void Saves_round_trip_byte_for_byte_and_overwrite()
    {
        var a = NewAccount();
        const string body = "{ \"b\": 1,  \"a\": [2, 1] }\n"; // spacing and key order must survive (checksums)
        Store.PutSave(new CloudSave(a.Id, "main", 1, body, T0));
        Assert.Equal(new CloudSave(a.Id, "main", 1, body, T0), Store.GetSave(a.Id, "main"));

        Store.PutSave(new CloudSave(a.Id, "main", 2, "{}", T0.AddMinutes(1)));
        Assert.Equal(2, Store.GetSave(a.Id, "main")!.Revision);
        Assert.Null(Store.GetSave(a.Id, "other"));
    }

    [SkippableFact]
    public void Open_sessions_close_when_claimed()
    {
        var a = NewAccount();
        var s = new OfflineSessionRecord("off_1", a.Id, "main", "{\"mapId\":\"map.x\",\"seed\":7}", T0, null, null, null);
        Store.AddSession(s);
        Assert.Equal("off_1", Store.GetOpenSession(a.Id, "main")!.Id);
        Assert.Contains("\"seed\"", Store.GetSession("off_1")!.SessionJson);

        Store.UpdateSession(s with { ClaimKey = "k", ClaimResponseJson = "{\"ok\":true}", ClaimedAt = T0.AddHours(1) });
        Assert.Null(Store.GetOpenSession(a.Id, "main"));
        var claimed = Store.GetSession("off_1")!;
        Assert.Equal("k", claimed.ClaimKey);
        Assert.Equal("{\"ok\":true}", claimed.ClaimResponseJson);
        Assert.Equal(T0.AddHours(1), claimed.ClaimedAt);
    }

    [SkippableFact]
    public void Telemetry_keeps_order_and_properties()
    {
        var a = NewAccount();
        Store.AddTelemetry(new[]
        {
            new TelemetryEvent(a.Id, "app.open", T0, new Dictionary<string, string> { ["build"] = "42" }),
            new TelemetryEvent(a.Id, "map.run", T0.AddSeconds(5), new Dictionary<string, string>()),
        });
        Store.AddTelemetry(Array.Empty<TelemetryEvent>());

        var all = Store.Telemetry;
        Assert.Equal(new[] { "app.open", "map.run" }, all.Select(e => e.Name));
        Assert.Equal("42", all[0].Properties["build"]);
        Assert.Equal(T0.AddSeconds(5), all[1].At);
    }

    [SkippableFact]
    public void Account_lock_serializes_read_modify_write()
    {
        var a = NewAccount();
        Store.PutSave(new CloudSave(a.Id, "main", 0, "{}", T0));

        // Without the lock, two of these could read the same revision and one increment would be lost.
        Parallel.For(0, 40, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
            Store.WithAccountLock(a.Id, () =>
            {
                var current = Store.GetSave(a.Id, "main")!;
                Thread.Sleep(1);
                Store.PutSave(current with { Revision = current.Revision + 1 });
                return 0;
            }));

        Assert.Equal(40, Store.GetSave(a.Id, "main")!.Revision);
    }
}

public class InMemoryStoreTests : StoreContractTests
{
    protected override IServerStore Store { get; } = new InMemoryServerStore();
}

public class PostgresStoreTests : StoreContractTests
{
    private readonly PostgresTestDatabase _db = new();
    private readonly PostgresServerStore _store;

    public PostgresStoreTests() => _store = PostgresServerStore.Open(_db.ConnectionString);

    protected override IServerStore Store => _store;

    public override void Dispose()
    {
        _store.Dispose();
        _db.Dispose();
    }

    [SkippableFact]
    public void Writes_inside_a_failed_lock_are_rolled_back()
    {
        var a = NewAccount();
        Assert.Throws<InvalidOperationException>(() => Store.WithAccountLock<int>(a.Id, () =>
        {
            Store.PutSave(new CloudSave(a.Id, "main", 1, "{}", T0));
            throw new InvalidOperationException("boom");
        }));
        Assert.Null(Store.GetSave(a.Id, "main"));
    }

    [SkippableFact]
    public void The_database_refuses_a_second_open_session_for_a_slot()
    {
        var a = NewAccount();
        Store.AddSession(new OfflineSessionRecord("off_1", a.Id, "main", "{}", T0, null, null, null));
        var e = Assert.Throws<PostgresException>(() =>
            Store.AddSession(new OfflineSessionRecord("off_2", a.Id, "main", "{}", T0, null, null, null)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, e.SqlState);
    }

    [SkippableFact]
    public void Migrations_run_once()
    {
        using var source = NpgsqlDataSource.Create(_db.ConnectionString);
        Assert.Empty(PostgresMigrations.Apply(source)); // Open already applied them
        Assert.Equal(PostgresMigrations.All().Select(m => m.Version),
            Query(source, "select version from schema_migrations order by version"));
    }

    [SkippableFact]
    public void Locks_on_two_accounts_cannot_nest()
    {
        var a = NewAccount("acc_a");
        var b = NewAccount("acc_b");
        Assert.Throws<InvalidOperationException>(() =>
            Store.WithAccountLock(a.Id, () => Store.WithAccountLock(b.Id, () => 0)));
        Assert.Equal(1, Store.WithAccountLock(a.Id, () => Store.WithAccountLock(a.Id, () => 1)));
    }

    private static List<int> Query(NpgsqlDataSource source, string sql)
    {
        using var cmd = source.CreateCommand(sql);
        using var r = cmd.ExecuteReader();
        var list = new List<int>();
        while (r.Read()) list.Add(r.GetInt32(0));
        return list;
    }
}
