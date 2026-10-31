using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Content
{
    /// <summary>
    /// The Guardian's starting tree: four branches (defence, melee, spell, wards) from one start
    /// node, with two keystones that bend a rule at a real cost.
    /// </summary>
    public static class PassiveTrees
    {
        public static readonly PassiveTreeDefinition Guardian = new(
            "tree.guardian",
            new[]
            {
                Node("pas.oathstone", "Oathstone", "Where every Guardian begins.", PassiveNodeKind.Start),

                // Defence
                Node("pas.def.frame1", "Sturdy Frame", "8% increased maximum Life", PassiveNodeKind.Minor, M(StatId.MaxLife, ModifierKind.Increased, 8)),
                Node("pas.def.skin1", "Iron Skin", "25% increased Armour", PassiveNodeKind.Minor, M(StatId.Armour, ModifierKind.Increased, 25)),
                Node("pas.def.skin2", "Iron Skin", "+100 Armour, 25% increased Armour", PassiveNodeKind.Minor, M(StatId.Armour, ModifierKind.Flat, 100), M(StatId.Armour, ModifierKind.Increased, 25)),
                Node("pas.def.frame2", "Sturdy Frame", "8% increased maximum Life", PassiveNodeKind.Minor, M(StatId.MaxLife, ModifierKind.Increased, 8)),
                Node("pas.def.unbroken", "Unbroken", "15% increased maximum Life, Regenerate 4 Life per second", PassiveNodeKind.Notable, M(StatId.MaxLife, ModifierKind.Increased, 15), M(StatId.LifeRegenPerSecond, ModifierKind.Flat, 4)),
                Node("pas.def.guard1", "Raised Guard", "+5% chance to Block", PassiveNodeKind.Minor, M(StatId.BlockChance, ModifierKind.Flat, 5)),
                Node("pas.def.guard2", "Raised Guard", "+5% chance to Block", PassiveNodeKind.Minor, M(StatId.BlockChance, ModifierKind.Flat, 5)),
                Node("pas.def.shieldwall", "Shieldwall", "+8% chance to Block, Gain 20 Life when you Block", PassiveNodeKind.Notable, M(StatId.BlockChance, ModifierKind.Flat, 8), M(StatId.LifeOnBlock, ModifierKind.Flat, 20)),
                Node("pas.key.bastion", "Unyielding Bastion", "Your chance to Block also applies to Spells. 20% less maximum Life.", PassiveNodeKind.Keystone,
                    M(StatId.BlockAppliesToSpells, ModifierKind.Flat, 1), M(StatId.MaxLife, ModifierKind.More, -20)),

                // Melee
                Node("pas.mel.hands1", "Heavy Hands", "15% increased Physical Damage", PassiveNodeKind.Minor, M(StatId.PhysicalDamage, ModifierKind.Increased, 15)),
                Node("pas.mel.eye", "Keen Eye", "+150 to Accuracy", PassiveNodeKind.Minor, M(StatId.Accuracy, ModifierKind.Flat, 150)),
                Node("pas.mel.swing", "Quick Swings", "6% increased Attack Speed", PassiveNodeKind.Minor, M(StatId.AttackSpeed, ModifierKind.Increased, 6)),
                Node("pas.mel.hands2", "Heavy Hands", "15% increased Physical Damage", PassiveNodeKind.Minor, M(StatId.PhysicalDamage, ModifierKind.Increased, 15)),
                Node("pas.mel.answer", "Answering Steel", "20% increased Physical Damage, 10% increased Attack Speed", PassiveNodeKind.Notable, M(StatId.PhysicalDamage, ModifierKind.Increased, 20), M(StatId.AttackSpeed, ModifierKind.Increased, 10)),

                // Spell
                Node("pas.spl.kindling", "Kindling", "15% increased Spell Damage", PassiveNodeKind.Minor, M(StatId.SpellDamage, ModifierKind.Increased, 15)),
                Node("pas.spl.hearth", "Hearthfire", "15% increased Fire Damage", PassiveNodeKind.Minor, M(StatId.FireDamage, ModifierKind.Increased, 15)),
                Node("pas.spl.well", "Deep Well", "+20 to maximum Mana, 25% increased Mana Regeneration", PassiveNodeKind.Minor, M(StatId.MaxMana, ModifierKind.Flat, 20), M(StatId.ManaRegenPerSecond, ModifierKind.Increased, 25)),
                Node("pas.spl.words", "Swift Words", "6% increased Cast Speed", PassiveNodeKind.Minor, M(StatId.CastSpeed, ModifierKind.Increased, 6)),
                Node("pas.spl.pyremind", "Pyre Mind", "25% increased Spell Damage, 30% increased Critical Chance", PassiveNodeKind.Notable, M(StatId.SpellDamage, ModifierKind.Increased, 25), M(StatId.CriticalChance, ModifierKind.Increased, 30)),
                Node("pas.spl.focus", "Searing Focus", "40% increased Critical Chance, +20% to Critical Damage Multiplier", PassiveNodeKind.Minor, M(StatId.CriticalChance, ModifierKind.Increased, 40), M(StatId.CriticalMultiplier, ModifierKind.Flat, 20)),
                Node("pas.key.overcharge", "Overcharge", "40% more Spell Damage. 50% more Mana Cost.", PassiveNodeKind.Keystone,
                    M(StatId.SpellDamage, ModifierKind.More, 40), M(StatId.ManaCost, ModifierKind.More, 50)),

                // Wards
                Node("pas.wrd.ember", "Emberward", "+12% to Fire Resistance", PassiveNodeKind.Minor, M(StatId.FireResistance, ModifierKind.Flat, 12)),
                Node("pas.wrd.frost", "Frostward", "+12% to Cold Resistance", PassiveNodeKind.Minor, M(StatId.ColdResistance, ModifierKind.Flat, 12)),
                Node("pas.wrd.storm", "Stormward", "+12% to Lightning Resistance", PassiveNodeKind.Minor, M(StatId.LightningResistance, ModifierKind.Flat, 12)),
                Node("pas.wrd.soul", "Warded Soul", "+8% to Fire, Cold and Lightning Resistance", PassiveNodeKind.Notable,
                    M(StatId.FireResistance, ModifierKind.Flat, 8), M(StatId.ColdResistance, ModifierKind.Flat, 8), M(StatId.LightningResistance, ModifierKind.Flat, 8)),
            },
            new[]
            {
                ("pas.oathstone", "pas.def.frame1"),
                ("pas.def.frame1", "pas.def.skin1"), ("pas.def.skin1", "pas.def.skin2"), ("pas.def.skin2", "pas.def.unbroken"),
                ("pas.def.skin1", "pas.def.frame2"), ("pas.def.frame2", "pas.def.unbroken"),
                ("pas.def.frame1", "pas.def.guard1"), ("pas.def.guard1", "pas.def.guard2"), ("pas.def.guard2", "pas.def.shieldwall"),
                ("pas.def.shieldwall", "pas.key.bastion"),

                ("pas.oathstone", "pas.mel.hands1"), ("pas.mel.hands1", "pas.mel.eye"),
                ("pas.mel.hands1", "pas.mel.swing"), ("pas.mel.swing", "pas.mel.hands2"), ("pas.mel.hands2", "pas.mel.answer"),
                ("pas.mel.answer", "pas.def.guard2"),

                ("pas.oathstone", "pas.spl.kindling"),
                ("pas.spl.kindling", "pas.spl.hearth"), ("pas.spl.hearth", "pas.spl.pyremind"),
                ("pas.spl.kindling", "pas.spl.well"), ("pas.spl.well", "pas.spl.words"), ("pas.spl.words", "pas.spl.pyremind"),
                ("pas.spl.pyremind", "pas.spl.focus"), ("pas.spl.pyremind", "pas.key.overcharge"),

                ("pas.oathstone", "pas.wrd.ember"), ("pas.wrd.ember", "pas.wrd.frost"), ("pas.wrd.frost", "pas.wrd.storm"), ("pas.wrd.storm", "pas.wrd.soul"),
            });

        private static PassiveNode Node(string id, string name, string description, PassiveNodeKind kind, params Modifier[] modifiers) => new()
        {
            Id = id,
            Name = name,
            Description = description,
            Kind = kind,
            Modifiers = modifiers,
        };

        private static Modifier M(StatId stat, ModifierKind kind, double value) => new(stat, kind, value);
    }
}
