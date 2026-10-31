using ChaosIdle.Domain.Expeditions;

namespace ChaosIdle.Content
{
    public static class Expeditions
    {
        /// <summary>Short, safe trash run for a fresh character.</summary>
        public static readonly ExpeditionDefinition RoadPatrol = new()
        {
            Id = "expedition.road_patrol",
            Name = "Road Patrol",
            AreaLevel = 3,
            Encounters = new[] { Encounters.AshenRoad },
        };

        /// <summary>Trash then the Ash Warden: needs real gear and passives.</summary>
        public static readonly ExpeditionDefinition AshenTrail = new()
        {
            Id = "expedition.ashen_trail",
            Name = "The Ashen Trail",
            AreaLevel = 8,
            Encounters = new[] { Encounters.AshenRoad, Encounters.AshenRoad, Encounters.WardensHearth },
        };

        public static readonly ExpeditionDefinition[] All = { RoadPatrol, AshenTrail };
    }
}
