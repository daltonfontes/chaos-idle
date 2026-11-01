using System;
using System.Collections.Generic;

namespace ChaosIdle.Persistence
{
    // Plain data objects: public get/set, no logic, IDs and rolled values only. They map 1:1 to
    // the JSON payload, so any serializer (System.Text.Json here, Newtonsoft in Unity) can read
    // them. Renaming a property is a schema change and needs a migration.

    public sealed class SaveData
    {
        public CharacterSave Character { get; set; } = new CharacterSave();
        public MapProgressSave MapProgress { get; set; } = new MapProgressSave();
        public LootFilterSave LootFilter { get; set; } = new LootFilterSave();
        public OfflineSessionSave? Offline { get; set; }
        public List<string> ClaimedOfflineSessions { get; set; } = new List<string>();
    }

    public sealed class CharacterSave
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public LoadoutSave Loadout { get; set; } = new LoadoutSave();
        public int InventoryCapacity { get; set; }
        public List<ItemSave> Inventory { get; set; } = new List<ItemSave>();
    }

    /// <summary>Everything that defines a build. Also frozen inside offline sessions.</summary>
    public sealed class LoadoutSave
    {
        public string ClassId { get; set; } = "";
        public string PassiveTreeId { get; set; } = "";
        public int PassivePoints { get; set; }
        public List<string> AllocatedPassives { get; set; } = new List<string>();
        public Dictionary<string, ItemSave> Equipment { get; set; } = new Dictionary<string, ItemSave>();
        public string PrimarySkillId { get; set; } = "";
        public List<string> TriggeredSkillIds { get; set; } = new List<string>();
        public Dictionary<string, List<string>> Supports { get; set; } = new Dictionary<string, List<string>>();
    }

    public sealed class ItemSave
    {
        public string Id { get; set; } = "";
        public string BaseId { get; set; } = "";
        public string Rarity { get; set; } = "";
        public int ItemLevel { get; set; }
        public string? UniqueId { get; set; }
        public List<AffixSave> Affixes { get; set; } = new List<AffixSave>();
    }

    public sealed class AffixSave
    {
        public string AffixId { get; set; } = "";
        public int Tier { get; set; }
        public double Value { get; set; }
    }

    public sealed class MapProgressSave
    {
        public int HighestUnlockedTier { get; set; } = 1;
        public List<string> BossesDefeated { get; set; } = new List<string>();
    }

    public sealed class LootFilterSave
    {
        public string Name { get; set; } = "";
        public string DefaultAction { get; set; } = "Show";
        public List<LootFilterRuleSave> Rules { get; set; } = new List<LootFilterRuleSave>();
    }

    public sealed class LootFilterRuleSave
    {
        public string Name { get; set; } = "";
        public string Action { get; set; } = "Show";
        public string? MinRarity { get; set; }
        public string? MaxRarity { get; set; }
        public List<string> Classes { get; set; } = new List<string>();
        public int MinItemLevel { get; set; }
        public List<string> WantedStats { get; set; } = new List<string>();
        public int MinMatchingAffixes { get; set; }
        public int? MaxMatchingAffixes { get; set; }
    }

    public sealed class OfflineSessionSave
    {
        public string SessionId { get; set; } = "";
        public string MapId { get; set; } = "";
        public List<string> MapModifierIds { get; set; } = new List<string>();
        public DateTime StartedAtUtc { get; set; }

        /// <summary>Stored as a string: JSON numbers lose precision above 2^53.</summary>
        public string Seed { get; set; } = "0";

        /// <summary>The build as it was when the session started.</summary>
        public LoadoutSave Loadout { get; set; } = new LoadoutSave();
    }
}
