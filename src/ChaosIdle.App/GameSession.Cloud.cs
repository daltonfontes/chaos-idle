using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChaosIdle.App.Cloud;
using ChaosIdle.Content;
using ChaosIdle.Domain.Profile;
using ChaosIdle.Persistence;

namespace ChaosIdle.App
{
    public enum CloudState
    {
        /// <summary>This device has no cloud account yet.</summary>
        NotConnected,
        Synced,
        /// <summary>Changes are waiting: no connection, or an offline session must finish first.</summary>
        Pending,
        /// <summary>This device and the cloud both changed; the player must choose.</summary>
        Conflict,
        SignedOut,
        /// <summary>The server refused the save; the message says why.</summary>
        Refused,
    }

    public sealed record CloudStatus(CloudState State, string Message, long Revision);

    /// <summary>What each side holds, in words, so the player can choose.</summary>
    public sealed record CloudConflict(string ThisDevice, string Cloud);

    /// <summary>What a device remembers about its cloud account (cloud.json next to the save).</summary>
    public sealed class CloudLink
    {
        public string AccountId { get; set; } = "";
        public string Token { get; set; } = "";
        /// <summary>The cloud revision this device last matched.</summary>
        public long SyncedRevision { get; set; }
        /// <summary>The content checksum at that moment; a different checksum now means local changes.</summary>
        public string SyncedChecksum { get; set; } = "";
    }

    /// <summary>
    /// Cloud save sync. The rules follow the server (docs/backend.md):
    /// <list type="bullet">
    /// <item>Uploads name the cloud revision they build on; if the cloud moved on and this device
    /// also changed, nothing is overwritten: the player chooses (<see cref="ResolveConflictAsync"/>).</item>
    /// <item>With a cloud account, offline sessions are started and paid by the server. If the
    /// server can't be reached when the app pauses, a local session is the fallback; a server
    /// session is never paid locally.</item>
    /// </list>
    /// </summary>
    public sealed partial class GameSession
    {
        public const string CloudFile = "cloud.json";
        private const string LocalSessionPrefix = "local_";
        private static readonly JsonSerializerOptions LinkJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

        private readonly SaveSerializer _serializer = new SaveSerializer();
        private CloudApi? _api;
        private CloudLink? _link;
        private CloudSaveText? _conflictCloud;
        private bool _syncing;

        public CloudStatus Cloud { get; private set; } = new CloudStatus(CloudState.NotConnected, "Not connected to the cloud.", 0);

        public CloudConflict? PendingConflict { get; private set; }

        public bool HasCloudAccount => _link != null;

        private string LinkPath => Path.Combine(Path.GetDirectoryName(_store.MainPath)!, CloudFile);

        /// <summary>Gives the session a way to reach the server and loads this device's cloud account, if any.</summary>
        public void UseCloud(HttpClient http)
        {
            _api = new CloudApi(http);
            if (File.Exists(LinkPath))
            {
                try
                {
                    _link = JsonSerializer.Deserialize<CloudLink>(File.ReadAllText(LinkPath), LinkJson);
                }
                catch (JsonException)
                {
                    _link = null; // a broken link file only costs the cloud connection, never the save
                }
            }

            _api.Token = _link?.Token;
            Cloud = _link == null
                ? new CloudStatus(CloudState.NotConnected, "Not connected to the cloud.", 0)
                : new CloudStatus(CloudState.Pending, "Not synced yet this session.", _link.SyncedRevision);
        }

        /// <summary>Creates a cloud account for this device (a guest) if it has none, then syncs.</summary>
        public async Task<CloudStatus> ConnectCloudAsync(DateTime nowUtc, CancellationToken cancel = default)
        {
            if (_api == null) throw new InvalidOperationException("Call UseCloud first.");
            if (_link == null)
            {
                var guest = await _api.CreateGuestAsync(cancel);
                if (!guest.Ok) return Cloud = Status(CloudState.NotConnected, Explain(guest.Status, guest.Message));
                _link = new CloudLink { AccountId = guest.Value!.AccountId, Token = guest.Value.Token };
                _api.Token = _link.Token;
                SaveLink();
            }

            return await SyncAsync(nowUtc, cancel);
        }

        /// <summary>Uploads local changes or downloads the cloud's, whichever side changed. Never overwrites both.</summary>
        public async Task<CloudStatus> SyncAsync(DateTime nowUtc, CancellationToken cancel = default)
        {
            if (_api == null || _link == null) return Cloud;
            if (_syncing) return Cloud;
            _syncing = true;
            try
            {
                return Cloud = await SyncCore(nowUtc, cancel);
            }
            finally
            {
                _syncing = false;
            }
        }

