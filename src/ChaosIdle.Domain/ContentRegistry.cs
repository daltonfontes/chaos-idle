using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Skills;

namespace ChaosIdle.Domain
{
    /// <summary>
    /// Looks content up by stable ID. Saves store only IDs and rolled values; this registry turns
    /// them back into definitions. Construction fails on duplicate IDs within a content type.
    /// </summary>
    public sealed class ContentRegistry
    {
        private readonly Dictionary<string, CharacterClassDefinition> _classes;
        private readonly Dictionary<string, SkillDefinition> _skills;
        private readonly Dictionary<string, SupportDefinition> _supports;
        private readonly Dictionary<string, ItemBase> _bases;
        private readonly Dictionary<string, AffixDefinition> _affixes;
        private readonly Dictionary<string, UniqueDefinition> _uniques;
        private readonly Dictionary<string, PassiveTreeDefinition> _trees;
        private readonly Dictionary<string, MapDefinition> _maps;
        private readonly Dictionary<string, MapModifier> _mapModifiers;

        public ContentRegistry(
            IEnumerable<CharacterClassDefinition> classes,
            IEnumerable<SkillDefinition> skills,
            IEnumerable<SupportDefinition> supports,
            IEnumerable<ItemBase> itemBases,
            IEnumerable<AffixDefinition> affixes,
            IEnumerable<UniqueDefinition> uniques,
            IEnumerable<PassiveTreeDefinition> passiveTrees,
            IEnumerable<MapDefinition> maps,
            IEnumerable<MapModifier> mapModifiers)
        {
            var uniqueList = uniques.ToList();
            _classes = Index(classes, x => x.Id, "class");
            _skills = Index(skills, x => x.Id, "skill");
            _supports = Index(supports, x => x.Id, "support");
            _bases = Index(itemBases, x => x.Id, "item base");
            _affixes = Index(affixes.Concat(uniqueList.SelectMany(u => u.Modifiers)), x => x.Id, "affix");
            _uniques = Index(uniqueList, x => x.Id, "unique");
            _trees = Index(passiveTrees, x => x.Id, "passive tree");
            _maps = Index(maps, x => x.Id, "map");
            _mapModifiers = Index(mapModifiers, x => x.Id, "map modifier");
        }

        public CharacterClassDefinition? Class(string id) => Get(_classes, id);
        public SkillDefinition? Skill(string id) => Get(_skills, id);
        public SupportDefinition? Support(string id) => Get(_supports, id);
        public ItemBase? ItemBase(string id) => Get(_bases, id);
        public AffixDefinition? Affix(string id) => Get(_affixes, id);
        public UniqueDefinition? Unique(string id) => Get(_uniques, id);
        public PassiveTreeDefinition? PassiveTree(string id) => Get(_trees, id);
        public MapDefinition? Map(string id) => Get(_maps, id);
        public MapModifier? MapModifier(string id) => Get(_mapModifiers, id);

        private static T? Get<T>(Dictionary<string, T> d, string? id) where T : class =>
            id != null && d.TryGetValue(id, out var v) ? v : null;

        private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> id, string kind)
        {
            var d = new Dictionary<string, T>();
            foreach (var item in items)
            {
                var key = id(item);
                if (d.ContainsKey(key)) throw new ArgumentException($"Duplicate {kind} id {key}.");
                d[key] = item;
            }

            return d;
        }
    }
}
