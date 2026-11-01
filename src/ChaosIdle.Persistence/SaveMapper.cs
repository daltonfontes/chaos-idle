using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChaosIdle.Domain;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Loot;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.OfflineProgression;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Profile;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Persistence
{
    /// <summary>A save loaded into the domain, plus everything that could not be restored exactly.</summary>
    public sealed record LoadedGame(GameState State, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Converts between <see cref="GameState"/> and <see cref="SaveData"/>.
    /// Loading is forgiving about content that changed between versions: unknown items, affixes,
    /// passives, supports or map modifiers are dropped with a warning instead of failing, so a
    /// content patch can never make a save unloadable. Only structurally required content
    /// (the class, the passive tree, the primary skill) is fatal when missing.
    /// </summary>
    public static class SaveMapper
    {
        public static SaveData ToSave(GameState state) => new SaveData
        {
            Character = new CharacterSave
            {
                Id = state.Character.Id,
                Name = state.Character.Name,
                Loadout = ToLoadout(state.Character),
                InventoryCapacity = state.Character.Inventory.Capacity,
                Inventory = state.Character.Inventory.Items.Select(ToSave).ToList(),
            },
            MapProgress = new MapProgressSave
            {
                HighestUnlockedTier = state.Progress.HighestUnlockedTier,
                BossesDefeated = state.Progress.BossesDefeated.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            },
            LootFilter = ToSave(state.LootFilter),
            Offline = state.Offline == null ? null : ToSave(state.Offline, state.Character),
            ClaimedOfflineSessions = state.ClaimedSessions.ToList(),
        };

        public static LoadedGame FromSave(SaveData save, ContentRegistry content)
        {
            var warnings = new List<string>();
            var character = ToCharacter(save.Character, content, warnings);
            var progress = MapProgress.Restore(save.MapProgress.HighestUnlockedTier, save.MapProgress.BossesDefeated);
            var filter = ToFilter(save.LootFilter, warnings);
            var state = new GameState(character, progress, filter);

            OfflineCheckpoint? offline = null;
            if (save.Offline != null)
            {
                offline = ToCheckpoint(save.Offline, save.Character, content, warnings);
            }

            state.Restore(offline, save.ClaimedOfflineSessions);
            return new LoadedGame(state, warnings);
        }

        // ---- to save ----

        private static LoadoutSave ToLoadout(PlayerCharacter c)
        {
            var skills = c.TriggeredSkills.Append(c.PrimarySkill);
            return new LoadoutSave
            {
                ClassId = c.Class.Id,
                PassiveTreeId = c.Passives.Tree.Id,
                PassivePoints = c.Passives.TotalPoints,
                AllocatedPassives = c.Passives.Allocated.Where(id => id != c.Passives.Tree.StartNodeId).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Equipment = c.Equipment.Items.ToDictionary(kv => kv.Key.ToString(), kv => ToSave(kv.Value)),
                PrimarySkillId = c.PrimarySkill.Id,
                TriggeredSkillIds = c.TriggeredSkills.Select(s => s.Id).ToList(),
                Supports = skills.Where(s => c.SupportsFor(s).Count > 0)
                    .ToDictionary(s => s.Id, s => c.SupportsFor(s).Select(x => x.Id).ToList()),
            };
        }

        private static ItemSave ToSave(Item item) => new ItemSave
        {
            Id = item.Id,
            BaseId = item.Base.Id,
            Rarity = item.Rarity.ToString(),
            ItemLevel = item.ItemLevel,
            UniqueId = item.Unique?.Id,
            Affixes = item.Affixes.Select(a => new AffixSave { AffixId = a.Affix.Id, Tier = a.Tier, Value = a.Value }).ToList(),
        };

        private static LootFilterSave ToSave(LootFilter f) => new LootFilterSave
        {
            Name = f.Name,
            DefaultAction = f.DefaultAction.ToString(),
            Rules = f.Rules.Select(r => new LootFilterRuleSave
            {
                Name = r.Name,
                Action = r.Action.ToString(),
                MinRarity = r.MinRarity?.ToString(),
                MaxRarity = r.MaxRarity?.ToString(),
                Classes = r.Classes.Select(c => c.ToString()).ToList(),
                MinItemLevel = r.MinItemLevel,
                WantedStats = r.WantedStats.Select(s => s.ToString()).ToList(),
                MinMatchingAffixes = r.MinMatchingAffixes,
                MaxMatchingAffixes = r.MaxMatchingAffixes,
            }).ToList(),
        };

        private static OfflineSessionSave ToSave(OfflineCheckpoint cp, PlayerCharacter current) => new OfflineSessionSave
        {
            SessionId = cp.SessionId,
            MapId = cp.Map.Map.Id,
            MapModifierIds = cp.Map.Modifiers.Select(m => m.Id).ToList(),
            StartedAtUtc = cp.StartedAtUtc,
            Seed = cp.Seed.ToString(CultureInfo.InvariantCulture),

            // The frozen loadout from the session start; never the current gear.
            Loadout = ToLoadout(cp.Loadout ?? throw new InvalidOperationException(
                $"Offline session {cp.SessionId} has no frozen loadout; create it with OfflineCheckpoint.Start.")),
        };

        // ---- from save ----

        private static PlayerCharacter ToCharacter(CharacterSave save, ContentRegistry content, List<string> warnings)
        {
            var character = BuildCharacter(save.Id, save.Name, save.Loadout, Math.Max(1, save.InventoryCapacity), content, warnings);
            foreach (var itemSave in save.Inventory)
            {
                var item = ToItem(itemSave, content, warnings);
                if (item != null && !character.Inventory.TryAdd(item))
                    warnings.Add($"Item {itemSave.Id} did not fit in the bag and was dropped.");
            }

            return character;
        }

        private static PlayerCharacter BuildCharacter(string id, string name, LoadoutSave l, int capacity, ContentRegistry content, List<string> warnings)
        {
            var cls = content.Class(l.ClassId) ?? throw new SaveLoadException(SaveLoadError.MissingContent, $"Unknown class {l.ClassId}.");
            var tree = content.PassiveTree(l.PassiveTreeId) ?? throw new SaveLoadException(SaveLoadError.MissingContent, $"Unknown passive tree {l.PassiveTreeId}.");

            var passives = PassiveAllocation.FromSet(tree, l.PassivePoints, l.AllocatedPassives, out var dropped);
            foreach (var d in dropped) warnings.Add($"Passive {d} could not be restored; its point was refunded.");

            var character = new PlayerCharacter(id, name, cls, passives, capacity)
            {
                PrimarySkill = content.Skill(l.PrimarySkillId) ?? throw new SaveLoadException(SaveLoadError.MissingContent, $"Unknown skill {l.PrimarySkillId}."),
            };

            foreach (var skillId in l.TriggeredSkillIds)
            {
                var skill = content.Skill(skillId);
                if (skill == null) warnings.Add($"Triggered skill {skillId} no longer exists and was removed.");
                else character.TriggeredSkills.Add(skill);
            }

            foreach (var kv in l.Equipment)
            {
                if (!Enum.TryParse<EquipmentSlot>(kv.Key, out var slot))
                {
                    warnings.Add($"Unknown equipment slot {kv.Key}; its item was moved to the bag.");
                    var moved = ToItem(kv.Value, content, warnings);
                    if (moved != null) character.Inventory.TryAdd(moved);
                    continue;
                }

                var item = ToItem(kv.Value, content, warnings);
                if (item == null) continue;
                if (Equipment.Fits(item, slot)) character.Equipment.Equip(item, slot);
                else
                {
                    warnings.Add($"{item.Name} no longer fits {slot}; it was moved to the bag.");
                    character.Inventory.TryAdd(item);
                }
            }

            foreach (var kv in l.Supports)
            {
                var skill = content.Skill(kv.Key);
                if (skill == null) continue; // the skill itself was already reported
                foreach (var supportId in kv.Value)
                {
                    var support = content.Support(supportId);
                    if (support == null)
                    {
                        warnings.Add($"Support {supportId} no longer exists and was unlinked.");
                        continue;
                    }

                    if (skill.Id != character.PrimarySkill.Id && character.TriggeredSkills.All(t => t.Id != skill.Id)) continue;
                    var result = character.LinkSupport(skill, support);
                    if (result != Domain.Skills.SupportCompatibility.Compatible)
                        warnings.Add($"Support {supportId} could not be linked to {skill.Id} ({result}) and was unlinked.");
                }
            }

            return character;
        }

        private static Item? ToItem(ItemSave s, ContentRegistry content, List<string> warnings)
        {
            var itemBase = content.ItemBase(s.BaseId);
            if (itemBase == null)
            {
                warnings.Add($"Item {s.Id}: base {s.BaseId} no longer exists; the item was removed.");
                return null;
            }

            if (!Enum.TryParse<ItemRarity>(s.Rarity, out var rarity))
            {
                warnings.Add($"Item {s.Id}: unknown rarity {s.Rarity}; the item was removed.");
                return null;
            }

            UniqueDefinition? unique = null;
            if (s.UniqueId != null)
            {
                unique = content.Unique(s.UniqueId);
                if (unique == null)
                {
                    warnings.Add($"Item {s.Id}: unique {s.UniqueId} no longer exists; the item was removed.");
                    return null;
                }
            }

            var affixes = new List<RolledAffix>();
            foreach (var a in s.Affixes)
            {
                var def = content.Affix(a.AffixId);
                if (def == null)
                {
                    warnings.Add($"Item {s.Id}: affix {a.AffixId} no longer exists and was removed.");
                    continue;
                }

                affixes.Add(new RolledAffix(def, a.Tier, a.Value));
            }

            return new Item { Id = s.Id, Base = itemBase, Rarity = rarity, ItemLevel = s.ItemLevel, Unique = unique, Affixes = affixes };
        }

        private static LootFilter ToFilter(LootFilterSave s, List<string> warnings)
        {
            var rules = new List<LootFilterRule>();
            foreach (var r in s.Rules)
            {
                if (!Enum.TryParse<FilterAction>(r.Action, out var action))
                {
                    warnings.Add($"Loot filter rule '{r.Name}' has an unknown action and was skipped.");
                    continue;
                }

                rules.Add(new LootFilterRule
                {
                    Name = r.Name,
                    Action = action,
                    MinRarity = ParseOrNull<ItemRarity>(r.MinRarity),
                    MaxRarity = ParseOrNull<ItemRarity>(r.MaxRarity),
                    Classes = r.Classes.Select(ParseOrNull<ItemClass>).Where(c => c.HasValue).Select(c => c!.Value).ToList(),
                    MinItemLevel = r.MinItemLevel,
                    WantedStats = r.WantedStats.Select(ParseOrNull<StatId>).Where(x => x.HasValue).Select(x => x!.Value).ToList(),
                    MinMatchingAffixes = r.MinMatchingAffixes,
                    MaxMatchingAffixes = r.MaxMatchingAffixes,
                });
            }

            return new LootFilter
            {
                Name = s.Name,
                Rules = rules,
                DefaultAction = ParseOrNull<FilterAction>(s.DefaultAction) ?? FilterAction.Show,
            };
        }

        private static OfflineCheckpoint? ToCheckpoint(OfflineSessionSave s, CharacterSave owner, ContentRegistry content, List<string> warnings)
        {
            var map = content.Map(s.MapId);
            if (map == null)
            {
                warnings.Add($"Offline session {s.SessionId}: map {s.MapId} no longer exists; the session was cancelled.");
                return null;
            }

            if (!ulong.TryParse(s.Seed, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
            {
                warnings.Add($"Offline session {s.SessionId}: invalid seed; the session was cancelled.");
                return null;
            }

            var modifiers = new List<MapModifier>();
            foreach (var id in s.MapModifierIds)
            {
                var m = content.MapModifier(id);
                if (m == null) warnings.Add($"Offline session {s.SessionId}: map modifier {id} no longer exists and was removed.");
                else modifiers.Add(m);
            }

            var frozen = BuildCharacter(owner.Id, owner.Name, s.Loadout, 1, content, warnings);
            var checkpoint = new OfflineCheckpoint
            {
                SessionId = s.SessionId,
                CharacterId = owner.Id,
                Loadout = frozen,
                Build = frozen.ToBuild(),
                Map = new MapInstance { Map = map, Modifiers = modifiers },
                StartedAtUtc = DateTime.SpecifyKind(s.StartedAtUtc, DateTimeKind.Utc),
                Seed = seed,
            };
            return checkpoint;
        }

        private static T? ParseOrNull<T>(string? value) where T : struct, Enum =>
            value != null && Enum.TryParse<T>(value, out var parsed) ? parsed : (T?)null;
    }
}
