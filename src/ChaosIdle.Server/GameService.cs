using System.Text.Json;
using ChaosIdle.Domain;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Domain.Profile;
using ChaosIdle.Persistence;
using ChaosIdle.Simulation.Offline;
using ChaosIdle.Simulation.Reporting;

namespace ChaosIdle.Server;

public enum Outcome
{
    Ok,
    BadRequest,
    NotFound,
    Conflict,
    Forbidden,
    Invalid,
    Unauthorized,
    Unavailable,
}

public sealed record ServiceResult<T>(Outcome Outcome, T? Value, string? Error = null, object? Details = null);

public sealed record SaveAccepted(long Revision);

public sealed record OfflineStarted(string SessionId, DateTimeOffset StartedAtUtc, long Revision);

public sealed record OfflineClaimed(
    string SessionId, long Revision, double CreditedHours, int RunsCompleted, int RunsFailed,
    int ItemsFound, IReadOnlyList<string> KeptItemIds, int Hidden, int OverStorageCap, int NoBagSpace,
    IReadOnlyList<string> Report);

/// <summary>
/// Server rules for saves and offline sessions. The server owns time, seeds, and claims;
/// clients own nothing that pays out rewards.
/// </summary>
public sealed class GameService
{
    public const int MaxSaveBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IServerStore _store;
    private readonly ContentRegistry _content;
    private readonly SaveSerializer _serializer;
    private readonly OfflineCalculator _offline;
    private readonly SaveRules _rules;
    private readonly TimeProvider _time;

    public GameService(IServerStore store, ContentRegistry content, SaveSerializer serializer, OfflineCalculator offline, SaveRules rules, TimeProvider time)
    {
        _store = store;
        _content = content;
        _serializer = serializer;
        _offline = offline;
        _rules = rules;
        _time = time;
    }

    /// <summary>
    /// Accepts an upload only if it was based on the server's current revision (optimistic
    /// concurrency) and describes a valid game. Re-sending the exact current save is a no-op.
    /// </summary>
    public ServiceResult<SaveAccepted> PutSave(Account account, string slot, long baseRevision, string text)
    {
        if (text.Length > MaxSaveBytes) return new(Outcome.BadRequest, null, "Save is too large.");

        LoadedGame loaded;
        SaveEnvelope envelope;
        try
        {
            (var data, envelope) = _serializer.Deserialize(text);
            if (envelope.MigrationsApplied.Count > 0)
                return new(Outcome.BadRequest, null, "Upload saves in the current format; update the game.");
            var violations = SaveValidator.Validate(data, _content, _rules);
            if (violations.Count > 0) return new(Outcome.Invalid, null, "The save describes things the game cannot produce.", violations);
            loaded = SaveMapper.FromSave(data, _content);
        }
        catch (SaveLoadException e)
        {
            return new(Outcome.BadRequest, null, e.Message, new { error = e.Error.ToString() });
        }

        if (loaded.Warnings.Count > 0) return new(Outcome.Invalid, null, "The save references content this server does not have.", loaded.Warnings);

        return _store.WithAccountLock(account.Id, () =>
        {
            var current = _store.GetSave(account.Id, slot);
            long serverRevision = current?.Revision ?? 0;

            if (current != null && envelope.Revision == current.Revision && current.Text == text)
                return new ServiceResult<SaveAccepted>(Outcome.Ok, new SaveAccepted(current.Revision));
            if (baseRevision != serverRevision)
                return new(Outcome.Conflict, null, "The cloud save changed since this device last synced.", new { serverRevision });
            if (envelope.Revision <= serverRevision)
                return new(Outcome.BadRequest, null, $"Save revision must be greater than {serverRevision}.");

            // Offline sessions are created and closed only by the server. The uploaded save must
            // carry exactly the server's open session (or none when there is none).
            var open = _store.GetOpenSession(account.Id, slot);
            string? uploadedSession = loaded.State.Offline?.SessionId;
            if (open != null && uploadedSession != open.Id)
                return new(Outcome.Conflict, null, "An offline session is running; claim it before overwriting the save.", new { sessionId = open.Id });
            if (open == null && uploadedSession != null)
                return new(Outcome.Invalid, null, "Offline sessions can only be started by the server.", new[] { uploadedSession });

            _store.PutSave(new CloudSave(account.Id, slot, envelope.Revision, text, _time.GetUtcNow()));
            return new(Outcome.Ok, new SaveAccepted(envelope.Revision));
        });
    }

