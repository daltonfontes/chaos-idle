using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Content
{
    /// <summary>
    /// The shared passive tree ("the Wayfinder's Wheel"): one round tree for every class.
    ///
    /// Layout (original to this game):
    /// - Three class starts near the centre, 120° apart: Guardian (210°), Arcanist (90°), Hunter (330°).
    /// - An inner ring of 12 road nodes; each start touches the three ring nodes in front of it.
    /// - From every ring node a spoke leads out to a themed cluster: four minor nodes around a notable.
    /// - Bridges join neighbouring clusters, so there is more than one way around.
    /// - Six keystones sit on the rim, each behind one cluster.
    /// Road nodes give small stats that follow the region: life near the Guardian, mana near the
    /// Arcanist, accuracy and speed near the Hunter. Going far from your start costs points.
    /// </summary>
    public static class PassiveTrees
    {
        public const string WorldId = "tree.world";

        private const double StartRadius = 170, RingRadius = 380, SpokeRadius = 520, ClusterRadius = 690, MinorOffset = 80,
            KeystoneTravelRadius = 850, KeystoneRadius = 960;

        private sealed record Theme(string Key, string NotableName, string NotableText, Modifier[] Notable,
            string MinorAName, string MinorAText, Modifier[] MinorA, string MinorBName, string MinorBText, Modifier[] MinorB);

        private sealed record KeystoneDef(int Angle, string Id, string Name, string Text, Modifier[] Mods);

        private static readonly Dictionary<int, Theme> Themes = new()
        {
            [0] = new("precision", "Deadeye Calm", "+150 to Accuracy, 20% increased Critical Chance", new[] { M(StatId.Accuracy, ModifierKind.Flat, 150), M(StatId.CriticalChance, ModifierKind.Increased, 20) },
                "Steady Aim", "+60 to Accuracy", new[] { M(StatId.Accuracy, ModifierKind.Flat, 60) },
                "Quick Hands", "5% increased Attack Speed", new[] { M(StatId.AttackSpeed, ModifierKind.Increased, 5) }),
            [30] = new("storm", "Stormcaller", "25% increased Lightning Damage, Penetrate 10% Elemental Resistances", new[] { M(StatId.LightningDamage, ModifierKind.Increased, 25), M(StatId.ElementalPenetration, ModifierKind.Flat, 10) },
                "Spark", "12% increased Lightning Damage", new[] { M(StatId.LightningDamage, ModifierKind.Increased, 12) },
                "Grounding", "+12% to Lightning Resistance", new[] { M(StatId.LightningResistance, ModifierKind.Flat, 12) }),
            [60] = new("insight", "Searing Focus", "40% increased Critical Chance, +20% to Critical Damage Multiplier", new[] { M(StatId.CriticalChance, ModifierKind.Increased, 40), M(StatId.CriticalMultiplier, ModifierKind.Flat, 20) },
                "Keen Mind", "20% increased Critical Chance", new[] { M(StatId.CriticalChance, ModifierKind.Increased, 20) },
                "Cruel Edge", "+10% to Critical Damage Multiplier", new[] { M(StatId.CriticalMultiplier, ModifierKind.Flat, 10) }),
            [90] = new("spellcraft", "Kindled Mind", "30% increased Spell Damage, 6% increased Cast Speed", new[] { M(StatId.SpellDamage, ModifierKind.Increased, 30), M(StatId.CastSpeed, ModifierKind.Increased, 6) },
                "Kindling", "15% increased Spell Damage", new[] { M(StatId.SpellDamage, ModifierKind.Increased, 15) },
                "Swift Words", "5% increased Cast Speed", new[] { M(StatId.CastSpeed, ModifierKind.Increased, 5) }),
            [120] = new("hearth", "Hearthfire", "30% increased Fire Damage, 10% increased Elemental Damage", new[] { M(StatId.FireDamage, ModifierKind.Increased, 30), M(StatId.ElementalDamage, ModifierKind.Increased, 10) },
                "Ember", "15% increased Fire Damage", new[] { M(StatId.FireDamage, ModifierKind.Increased, 15) },
                "Deep Well", "+15 to maximum Mana, 20% increased Mana Regeneration", new[] { M(StatId.MaxMana, ModifierKind.Flat, 15), M(StatId.ManaRegenPerSecond, ModifierKind.Increased, 20) }),
            [150] = new("wards", "Warded Soul", "+8% to Fire, Cold and Lightning Resistance", new[] { M(StatId.FireResistance, ModifierKind.Flat, 8), M(StatId.ColdResistance, ModifierKind.Flat, 8), M(StatId.LightningResistance, ModifierKind.Flat, 8) },
                "Emberward", "+12% to Fire Resistance", new[] { M(StatId.FireResistance, ModifierKind.Flat, 12) },
                "Frostward", "+12% to Cold Resistance", new[] { M(StatId.ColdResistance, ModifierKind.Flat, 12) }),
            [180] = new("forge", "Forge Temper", "25% of Physical Damage converted to Fire, 15% increased Fire Damage", new[] { M(StatId.PhysicalToFireConversion, ModifierKind.Flat, 25), M(StatId.FireDamage, ModifierKind.Increased, 15) },
                "Hot Iron", "Adds 4 Fire Damage", new[] { M(StatId.FireDamage, ModifierKind.Flat, 4) },
                "Tempered", "12% increased Physical Damage", new[] { M(StatId.PhysicalDamage, ModifierKind.Increased, 12) }),
            [210] = new("vitality", "Unbroken", "15% increased maximum Life, Regenerate 4 Life per second", new[] { M(StatId.MaxLife, ModifierKind.Increased, 15), M(StatId.LifeRegenPerSecond, ModifierKind.Flat, 4) },
                "Sturdy Frame", "8% increased maximum Life", new[] { M(StatId.MaxLife, ModifierKind.Increased, 8) },
                "Mending", "Regenerate 2 Life per second", new[] { M(StatId.LifeRegenPerSecond, ModifierKind.Flat, 2) }),
            [240] = new("bulwark", "Shieldwall", "+8% chance to Block, Gain 20 Life when you Block", new[] { M(StatId.BlockChance, ModifierKind.Flat, 8), M(StatId.LifeOnBlock, ModifierKind.Flat, 20) },
                "Raised Guard", "+5% chance to Block", new[] { M(StatId.BlockChance, ModifierKind.Flat, 5) },
                "Steadfast", "Gain 8 Life when you Block", new[] { M(StatId.LifeOnBlock, ModifierKind.Flat, 8) }),
            [270] = new("iron", "Ironclad", "+150 to Armour, 30% increased Armour", new[] { M(StatId.Armour, ModifierKind.Flat, 150), M(StatId.Armour, ModifierKind.Increased, 30) },
                "Iron Skin", "25% increased Armour", new[] { M(StatId.Armour, ModifierKind.Increased, 25) },
                "Heavy Hands", "15% increased Physical Damage", new[] { M(StatId.PhysicalDamage, ModifierKind.Increased, 15) }),
            [300] = new("bloodletting", "Answering Steel", "20% increased Physical Damage, 10% increased Attack Speed", new[] { M(StatId.PhysicalDamage, ModifierKind.Increased, 20), M(StatId.AttackSpeed, ModifierKind.Increased, 10) },
                "Butcher's Weight", "15% increased Attack Damage", new[] { M(StatId.AttackDamage, ModifierKind.Increased, 15) },
                "Bloodthirst", "0.5% of damage dealt leeched as Life", new[] { M(StatId.LifeLeechPercent, ModifierKind.Flat, 0.5) }),
            [330] = new("swiftness", "Wind Step", "10% increased Attack Speed, +200 to Evasion", new[] { M(StatId.AttackSpeed, ModifierKind.Increased, 10), M(StatId.Evasion, ModifierKind.Flat, 200) },
                "Light Feet", "+80 to Evasion", new[] { M(StatId.Evasion, ModifierKind.Flat, 80) },
                "Quick Hands", "5% increased Attack Speed", new[] { M(StatId.AttackSpeed, ModifierKind.Increased, 5) }),
        };

        private static readonly KeystoneDef[] Keystones =
        {
            new(30, "pas.key.steady", "Steady Hand", "You cannot deal Critical Strikes. 30% more Damage.",
                new[] { M(StatId.CriticalChance, ModifierKind.More, -100), M(StatId.Damage, ModifierKind.More, 30) }),
            new(90, "pas.key.overcharge", "Overcharge", "40% more Spell Damage. 50% more Mana Cost.",
                new[] { M(StatId.SpellDamage, ModifierKind.More, 40), M(StatId.ManaCost, ModifierKind.More, 50) }),
            new(150, "pas.key.unhurried", "Unhurried Mind", "60% more Mana Regeneration. 15% less Cast Speed.",
                new[] { M(StatId.ManaRegenPerSecond, ModifierKind.More, 60), M(StatId.CastSpeed, ModifierKind.More, -15) }),
            new(180, "pas.key.ember", "Ember Covenant", "All Physical Damage is converted to Fire. 20% less Armour.",
                new[] { M(StatId.PhysicalToFireConversion, ModifierKind.Flat, 100), M(StatId.Armour, ModifierKind.More, -20) }),
            new(240, "pas.key.bastion", "Unyielding Bastion", "Your chance to Block also applies to Spells. 20% less maximum Life.",
                new[] { M(StatId.BlockAppliesToSpells, ModifierKind.Flat, 1), M(StatId.MaxLife, ModifierKind.More, -20) }),
            new(300, "pas.key.glass", "Glass Focus", "30% more Damage. 25% less maximum Life.",
                new[] { M(StatId.Damage, ModifierKind.More, 30), M(StatId.MaxLife, ModifierKind.More, -25) }),
        };

        // Declared after Themes and Keystones: static fields initialise in order.
        public static readonly PassiveTreeDefinition World = Build();

        /// <summary>Cluster node ids: notable, and minors 1 (facing the centre), 2 (counter-clockwise side), 3 (facing out), 4 (clockwise side).</summary>
        public static string Notable(string theme) => $"pas.{theme}.notable";
        public static string Minor(string theme, int index) => $"pas.{theme}.{index}";

        private static PassiveTreeDefinition Build()
        {
            var nodes = new List<PassiveNode>();
            var links = new List<(string, string)>();
            void Link(string a, string b) => links.Add((a, b));

            // Class starts.
            foreach (var (classId, name, angle) in new[] { ("class.guardian", "Oathstone", 210), ("class.arcanist", "Emberglass", 90), ("class.hunter", "Wildmark", 330) })
            {
                string id = "pas.start." + classId.Substring("class.".Length);
                nodes.Add(Node(id, name, "Where this class begins.", PassiveNodeKind.Start, Polar(angle, StartRadius)) with { ClassId = classId });
                foreach (int d in new[] { -30, 0, 30 }) Link(id, Ring(Norm(angle + d)));
            }

            // Inner ring and spokes.
            for (int a = 0; a < 360; a += 30)
            {
                nodes.Add(Road(Ring(a), a, Polar(a, RingRadius)));
                Link(Ring(a), Ring(Norm(a + 30)));
                nodes.Add(Road($"pas.spoke.{a}", a, Polar(a, SpokeRadius)));
                Link(Ring(a), $"pas.spoke.{a}");
            }

            // Clusters.
            foreach (var kv in Themes)
            {
                int a = kv.Key;
                var t = kv.Value;
                var c = Polar(a, ClusterRadius);
                nodes.Add(Node(Notable(t.Key), t.NotableName, t.NotableText, PassiveNodeKind.Notable, c, t.Notable));
                for (int i = 1; i <= 4; i++)
                {
                    double local = a + 180 - 90 * (i - 1); // 1: inward, 2: counter-clockwise side, 3: outward, 4: clockwise side
                    var p = (c.X + MinorOffset * Math.Cos(Rad(local)), c.Y - MinorOffset * Math.Sin(Rad(local)));
                    bool isA = i % 2 == 1;
                    nodes.Add(Node(Minor(t.Key, i), isA ? t.MinorAName : t.MinorBName, isA ? t.MinorAText : t.MinorBText,
                        PassiveNodeKind.Minor, p, isA ? t.MinorA : t.MinorB));
                }

                Link($"pas.spoke.{a}", Minor(t.Key, 1));
                Link(Minor(t.Key, 1), Minor(t.Key, 2));
                Link(Minor(t.Key, 2), Minor(t.Key, 3));
                Link(Minor(t.Key, 3), Minor(t.Key, 4));
                Link(Minor(t.Key, 4), Minor(t.Key, 1));
                Link(Minor(t.Key, 2), Notable(t.Key));
                Link(Minor(t.Key, 4), Notable(t.Key));
            }

            // Bridges between neighbouring clusters (counter-clockwise side of one to clockwise side of the next).
            for (int a = 0; a < 360; a += 30)
            {
                string id = $"pas.bridge.{a + 15}";
                nodes.Add(Road(id, a + 15, Polar(a + 15, ClusterRadius + 20)));
                Link(Minor(Themes[a].Key, 2), id);
                Link(id, Minor(Themes[Norm(a + 30)].Key, 4));
            }

            // Keystones on the rim, behind a cluster's outward minor.
            foreach (var k in Keystones)
            {
                string travel = $"pas.reach.{k.Angle}";
                nodes.Add(Road(travel, k.Angle, Polar(k.Angle, KeystoneTravelRadius)));
                nodes.Add(Node(k.Id, k.Name, k.Text, PassiveNodeKind.Keystone, Polar(k.Angle, KeystoneRadius), k.Mods));
                Link(Minor(Themes[k.Angle].Key, 3), travel);
                Link(travel, k.Id);
            }

            return new PassiveTreeDefinition(WorldId, nodes, links);
        }

        private static string Ring(int angle) => $"pas.ring.{angle}";

        /// <summary>Road nodes: small stats that follow the region of the tree.</summary>
        private static PassiveNode Road(string id, double angle, (double X, double Y) p)
        {
            double a = ((angle % 360) + 360) % 360;
            if (a >= 150 && a < 270) return Node(id, "Vigour", "+10 to maximum Life", PassiveNodeKind.Minor, p, M(StatId.MaxLife, ModifierKind.Flat, 10));
            if (a >= 30 && a < 150) return Node(id, "Focus", "+8 to maximum Mana, 8% increased Mana Regeneration", PassiveNodeKind.Minor, p,
                M(StatId.MaxMana, ModifierKind.Flat, 8), M(StatId.ManaRegenPerSecond, ModifierKind.Increased, 8));
            return Node(id, "Poise", "+50 to Accuracy, 3% increased Attack Speed", PassiveNodeKind.Minor, p,
                M(StatId.Accuracy, ModifierKind.Flat, 50), M(StatId.AttackSpeed, ModifierKind.Increased, 3));
        }

        private static PassiveNode Node(string id, string name, string description, PassiveNodeKind kind, (double X, double Y) p, params Modifier[] modifiers) => new()
        {
            Id = id, Name = name, Description = description, Kind = kind, Modifiers = modifiers, X = Math.Round(p.X, 1), Y = Math.Round(p.Y, 1),
        };

        /// <summary>Screen-style coordinates: 0° points right, angles grow counter-clockwise (y up is negative).</summary>
        private static (double X, double Y) Polar(double angleDeg, double radius) => (radius * Math.Cos(Rad(angleDeg)), -radius * Math.Sin(Rad(angleDeg)));

        private static double Rad(double deg) => deg * Math.PI / 180.0;

        private static int Norm(int angle) => ((angle % 360) + 360) % 360;

        private static Modifier M(StatId stat, ModifierKind kind, double value) => new(stat, kind, value);
    }
}
