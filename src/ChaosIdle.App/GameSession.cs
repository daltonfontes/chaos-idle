using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Content;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Expeditions;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Profile;
using ChaosIdle.Persistence;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Expeditions;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Offline;
using ChaosIdle.Simulation.Reporting;

namespace ChaosIdle.App
{
    /// <summary>An expedition the player can pick on the main screen.</summary>
    public sealed record ExpeditionChoice(string Id, string Name, int Tier, bool Unlocked);

    /// <summary>One bag item as the inventory list shows it.</summary>
    public sealed record InventoryRow(string ItemId, string Label, ItemRarity Rarity, string Verdict);

    /// <summary>
    /// Everything the game screens do, without any engine types: the Godot client calls these
    /// methods and shows the returned text. Owns the game state, saves after every change, and
    /// runs local offline progress around app pause/resume (the server version replaces this
    /// once accounts exist). Not thread-safe: call from the main thread.
    /// </summary>
    public sealed partial class GameSession
    {
        public const string Slot = "main";

        private readonly SaveStore _store;
        private readonly ExpeditionRunner _runner;
        private readonly OfflineCalculator _offline;
        private readonly Func<ulong> _seeds;

        private GameSession(SaveStore store, GameState state, Func<ulong> seeds, LootGenerator loot)
        {
            _store = store;
            State = state;
            _seeds = seeds;
            _runner = new ExpeditionRunner(loot);
            _offline = new OfflineCalculator(loot);
        }

        public GameState State { get; }
        public PlayerCharacter Character => State.Character;