        private async Task<CloudStatus> SyncCore(DateTime nowUtc, CancellationToken cancel)
        {
            if (State.Offline != null && IsLocalSession(State.Offline.SessionId))
                return Status(CloudState.Pending, "Offline progress on this device must finish before syncing.");

            var data = SaveMapper.ToSave(State);
            string checksum = SaveSerializer.PayloadChecksum(data);
            bool localChanged = checksum != _link!.SyncedChecksum;

            var cloud = await _api!.GetSaveAsync(Slot, cancel);
            if (cloud.Status == CallStatus.NotFound)
                return await Push(data, checksum, baseRevision: 0, nowUtc, cancel);
            if (!cloud.Ok) return Failed(cloud.Status, cloud.Message);

            var (cloudData, envelope) = _serializer.Deserialize(cloud.Value!.Text);
            string cloudChecksum = SaveSerializer.PayloadChecksum(cloudData);

            if (cloudChecksum == checksum) // same game on both sides
            {
                MarkSynced(envelope.Revision, checksum);
                return Status(CloudState.Synced, "Saved in the cloud.");
            }

            if (envelope.Revision == _link.SyncedRevision) // only this device changed
                return await Push(data, checksum, envelope.Revision, nowUtc, cancel);

            if (!localChanged) // only the cloud changed
            {
                Adopt(cloudData, envelope.Revision, cloudChecksum, nowUtc);
                return Status(CloudState.Synced, "Loaded the newer save from the cloud.");
            }

            _conflictCloud = cloud.Value;
            PendingConflict = new CloudConflict(Describe(State, "This device"), Describe(cloudData, envelope));
            return Status(CloudState.Conflict, "This device and the cloud both changed. Choose which save to keep.");
        }

        /// <summary>Settles a conflict: keep this device's save (overwriting the cloud) or take the cloud's.</summary>
        public async Task<CloudStatus> ResolveConflictAsync(bool keepThisDevice, DateTime nowUtc, CancellationToken cancel = default)
        {
            if (_conflictCloud == null || _link == null) return Cloud;
            var (cloudData, envelope) = _serializer.Deserialize(_conflictCloud.Text);
            _conflictCloud = null;
            PendingConflict = null;

            if (!keepThisDevice)
            {
                Adopt(cloudData, envelope.Revision, SaveSerializer.PayloadChecksum(cloudData), nowUtc);
                return Cloud = Status(CloudState.Synced, "Loaded the cloud save.");
            }

            var data = SaveMapper.ToSave(State);
            return Cloud = await Push(data, SaveSerializer.PayloadChecksum(data), envelope.Revision, nowUtc, cancel);
        }

        private async Task<CloudStatus> Push(SaveData data, string checksum, long baseRevision, DateTime nowUtc, CancellationToken cancel)
        {
            long revision = Math.Max(State.Revision, baseRevision + 1);
            var put = await _api!.PutSaveAsync(Slot, _serializer.Serialize(data, revision, nowUtc), baseRevision, cancel);
            if (put.Status == CallStatus.Conflict)
                return Status(CloudState.Pending, "The cloud changed while uploading; syncing again shortly.");
            if (!put.Ok) return Failed(put.Status, put.Message);

            if (State.Revision != revision) _store.SaveAs(State, revision, nowUtc);
            MarkSynced(revision, checksum);
            return Status(CloudState.Synced, "Saved in the cloud.");
        }

