using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ChaosIdle.Server;

/// <summary>
/// <see cref="IServerStore"/> on PostgreSQL. <see cref="WithAccountLock{T}"/> runs the action in
/// one transaction that first locks the account's row (<c>select … for update</c>), so writes for
/// one account are serialized across every server instance, and the action's writes commit or
/// roll back together. Calls made inside the action reuse that transaction.
/// </summary>
public sealed class PostgresServerStore : IServerStore, IDisposable
{
    private readonly NpgsqlDataSource _source;
    private readonly AsyncLocal<NpgsqlTransaction?> _current = new();
    private readonly AsyncLocal<string?> _lockedAccount = new();

    private PostgresServerStore(NpgsqlDataSource source) => _source = source;

    /// <summary>Connects and applies any migrations that have not run yet.</summary>
    public static PostgresServerStore Open(string connectionString)
    {
        var store = new PostgresServerStore(NpgsqlDataSource.Create(connectionString));
        PostgresMigrations.Apply(store._source);
        return store;
    }

    public void Dispose() => _source.Dispose();

    // ---- Accounts ----

    public void AddAccount(Account account) =>
        Execute("insert into accounts (id, token_hash, created_at) values ($1, $2, $3)",
            account.Id, account.TokenHash, account.CreatedAt);

    public Account? FindAccountByTokenHash(string tokenHash) =>
        QuerySingle("select id, token_hash, created_at from accounts where token_hash = $1",
            r => new Account(r.GetString(0), r.GetString(1), r.GetFieldValue<DateTimeOffset>(2)), tokenHash);

    // ---- Saves ----

    public CloudSave? GetSave(string accountId, string slot) =>
        QuerySingle("select account_id, slot, revision, body, updated_at from cloud_saves where account_id = $1 and slot = $2",
            r => new CloudSave(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4)),
            accountId, slot);

    public void PutSave(CloudSave save) =>
        Execute("""
            insert into cloud_saves (account_id, slot, revision, body, updated_at) values ($1, $2, $3, $4, $5)
            on conflict (account_id, slot) do update
              set revision = excluded.revision, body = excluded.body, updated_at = excluded.updated_at
            """, save.AccountId, save.Slot, save.Revision, save.Text, save.UpdatedAt);

    // ---- Offline sessions ----

    private const string SessionColumns = "id, account_id, slot, session::text, started_at, claim_key, claim_response, claimed_at";

    public void AddSession(OfflineSessionRecord s) =>
        Execute($"""
            insert into offline_sessions (id, account_id, slot, session, started_at, claim_key, claim_response, claimed_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8)
            """, s.Id, s.AccountId, s.Slot, Jsonb(s.SessionJson), s.StartedAt, s.ClaimKey, s.ClaimResponseJson, s.ClaimedAt);

    public OfflineSessionRecord? GetSession(string sessionId) =>
        QuerySingle($"select {SessionColumns} from offline_sessions where id = $1", ReadSession, sessionId);

    public OfflineSessionRecord? GetOpenSession(string accountId, string slot) =>
        QuerySingle($"select {SessionColumns} from offline_sessions where account_id = $1 and slot = $2 and claimed_at is null",
            ReadSession, accountId, slot);

    public void UpdateSession(OfflineSessionRecord s) =>
        Execute("""
            update offline_sessions
               set account_id = $2, slot = $3, session = $4, started_at = $5, claim_key = $6, claim_response = $7, claimed_at = $8
             where id = $1
            """, s.Id, s.AccountId, s.Slot, Jsonb(s.SessionJson), s.StartedAt, s.ClaimKey, s.ClaimResponseJson, s.ClaimedAt);

    private static OfflineSessionRecord ReadSession(NpgsqlDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7));

    // ---- Telemetry ----

    public void AddTelemetry(IEnumerable<TelemetryEvent> events)
    {
        var list = events.ToList();
        if (list.Count == 0) return;
        Run(tx =>
        {
            using var batch = new NpgsqlBatch(tx.Connection, tx);
            foreach (var e in list)
            {
                var cmd = new NpgsqlBatchCommand("insert into telemetry_events (account_id, name, at, properties) values ($1, $2, $3, $4)");
                foreach (var p in new object?[] { e.AccountId, e.Name, e.At, Jsonb(JsonSerializer.Serialize(e.Properties)) })
                    cmd.Parameters.Add(Parameter(p));
                batch.BatchCommands.Add(cmd);
            }

            batch.ExecuteNonQuery();
            return 0;
        });
    }

    /// <summary>Every event, oldest first. For tests and small deployments; real analytics should query the table.</summary>
    public IReadOnlyList<TelemetryEvent> Telemetry =>
        Query("select account_id, name, at, properties::text from telemetry_events order by id",
            r => new TelemetryEvent(r.GetString(0), r.GetString(1), r.GetFieldValue<DateTimeOffset>(2),
                JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(3)) ?? new Dictionary<string, string>()));

    // ---- Locking ----

    public T WithAccountLock<T>(string accountId, Func<T> action)
    {
        if (_current.Value != null)
        {
            if (_lockedAccount.Value == accountId) return action(); // already inside this account's transaction
            throw new InvalidOperationException("Nested locks on two accounts are not supported.");
        }

        using var connection = _source.OpenConnection();
        using var tx = connection.BeginTransaction();
        using (var cmd = new NpgsqlCommand("select 1 from accounts where id = $1 for update", connection, tx))
        {
            cmd.Parameters.Add(Parameter(accountId));
            cmd.ExecuteScalar();
        }

        _current.Value = tx;
        _lockedAccount.Value = accountId;
        try
        {
            var result = action();
            tx.Commit();
            return result;
        }
        finally
        {
            _current.Value = null;
            _lockedAccount.Value = null;
        }
    }

    // ---- Helpers ----

    /// <summary>Runs on the current account transaction if there is one, else in a short transaction of its own.</summary>
    private T Run<T>(Func<NpgsqlTransaction, T> work)
    {
        if (_current.Value is { } tx) return work(tx);
        using var connection = _source.OpenConnection();
        using var own = connection.BeginTransaction();
        var result = work(own);
        own.Commit();
        return result;
    }

    private void Execute(string sql, params object?[] args) => Run(tx =>
    {
        using var cmd = Command(sql, tx, args);
        return cmd.ExecuteNonQuery();
    });

    private List<T> Query<T>(string sql, Func<NpgsqlDataReader, T> read, params object?[] args) => Run(tx =>
    {
        using var cmd = Command(sql, tx, args);
        using var reader = cmd.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(read(reader));
        return rows;
    });

    private T? QuerySingle<T>(string sql, Func<NpgsqlDataReader, T> read, params object?[] args) where T : class =>
        Query(sql, read, args).SingleOrDefault();

    private static NpgsqlCommand Command(string sql, NpgsqlTransaction tx, object?[] args)
    {
        var cmd = new NpgsqlCommand(sql, tx.Connection, tx);
        foreach (var a in args) cmd.Parameters.Add(Parameter(a));
        return cmd;
    }

    private sealed record JsonText(string Value);

    private static JsonText Jsonb(string json) => new(json);

    private static NpgsqlParameter Parameter(object? value) => value switch
    {
        null => new NpgsqlParameter { Value = DBNull.Value }, // untyped: PostgreSQL infers the column type
        JsonText j => new NpgsqlParameter { Value = j.Value, NpgsqlDbType = NpgsqlDbType.Jsonb },
        DateTimeOffset d => new NpgsqlParameter { Value = d.ToUniversalTime(), NpgsqlDbType = NpgsqlDbType.TimestampTz },
        _ => new NpgsqlParameter { Value = value },
    };
}

