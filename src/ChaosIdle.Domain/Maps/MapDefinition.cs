using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Expeditions;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.Maps
{
    /// <summary>
    /// A rule that makes a map harder in a specific way and pays for it with better loot.
    /// Modifiers should test a build weakness (resistances, mitigation, sustain, damage), not
    /// just inflate numbers.
    /// </summary>
    public sealed record MapModifier
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";

        public double EnemyLifeMore { get; init; }
        public double EnemyDamageMore { get; init; }

        /// <summary>Percent more action speed: action time is divided by (1 + value/100).</summary>
        public double EnemySpeedMore { get; init; }

        /// <summary>Enemy hits gain this percent of their damage as extra <see cref="ExtraDamageType"/>.</summary>
        public double ExtraDamagePercent { get; init; }
        public DamageType ExtraDamageType { get; init; } = DamageType.Cold;

        public double EnemyArmour { get; init; }
        public double EnemyElementalResistance { get; init; }

        /// <summary>Applied to the player's character stats while in the map.</summary>
        public IReadOnlyList<Modifier> PlayerModifiers { get; init; } = Array.Empty<Modifier>();

        public double ItemQuantityPercent { get; init; }
        public double ItemRarityPercent { get; init; }
    }

    /// <summary>A map layout: trash packs and a boss, at a tier and area level.</summary>
    public sealed record MapDefinition
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public int Tier { get; init; } = 1;
        public int AreaLevel { get; init; } = 1;
        public IReadOnlyList<Encounter> Packs { get; init; } = Array.Empty<Encounter>();
        public Encounter Boss { get; init; } = new Encounter();
    }

    /// <summary>A map with its rolled modifiers. Pure: produces transformed copies of content.</summary>
    public sealed record MapInstance
    {
        public const int MaxModifiers = 3;

        public MapDefinition Map { get; init; } = new MapDefinition();
        public IReadOnlyList<MapModifier> Modifiers { get; init; } = Array.Empty<MapModifier>();

        public string Name => Modifiers.Count == 0 ? Map.Name : $"{Map.Name} ({string.Join(", ", Modifiers.Select(m => m.Name))})";

        public double ItemQuantityPercent => Modifiers.Sum(m => m.ItemQuantityPercent);
        public double ItemRarityPercent => Modifiers.Sum(m => m.ItemRarityPercent);

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (Modifiers.Count > MaxModifiers) errors.Add($"{Map.Id}: at most {MaxModifiers} modifiers.");
            if (Modifiers.Select(m => m.Id).Distinct().Count() != Modifiers.Count) errors.Add($"{Map.Id}: duplicate modifiers.");
            if (Map.Packs.Count == 0) errors.Add($"{Map.Id}: no packs.");
            if (Map.Boss.Enemies.Count == 0) errors.Add($"{Map.Id}: no boss.");
            return errors;
        }

        /// <summary>Trash packs, optionally followed by the boss, with modifiers applied to every enemy.</summary>
        public ExpeditionDefinition ToExpedition(bool includeBoss)
        {
            var encounters = Map.Packs.AsEnumerable();
            if (includeBoss) encounters = encounters.Append(Map.Boss);
            return new ExpeditionDefinition
            {
                Id = Map.Id + (includeBoss ? "" : ".packs"),
                Name = Name,
                AreaLevel = Map.AreaLevel,
                Encounters = encounters.Select(Apply).ToList(),
            };
        }

        /// <summary>The build as it fights in this map (player-side modifiers added).</summary>
        public CharacterBuild ApplyTo(CharacterBuild build) =>
            build with { Modifiers = build.Modifiers.Concat(Modifiers.SelectMany(m => m.PlayerModifiers)).ToArray() };

        public Encounter Apply(Encounter encounter) =>
            encounter with { Enemies = encounter.Enemies.Select(Apply).ToList() };

        public EnemyDefinition Apply(EnemyDefinition enemy)
        {
            double life = 1, damage = 1, speed = 1, extra = 0, armour = 0, res = 0;
            var extraType = DamageType.Cold;
            foreach (var m in Modifiers)
            {
                life *= 1 + m.EnemyLifeMore / 100.0;
                damage *= 1 + m.EnemyDamageMore / 100.0;
                speed *= 1 + m.EnemySpeedMore / 100.0;
                armour += m.EnemyArmour;
                res += m.EnemyElementalResistance;
                if (m.ExtraDamagePercent > 0)
                {
                    extra += m.ExtraDamagePercent;
                    extraType = m.ExtraDamageType;
                }
            }

            return enemy with
            {
                MaxLife = enemy.MaxLife * life,
                ActionTimeSeconds = enemy.ActionTimeSeconds / speed,
                Armour = enemy.Armour + armour,
                FireResistance = enemy.FireResistance + res,
                ColdResistance = enemy.ColdResistance + res,
                LightningResistance = enemy.LightningResistance + res,
                Attacks = enemy.Attacks.Select(a =>
                {
                    var scaled = a.Damage.Select(d => d with { Min = d.Min * damage, Max = d.Max * damage }).ToList();
                    if (extra > 0)
                    {
                        double min = scaled.Sum(d => d.Min) * extra / 100.0;
                        double max = scaled.Sum(d => d.Max) * extra / 100.0;
                        scaled.Add(new DamageRange(extraType, min, max));
                    }

                    return a with { Damage = scaled };
                }).ToList(),
            };
        }
    }
}