        /// <summary>
        /// App goes to the background. A local offline session starts at once (so progress is never
        /// lost); with a cloud account the session is then moved to the server, which replaces it.
        /// </summary>
        public async Task OnPauseAsync(DateTime nowUtc, CancellationToken cancel = default)
        {
            bool alreadyRunning = State.Offline != null;
            var snapshot = SaveMapper.ToSave(State); // before the local session, for the upload
            string checksum = SaveSerializer.PayloadChecksum(snapshot);
            long revision = State.Revision;
            OnPause(nowUtc);
            if (_api == null || _link == null || alreadyRunning || _syncing) return;

            _syncing = true;
            try
            {
                var cloud = await _api.GetSaveAsync(Slot, cancel);
                long cloudRevision = cloud.Ok ? _serializer.Deserialize(cloud.Value!.Text).Envelope.Revision : 0;
                if (cloud.Ok && cloudRevision != _link.SyncedRevision) return; // the cloud moved on: settle that on resume
                if (!cloud.Ok && cloud.Status != CallStatus.NotFound) return;

                if (checksum != _link.SyncedChecksum || !cloud.Ok)
                {
                    long next = Math.Max(revision, cloudRevision + 1);
                    var put = await _api.PutSaveAsync(Slot, _serializer.Serialize(snapshot, next, nowUtc), cloudRevision, cancel);
                    if (!put.Ok) return;
                    MarkSynced(next, checksum);
                }

                var map = EffectiveOfflineMap();
                var started = await _api.StartOfflineAsync(Slot, map.Map.Id, map.Modifiers.Select(m => m.Id).ToList(), cancel);
                if (!started.Ok) return;

                var withSession = await _api.GetSaveAsync(Slot, cancel);
                if (!withSession.Ok) return; // the server session stands; resume collects it from the server
                var (data, envelope) = _serializer.Deserialize(withSession.Value!.Text);
                Adopt(data, envelope.Revision, SaveSerializer.PayloadChecksum(data), nowUtc); // drops the local session
                Cloud = Status(CloudState.Synced, "Offline progress is running on the server.");
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// App comes back. A server session is collected from the server; a local fallback session
        /// is resolved here. Then the save is synced.
        /// </summary>
        public async Task<IReadOnlyList<string>> OnResumeAsync(DateTime nowUtc, CancellationToken cancel = default)
        {
            if (_api == null || _link == null) return OnResume(nowUtc);

            var cloud = await _api.GetSaveAsync(Slot, cancel);
            if (!cloud.Ok && cloud.Status != CallStatus.NotFound)
            {
                Cloud = Failed(cloud.Status, cloud.Message);
                if (State.Offline != null && !IsLocalSession(State.Offline.SessionId))
                    return new[] { "Offline progress is waiting on the server. Connect to the internet to collect it." };
                return OnResume(nowUtc);
            }

            var cloudSession = cloud.Ok ? _serializer.Deserialize(cloud.Value!.Text).Data.Offline : null;
            if (cloudSession != null)
            {
                if (nowUtc - cloudSession.StartedAtUtc < _offline.Settings.MinimumAbsence) return Array.Empty<string>();

                var claim = await _api.ClaimAsync(cloudSession.SessionId, idempotencyKey: cloudSession.SessionId, cancel);
                if (!claim.Ok && claim.Status != CallStatus.Conflict)
                {
                    Cloud = Failed(claim.Status, claim.Message);
                    return new[] { "Couldn't collect offline progress from the server yet; it will be collected next time." };
                }

                var after = await _api.GetSaveAsync(Slot, cancel);
                if (after.Ok)
                {
                    var (data, envelope) = _serializer.Deserialize(after.Value!.Text);
                    Adopt(data, envelope.Revision, SaveSerializer.PayloadChecksum(data), nowUtc);
                    Cloud = Status(CloudState.Synced, "Saved in the cloud.");
                }

                return claim.Ok ? claim.Value!.Report : new List<string> { "Offline progress was already collected on another device." };
            }

            // No session on the server. A server session still on this device was collected on another
            // device: never pay it here. The cloud holds the result (nothing was played here meanwhile).
            if (State.Offline != null && !IsLocalSession(State.Offline.SessionId))
            {
                if (cloud.Ok)
                {
                    var (data, envelope) = _serializer.Deserialize(cloud.Value!.Text);
                    Adopt(data, envelope.Revision, SaveSerializer.PayloadChecksum(data), nowUtc);
                    Cloud = Status(CloudState.Synced, "Saved in the cloud.");
                }
                else
                {
                    State.CompleteOffline(State.Offline.SessionId);
                    Save(nowUtc);
                }

                return new[] { "Offline progress was already collected on another device." };
            }

            var lines = OnResume(nowUtc); // a local fallback session, if any
            await SyncAsync(nowUtc, cancel);
            return lines;
        }

        private void Adopt(SaveData data, long revision, string checksum, DateTime nowUtc)
        {
            var loaded = SaveMapper.FromSave(data, ContentDatabase.Default);
            State = loaded.State;
            _store.SaveAs(State, revision, nowUtc);
            MarkSynced(revision, checksum);
        }

        private void MarkSynced(long revision, string checksum)
        {
            _link!.SyncedRevision = revision;
            _link.SyncedChecksum = checksum;
            SaveLink();
        }

        private void SaveLink()
        {
            string tmp = LinkPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_link, LinkJson));
            if (File.Exists(LinkPath)) File.Delete(LinkPath);
            File.Move(tmp, LinkPath);
        }

        private CloudStatus Failed(CallStatus status, string message)
        {
            if (status == CallStatus.SignedOut)
            {
                // The token no longer works (signed out elsewhere). Forget it; "Connect" starts again.
                _link = null;
                _api!.Token = null;
                if (File.Exists(LinkPath)) File.Delete(LinkPath);
                return Status(CloudState.SignedOut, "This device was signed out of the cloud. Your save is still here.");
            }

            return status == CallStatus.Refused
                ? Status(CloudState.Refused, "The cloud refused this save: " + message)
                : Status(CloudState.Pending, Explain(status, message));
        }

        private CloudStatus Status(CloudState state, string message) => new CloudStatus(state, message, _link?.SyncedRevision ?? 0);

        private static string Explain(CallStatus status, string message) =>
            status == CallStatus.Offline ? "No connection; will sync when back online." : message;

        private static bool IsLocalSession(string sessionId) => sessionId.StartsWith(LocalSessionPrefix, StringComparison.Ordinal);

        private static string Describe(GameState s, string label) =>
            $"{label}: tier {s.Progress.HighestUnlockedTier} maps, {s.Character.Inventory.Items.Count} items in the bag, " +
            $"{s.Character.Passives.Allocated.Count} passives.";

        private static string Describe(SaveData data, SaveEnvelope envelope)
        {
            var loaded = SaveMapper.FromSave(data, ContentDatabase.Default);
            return Describe(loaded.State, "Cloud") + $" Saved {envelope.SavedAtUtc:yyyy-MM-dd HH:mm} UTC.";
        }
    }
}
