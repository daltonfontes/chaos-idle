using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChaosIdle.Persistence
{
    public enum SaveLoadError
    {
        /// <summary>Not JSON, truncated, or missing required envelope fields.</summary>
        Corrupt = 1,

        /// <summary>The payload does not match its checksum (damaged or hand-edited).</summary>
        ChecksumMismatch = 2,

        /// <summary>Written by a newer version of the game; loading it could lose data.</summary>
        NewerVersion = 3,

        /// <summary>No migration path from the save's version.</summary>
        NoMigration = 4,

        /// <summary>Content the save cannot work without (class, tree, primary skill) is gone.</summary>
        MissingContent = 5,
    }

    public sealed class SaveLoadException : Exception
    {
        public SaveLoadException(SaveLoadError error, string message, Exception? inner = null) : base(message, inner)
        {
            Error = error;
        }

        public SaveLoadError Error { get; }
    }

    /// <summary>Upgrades a payload from <see cref="FromVersion"/> to FromVersion + 1, in place.</summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }
        string Description { get; }
        void Apply(JsonObject payload);
    }

    public sealed record SaveEnvelope(int SchemaVersion, long Revision, DateTime SavedAtUtc, IReadOnlyList<string> MigrationsApplied);

    /// <summary>
    /// The save file format:
    /// <code>
    /// { "format": "chaos-idle-save", "schemaVersion": 1, "revision": 12,
    ///   "savedAtUtc": "...", "checksum": "sha256:...", "payload": { ... } }
    /// </code>
    /// The checksum covers the exact payload text, so damage is detected before anything is read.
    /// It is a corruption check, not anti-cheat: anyone can recompute it.
    /// Older payloads are upgraded one version at a time by registered migrations, on the raw
    /// JSON, before being read into <see cref="SaveData"/>.
    /// </summary>
    public sealed class SaveSerializer
    {
        public const string Format = "chaos-idle-save";
        public const int CurrentVersion = 1;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            WriteIndented = false,
        };

        private readonly int _currentVersion;
        private readonly Dictionary<int, ISaveMigration> _migrations;

        public SaveSerializer(IEnumerable<ISaveMigration>? migrations = null, int currentVersion = CurrentVersion)
        {
            _currentVersion = currentVersion;
            _migrations = new Dictionary<int, ISaveMigration>();
            foreach (var m in migrations ?? Array.Empty<ISaveMigration>())
            {
                if (_migrations.ContainsKey(m.FromVersion)) throw new ArgumentException($"Two migrations from version {m.FromVersion}.");
                _migrations[m.FromVersion] = m;
            }
        }

        public string Serialize(SaveData data, long revision, DateTime savedAtUtc)
        {
            string payload = JsonSerializer.Serialize(data, Options);
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteString("format", Format);
                w.WriteNumber("schemaVersion", _currentVersion);
                w.WriteNumber("revision", revision);
                w.WriteString("savedAtUtc", DateTime.SpecifyKind(savedAtUtc, DateTimeKind.Utc));
                w.WriteString("checksum", Checksum(payload));
                w.WritePropertyName("payload");
                w.WriteRawValue(payload, skipInputValidation: true);
                w.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public (SaveData Data, SaveEnvelope Envelope) Deserialize(string text)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(text);
            }
            catch (JsonException e)
            {
                throw new SaveLoadException(SaveLoadError.Corrupt, "The save file is not valid JSON (it may be truncated).", e);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("format", out var format) || format.GetString() != Format
                    || !root.TryGetProperty("schemaVersion", out var versionEl) || !versionEl.TryGetInt32(out int version)
                    || !root.TryGetProperty("checksum", out var checksumEl)
                    || !root.TryGetProperty("payload", out var payloadEl) || payloadEl.ValueKind != JsonValueKind.Object)
                {
                    throw new SaveLoadException(SaveLoadError.Corrupt, "The save file is missing required fields.");
                }

                string payloadText = payloadEl.GetRawText();
                if (checksumEl.GetString() != Checksum(payloadText))
                {
                    throw new SaveLoadException(SaveLoadError.ChecksumMismatch, "The save file is damaged: its contents do not match the checksum.");
                }

                if (version > _currentVersion)
                {
                    throw new SaveLoadException(SaveLoadError.NewerVersion,
                        $"This save was made by a newer version of the game (save version {version}, this game reads up to {_currentVersion}). Update the game to load it.");
                }

                long revision = root.TryGetProperty("revision", out var r) && r.TryGetInt64(out var rv) ? rv : 0;
                DateTime savedAt = root.TryGetProperty("savedAtUtc", out var t) && t.TryGetDateTime(out var dt) ? dt.ToUniversalTime() : DateTime.MinValue;

                var payload = JsonNode.Parse(payloadText)!.AsObject();
                var applied = new List<string>();
                for (int v = version; v < _currentVersion; v++)
                {
                    if (!_migrations.TryGetValue(v, out var migration))
                        throw new SaveLoadException(SaveLoadError.NoMigration, $"No migration from save version {v} to {v + 1}.");
                    migration.Apply(payload);
                    applied.Add($"v{v}→v{v + 1}: {migration.Description}");
                }

                SaveData? data;
                try
                {
                    data = payload.Deserialize<SaveData>(Options);
                }
                catch (JsonException e)
                {
                    throw new SaveLoadException(SaveLoadError.Corrupt, "The save's contents could not be read.", e);
                }

                if (data == null) throw new SaveLoadException(SaveLoadError.Corrupt, "The save is empty.");
                return (data, new SaveEnvelope(version, revision, savedAt, applied));
            }
        }

        public static string Checksum(string payload)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return "sha256:" + string.Concat(hash.Select(b => b.ToString("x2")));
        }
    }
}
