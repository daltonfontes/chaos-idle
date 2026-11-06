namespace ChaosIdle.Server;

public sealed record Account(string Id, DateTimeOffset CreatedAt);

/// <summary>A device's sign-in. Only the SHA-256 hash of the token is stored; one account can have many.</summary>
public sealed record DeviceToken(string Hash, string AccountId, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt = null);

/// <summary>A platform identity (e.g. Google, Apple) linked to an account: <paramref name="Subject"/> is the provider's stable user id.</summary>
public sealed record LinkedIdentity(string Provider, string Subject, string AccountId, string? Email, DateTimeOffset LinkedAt);

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
    Account? FindAccount(string accountId);

    void AddToken(DeviceToken token);
    /// <summary>The account for a token that exists and is not revoked.</summary>
    Account? FindAccountByTokenHash(string tokenHash);
    void RevokeToken(string tokenHash, DateTimeOffset at);
    /// <returns>How many tokens were revoked.</returns>
    int RevokeAllTokens(string accountId, DateTimeOffset at);

    LinkedIdentity? FindIdentity(string provider, string subject);
    IReadOnlyList<LinkedIdentity> IdentitiesOf(string accountId);
    /// <summary>
    /// Links an identity. Returns false, changing nothing, if that identity is already linked to
    /// any account or the account already has an identity from that provider.
    /// </summary>
    bool TryLinkIdentity(LinkedIdentity identity);

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

    /// <summary>Throws if the storage can't be reached (health checks).</summary>
    void Ping();
}

public sealed class InMemoryServerStore : IServerStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Account> _accounts = new();
    private readonly Dictionary<string, DeviceToken> _tokens = new();
    private readonly Dictionary<(string, string), LinkedIdentity> _identities = new();
    private readonly Dictionary<(string, string), CloudSave> _saves = new();
    private readonly Dictionary<string, OfflineSessionRecord> _sessions = new();
    private readonly List<TelemetryEvent> _telemetry = new();
    private readonly Dictionary<string, object> _accountLocks = new();

    public void AddAccount(Account account) { lock (_gate) _accounts.Add(account.Id, account); }

    public Account? FindAccount(string accountId) { lock (_gate) return _accounts.GetValueOrDefault(accountId); }

    public void AddToken(DeviceToken token) { lock (_gate) _tokens.Add(token.Hash, token); }

    public Account? FindAccountByTokenHash(string tokenHash)
    {
        lock (_gate)
            return _tokens.TryGetValue(tokenHash, out var t) && t.RevokedAt == null ? _accounts.GetValueOrDefault(t.AccountId) : null;
    }

    public void RevokeToken(string tokenHash, DateTimeOffset at)
    {
        lock (_gate)
            if (_tokens.TryGetValue(tokenHash, out var t) && t.RevokedAt == null) _tokens[tokenHash] = t with { RevokedAt = at };
    }

    public int RevokeAllTokens(string accountId, DateTimeOffset at)
    {
        lock (_gate)
        {
            var live = _tokens.Values.Where(t => t.AccountId == accountId && t.RevokedAt == null).ToList();
            foreach (var t in live) _tokens[t.Hash] = t with { RevokedAt = at };
            return live.Count;
        }
    }

    public LinkedIdentity? FindIdentity(string provider, string subject) { lock (_gate) return _identities.GetValueOrDefault((provider, subject)); }

    public IReadOnlyList<LinkedIdentity> IdentitiesOf(string accountId)
    {
        lock (_gate) return _identities.Values.Where(i => i.AccountId == accountId).OrderBy(i => i.Provider).ToList();
    }

    public bool TryLinkIdentity(LinkedIdentity identity)
    {
        lock (_gate)
        {
            if (_identities.ContainsKey((identity.Provider, identity.Subject))) return false;
            if (_identities.Values.Any(i => i.AccountId == identity.AccountId && i.Provider == identity.Provider)) return false;
            _identities.Add((identity.Provider, identity.Subject), identity);
            return true;
        }
    }

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

    public void Ping() { }

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
