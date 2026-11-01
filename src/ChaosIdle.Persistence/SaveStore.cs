using System;
using System.Collections.Generic;
using System.IO;
using ChaosIdle.Domain;
using ChaosIdle.Domain.Profile;

namespace ChaosIdle.Persistence
{
    public enum SaveSource
    {
        None = 0,
        Main = 1,

        /// <summary>The main file was unreadable; the previous save was used.</summary>
        Backup = 2,
    }

    public sealed record StoreLoadResult(LoadedGame? Game, SaveSource Source, SaveEnvelope? Envelope, IReadOnlyList<string> Problems);

    /// <summary>
    /// One save slot on disk: <c>slot.json</c> plus <c>slot.json.bak</c> (the previous good save).
    /// Writes go to a temporary file first and then replace the main file, so a crash mid-write
    /// leaves either the old or the new save, never half of one. On load, a damaged main file
    /// falls back to the backup and the problem is reported.
    /// </summary>
    public sealed class SaveStore
    {
        private readonly string _path;
        private readonly SaveSerializer _serializer;
        private readonly ContentRegistry _content;

        public SaveStore(string directory, string slot, ContentRegistry content, SaveSerializer? serializer = null)
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, slot + ".json");
            _content = content;
            _serializer = serializer ?? new SaveSerializer();
        }

        public string MainPath => _path;
        public string BackupPath => _path + ".bak";
        private string TempPath => _path + ".tmp";

        /// <summary>Saves the state, incrementing its revision.</summary>
        public void Save(GameState state, DateTime nowUtc)
        {
            state.Revision++;
            string text = _serializer.Serialize(SaveMapper.ToSave(state), state.Revision, nowUtc);

            using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(_path))
            {
                File.Replace(TempPath, _path, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(TempPath, _path);
            }
        }

        public StoreLoadResult Load()
        {
            var problems = new List<string>();
            foreach (var (path, source) in new[] { (_path, SaveSource.Main), (BackupPath, SaveSource.Backup) })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var (data, envelope) = _serializer.Deserialize(File.ReadAllText(path));
                    var game = SaveMapper.FromSave(data, _content);
                    game.State.Revision = envelope.Revision;
                    return new StoreLoadResult(game, source, envelope, problems);
                }
                catch (SaveLoadException e) when (e.Error != SaveLoadError.NewerVersion)
                {
                    problems.Add($"{Path.GetFileName(path)}: {e.Message}");
                }
            }

            return new StoreLoadResult(null, SaveSource.None, null, problems);
        }
    }
}
