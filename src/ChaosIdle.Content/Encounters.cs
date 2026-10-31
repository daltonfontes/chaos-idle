using ChaosIdle.Domain.Monsters;

namespace ChaosIdle.Content
{
    public static class Encounters
    {
        /// <summary>Trash pack: measures clear speed and attrition.</summary>
        public static readonly Encounter AshenRoad = new Encounter
        {
            Id = "encounter.ashen_road",
            Name = "Ashen Road",
            Enemies = new[]
            {
                Monsters.RustHound, Monsters.RustHound, Monsters.CinderWisp, Monsters.RustHound,
                Monsters.BogBrute, Monsters.CinderWisp, Monsters.RustHound, Monsters.CinderWisp,
            },
        };

        /// <summary>Single boss: measures boss kill time and survivability.</summary>
        public static readonly Encounter WardensHearth = new Encounter
        {
            Id = "encounter.wardens_hearth",
            Name = "Warden's Hearth",
            Enemies = new[] { Monsters.AshWarden },
        };

        public static readonly Encounter[] All = { AshenRoad, WardensHearth };
    }
}