        /// <summary>Messages to show once after loading (content changes, recovered backups).</summary>
        public IReadOnlyList<string> LoadNotices { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Loads the save in <paramref name="saveDirectory"/>, or starts a new Guardian if there is
        /// none. A save that cannot be read is never overwritten silently: the caller gets the
        /// exception and must decide (e.g. offer to start over).
        /// </summary>
        public static GameSession LoadOrCreate(string saveDirectory, DateTime nowUtc, Func<ulong>? seeds = null)
        {
            seeds ??= RandomSeeds;
            var loot = new LootGenerator(ItemBases.All, Affixes.All, uniques: Uniques.All);
            var store = new SaveStore(saveDirectory, Slot, ContentDatabase.Default);
            var loaded = store.Load();

            if (loaded.Game != null)
            {
                var notices = loaded.Problems.Select(p => "Recovered from backup: " + p)
                    .Concat(loaded.Game.Warnings).ToList();
                return new GameSession(store, loaded.Game.State, seeds, loot) { LoadNotices = notices };
            }

            if (loaded.Problems.Count > 0)
            {
                throw new SaveLoadException(SaveLoadError.Corrupt, "Your save could not be read: " + string.Join(" ", loaded.Problems));
            }

            var guardian = StarterCharacters.NewGuardian("char.guardian", "Guardian");
            guardian.Passives = StarterCharacters.StarterPassives();
            var state = new GameState(guardian, new MapProgress(), BuildRelevance.FilterFor(guardian.ToBuild()));
            var session = new GameSession(store, state, seeds, loot) { LoadNotices = new[] { "A new Guardian joins the hunt." } };
            session.Save(nowUtc);
            return session;
        }

        public IReadOnlyList<string> CharacterSheet()
        {
            var s = BuildSummary.Compute(Character.ToBuild());
            return new[]
            {
                $"{Character.Name} — {Character.PrimarySkill.Name}",
                $"Life {s.MaxLife:0}  Armour {s.Armour:0}  Block {s.BlockChance:0}%",
                $"Fire {s.FireResistance:0}%  Cold {s.ColdResistance:0}%  Lightning {s.LightningResistance:0}%",
                $"DPS {s.HitDamagePerSecond:0}  Mana {s.ManaSpentPerSecond:0.#}/{s.ManaRegenPerSecond:0.#} per s",
                $"Map tier unlocked: {State.Progress.HighestUnlockedTier}",
            };
        }

        public IReadOnlyList<ExpeditionChoice> Expeditions() =>
            Maps.All.Select(m => new ExpeditionChoice(m.Id, m.Name, m.Tier, State.Progress.CanRun(m))).ToList();

        /// <summary>
        /// Runs a map actively (boss included), claims loot through the loot filter, records a
        /// boss kill for tier unlocks, saves, and returns the report lines.
        /// </summary>
        public IReadOnlyList<string> RunExpedition(string mapId, DateTime nowUtc)
        {
            var map = Maps.All.FirstOrDefault(m => m.Id == mapId) ?? throw new ArgumentException($"Unknown map {mapId}.");
            if (!State.Progress.CanRun(map)) return new[] { $"{map.Name} is locked: defeat the tier {map.Tier - 1} boss first." };

            var report = _runner.RunMap(Character.ToBuild(), Character.Id, new MapInstance { Map = map }, includeBoss: true, _seeds());
            var lines = ReportText.Expedition(report).ToList();
            lines.Add(ReportText.Claim(report.ClaimLoot(Character.Inventory, State.LootFilter)));

            if (report.Outcome == ExpeditionOutcome.Completed && State.Progress.RecordActiveBossKill(map))
                lines.Add($"Tier {State.Progress.HighestUnlockedTier} maps are now open!");

            Save(nowUtc);
            return lines;
        }

        public IReadOnlyList<InventoryRow> Inventory() =>
            Character.Inventory.Items.Select(i =>
            {
                var best = BestComparison(i);
                string verdict = best == null ? "" : best.Verdict.ToString();
                return new InventoryRow(i.Id, $"{i.Name} [{i.Rarity}]", i.Rarity, verdict);
            }).ToList();

        /// <summary>Tooltip plus the comparison against what is worn in the best-fitting slot.</summary>
        public IReadOnlyList<string> Describe(string itemId)
        {
            var item = Find(itemId);
            var lines = ReportText.ItemTooltip(item).ToList();
            var best = BestComparison(item);
            if (best != null) lines.AddRange(ReportText.Comparison(best));
            return lines;
        }

        /// <summary>Equips into the best-fitting slot; the replaced item goes back to the bag.</summary>
        public string Equip(string itemId, DateTime nowUtc)
        {
            var item = Find(itemId);
            var best = BestComparison(item);
            if (best == null) return $"{item.Name} cannot be equipped.";
            var result = Character.EquipFromInventory(item, best.Slot);
            if (result != EquipResult.Equipped) return $"Could not equip {item.Name}: {result}.";
            Save(nowUtc);
            return $"Equipped {item.Name} in {best.Slot}.";
        }

        /// <summary>Removes an item for good (there is no currency yet to sell it for).</summary>
        public string Discard(string itemId, DateTime nowUtc)
        {
            var item = Find(itemId);
            Character.Inventory.Remove(item);
            Save(nowUtc);
            return $"Discarded {item.Name}.";
        }

        /// <summary>
        /// App goes to the background: start offline progress on the highest unlocked map's packs.
        /// Safe to call repeatedly; only one session runs at a time.
        /// </summary>
        public void OnPause(DateTime nowUtc)
        {
            if (State.Offline == null)
            {
                var map = Maps.All.Where(State.Progress.CanRun).OrderByDescending(m => m.Tier).First();
                var checkpoint = OfflineCheckpoint.Start("local_" + _seeds().ToString("x16"), Character, new MapInstance { Map = map }, nowUtc, _seeds());
                State.StartOffline(checkpoint);
            }

            Save(nowUtc);
        }

        /// <summary>App comes back: resolve and claim the offline session, if any, and return the welcome-back lines.</summary>
        public IReadOnlyList<string> OnResume(DateTime nowUtc)
        {
            var checkpoint = State.Offline;
            if (checkpoint == null) return Array.Empty<string>();

            var result = _offline.Resolve(checkpoint, nowUtc);
            if (result.TooShort)
            {
                return Array.Empty<string>(); // keep the session running; a quick app switch is not a "return"
            }

            var claim = result.Claim(Character.Inventory, State.LootFilter, _offline.Settings);
            State.CompleteOffline(checkpoint.SessionId);
            Save(nowUtc);
            return ReportText.Return(result, claim, "Bosses and new map tiers are only won in active play.");
        }

        public void Save(DateTime nowUtc) => _store.Save(State, nowUtc);

        private Item Find(string itemId) =>
            Character.Inventory.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new ArgumentException($"No item {itemId} in the bag.");

        private ItemComparison? BestComparison(Item item) =>
            item.Class.SlotsFor()
                .Select(slot => ItemComparer.Compare(Character, item, slot))
                .OrderBy(c => c.Replaced == null ? 0 : 1) // prefer an empty slot (e.g. the free ring slot)
                .ThenByDescending(c => c.After.HitDamagePerSecond + c.After.MaxLife)
                .FirstOrDefault();

        private static ulong RandomSeeds()
        {
            var bytes = new byte[8];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }
    }
}
