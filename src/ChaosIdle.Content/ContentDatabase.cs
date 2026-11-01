using ChaosIdle.Domain;

namespace ChaosIdle.Content
{
    public static class ContentDatabase
    {
        /// <summary>All shipped content, indexed by stable ID.</summary>
        public static readonly ContentRegistry Default = new ContentRegistry(
            new[] { Classes.Guardian },
            Skills.All,
            Supports.All,
            ItemBases.All,
            Affixes.All,
            Uniques.All,
            new[] { PassiveTrees.Guardian },
            Maps.All,
            MapModifiers.All);
    }
}