    /// <summary>
    /// Starts an offline session from the cloud copy of the character, at server time with a
    /// server seed, and writes it into the cloud save so every device sees it.
    /// </summary>
    public ServiceResult<OfflineStarted> StartOffline(Account account, string slot, string mapId, IReadOnlyList<string> modifierIds)
    {
        var map = _content.Map(mapId);
        if (map == null) return new(Outcome.BadRequest, null, $"Unknown map {mapId}.");
        var modifiers = modifierIds.Select(id => _content.MapModifier(id)).ToList();
        if (modifiers.Any(m => m == null)) return new(Outcome.BadRequest, null, "Unknown map modifier.");
        var instance = new MapInstance { Map = map, Modifiers = modifiers! };
        if (instance.Validate().Count > 0) return new(Outcome.BadRequest, null, string.Join(" ", instance.Validate()));

        return _store.WithAccountLock(account.Id, () =>
        {
            var current = _store.GetSave(account.Id, slot);
            if (current == null) return new ServiceResult<OfflineStarted>(Outcome.NotFound, null, "Upload a save first.");
            if (_store.GetOpenSession(account.Id, slot) is { } open)
                return new(Outcome.Conflict, null, "An offline session is already running.", new { sessionId = open.Id });

            var (state, revision) = LoadCloud(current);
            if (!state.Progress.CanRun(map)) return new(Outcome.Forbidden, null, $"Tier {map.Tier} is not unlocked.");

            var now = _time.GetUtcNow();
            var checkpoint = OfflineCheckpoint.Start("off_" + Guid.NewGuid().ToString("N"), state.Character, instance, now.UtcDateTime,
                BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)));
            if (!state.StartOffline(checkpoint))
                return new(Outcome.Conflict, null, "The cloud save already holds an offline session.");
            long newRevision = WriteCloud(account, slot, state, revision);

            _store.AddSession(new OfflineSessionRecord(checkpoint.SessionId, account.Id, slot,
                JsonSerializer.Serialize(SaveMapper.ToSave(checkpoint), Json), now, null, null, null));
            return new(Outcome.Ok, new OfflineStarted(checkpoint.SessionId, now, newRevision));
        });
    }

    /// <summary>
    /// Resolves a session at server time and puts the rewards straight into the cloud save.
    /// Idempotent per key: the same key returns the same answer; another key after a claim
    /// is a conflict that still returns the original answer.
    /// </summary>
    public ServiceResult<OfflineClaimed> Claim(Account account, string sessionId, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            return new(Outcome.BadRequest, null, "An Idempotency-Key header (1-100 characters) is required.");

        return _store.WithAccountLock(account.Id, () =>
        {
            var session = _store.GetSession(sessionId);
            if (session == null || session.AccountId != account.Id) return new ServiceResult<OfflineClaimed>(Outcome.NotFound, null, "No such session.");

            if (session.ClaimResponseJson != null)
            {
                var previous = JsonSerializer.Deserialize<OfflineClaimed>(session.ClaimResponseJson, Json);
                return session.ClaimKey == idempotencyKey
                    ? new(Outcome.Ok, previous)
                    : new(Outcome.Conflict, previous, "This session was already claimed.");
            }

            var current = _store.GetSave(account.Id, session.Slot);
            if (current == null) return new(Outcome.NotFound, null, "The save for this session is gone.");
            var (state, revision) = LoadCloud(current);

            var warnings = new List<string>();
            var stored = JsonSerializer.Deserialize<ChaosIdle.Persistence.OfflineSessionSave>(session.SessionJson, Json)!;
            var checkpoint = SaveMapper.ToCheckpoint(stored, state.Character.Id, state.Character.Name, _content, warnings)
                             ?? throw new InvalidOperationException($"Session {sessionId} can no longer be resolved: {string.Join(" ", warnings)}");

            var now = _time.GetUtcNow();
            var result = _offline.Resolve(checkpoint, now.UtcDateTime);
            var claim = result.Claim(state.Character.Inventory, state.LootFilter, _offline.Settings);
            state.CompleteOffline(sessionId);
            long newRevision = WriteCloud(account, session.Slot, state, revision);

            var response = new OfflineClaimed(sessionId, newRevision, result.CreditedTime.TotalHours, result.RunsCompleted, result.RunsFailed,
                result.Loot.Count, claim.Kept.Select(i => i.Id).ToList(), claim.Hidden.Count, claim.OverStorageCap.Count, claim.NoBagSpace.Count,
                ReportText.Return(result, claim));
            _store.UpdateSession(session with { ClaimKey = idempotencyKey, ClaimResponseJson = JsonSerializer.Serialize(response, Json), ClaimedAt = now });
            return new(Outcome.Ok, response);
        });
    }

    private (GameState State, long Revision) LoadCloud(CloudSave save)
    {
        var (data, envelope) = _serializer.Deserialize(save.Text);
        var loaded = SaveMapper.FromSave(data, _content);
        loaded.State.Revision = envelope.Revision;
        return (loaded.State, envelope.Revision);
    }

    private long WriteCloud(Account account, string slot, GameState state, long revision)
    {
        long next = revision + 1;
        var now = _time.GetUtcNow();
        _store.PutSave(new CloudSave(account.Id, slot, next, _serializer.Serialize(SaveMapper.ToSave(state), next, now.UtcDateTime), now));
        return next;
    }
}
