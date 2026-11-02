namespace ChaosIdle.Server;

public sealed record Account(string Id, string TokenHash, DateTimeOffset CreatedAt);

public sealed record CloudSave(string AccountId, string Slot, long Revision, string Text, DateTimeOffset UpdatedAt);

/// <param name="SessionJson">The session (map, modifiers, start, seed, frozen loadout) as saved data.</param>
public sealed record OfflineSessionRecord(
    string Id, string AccountId, string Slot, string SessionJson, DateTimeOffset StartedAt,
    string? ClaimKey, string? ClaimResponseJson, DateTimeOffset? ClaimedAt);

public sealed record TelemetryEvent(string AccountId, string Name, DateTimeOffset At, IReadOnlyDictionary<string, string> Properties);

/// <summary>
/// Storage for the server. The in-memory implementation backs tests and local runs; a
/// PostgreSQL implementation (see docs/backend.md) must give the same guarantees, in
/// particular that <see cref="WithAccountLock{T}"/> serializes writes for one account
/// (row lock or serializable transaction).
/// </summary>
public interface IServerStore
{
    void AddAccount(Account account);
    Account? FindAccountByTokenHash(string tokenHash);

    CloudSave? GetSave(string accountId, string slot);
    void PutSave(CloudSave save);

    void AddSession(OfflineSessionRecord session);
    OfflineSessionRecord? GetSession(string sessionId);
    OfflineSessionRecord? GetOpenSession(string accountId, string slot);
    void UpdateSession(OfflineSessionRecord session);

    void AddTelemetry(IEnumerable<TelemetryEvent> events);
    IReadOnlyList<TelemetryEvent> Telemetry { get; }

    /// <summary>Runs <paramref name="action"/> with no other writer for this account running.</summary>
    T WithAccountLock<T>(string accountId, Func<T> action);
}

public sealed class InMemoryServerStore : IServerStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Account> _accountsByHash = new();
    private readonly Dictionary<(string, string), CloudSave> _saves = new();
    private readonly Dictionary<string, OfflineSessionRecord> _sessions = new();
    private readonly List<TelemetryEvent> _telemetry = new();
    private readonly Dictionary<string, object> _accountLocks = new();

    public void AddAccount(Account account) { lock (_gate) _accountsByHash.Add(account.TokenHash, account); }

    public Account? FindAccountByTokenHash(string tokenHash) { lock (_gate) return _accountsByHash.GetValueOrDefault(tokenHash); }

    public CloudSave? GetSave(string accountId, string slot) { lock (_gate) return _saves.GetValueOrDefault((accountId, slot)); }

    public void PutSave(CloudSave save) { lock (_gate) _saves[(save.AccountId, save.Slot)] = save; }

    public void AddSession(OfflineSessionRecord session) { lock (_gate) _sessions.Add(session.Id, session); }

    public OfflineSessionRecord? GetSession(string sessionId) { lock (_gate) return _sessions.GetValueOrDefault(sessionId); }

    public OfflineSessionRecord? GetOpenSession(string accountId, string slot)
    {
        lock (_gate) return _sessions.Values.FirstOrDefault(s => s.AccountId == accountId && s.Slot == slot && s.ClaimedAt == null);
    }

    public void UpdateSession(OfflineSessionRecord session) { lock (_gate) _sessions[session.Id] = session; }

    public void AddTelemetry(IEnumerable<TelemetryEvent> events) { lock (_gate) _telemetry.AddRange(events); }

    public IReadOnlyList<TelemetryEvent> Telemetry { get { lock (_gate) return _telemetry.ToList(); } }

    public T WithAccountLock<T>(string accountId, Func<T> action)
    {
        object accountLock;
        lock (_gate)
        {
            if (!_accountLocks.TryGetValue(accountId, out accountLock!)) _accountLocks[accountId] = accountLock = new object();
        }

        lock (accountLock) return action();
    }
}