/// <summary>
/// Applies the SQL files in <c>Migrations/</c> (embedded, named <c>NNNN_name.sql</c>) in order,
/// each in its own transaction, recording them in <c>schema_migrations</c>. An advisory lock keeps
/// two server instances starting at once from applying the same migration twice.
/// </summary>
public static class PostgresMigrations
{
    private const long LockKey = 0x43_48_41_4F_53; // "CHAOS"

    public static IReadOnlyList<(int Version, string Name, string Sql)> All()
    {
        var asm = typeof(PostgresMigrations).Assembly;
        return asm.GetManifestResourceNames()
            .Where(n => n.Contains(".Migrations.") && n.EndsWith(".sql"))
            .Select(n =>
            {
                string file = n[(n.IndexOf(".Migrations.") + ".Migrations.".Length)..];
                int version = int.Parse(file[..file.IndexOf('_')]);
                using var stream = asm.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return (version, file, reader.ReadToEnd());
            })
            .OrderBy(m => m.version)
            .ToList();
    }

    /// <returns>The versions applied by this call.</returns>
    public static IReadOnlyList<int> Apply(NpgsqlDataSource source)
    {
        using var connection = source.OpenConnection();
        Exec(connection, null, $"select pg_advisory_lock({LockKey})");
        try
        {
            Exec(connection, null, "create table if not exists schema_migrations (version int primary key, name text not null, applied_at timestamptz not null default now())");
            var done = new HashSet<int>();
            using (var cmd = new NpgsqlCommand("select version from schema_migrations", connection))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) done.Add(r.GetInt32(0));

            var applied = new List<int>();
            foreach (var (version, name, sql) in All())
            {
                if (done.Contains(version)) continue;
                using var tx = connection.BeginTransaction();
                Exec(connection, tx, sql);
                using (var record = new NpgsqlCommand("insert into schema_migrations (version, name) values ($1, $2)", connection, tx))
                {
                    record.Parameters.Add(new NpgsqlParameter { Value = version });
                    record.Parameters.Add(new NpgsqlParameter { Value = name });
                    record.ExecuteNonQuery();
                }

                tx.Commit();
                applied.Add(version);
            }

            return applied;
        }
        finally
        {
            Exec(connection, null, $"select pg_advisory_unlock({LockKey})");
        }
    }

    private static void Exec(NpgsqlConnection connection, NpgsqlTransaction? tx, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, connection, tx);
        cmd.ExecuteNonQuery();
    }
}
