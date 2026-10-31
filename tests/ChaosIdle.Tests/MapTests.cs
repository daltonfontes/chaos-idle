using ChaosIdle.Content;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Maps;
using ChaosIdle.Domain.Stats;
using ChaosIdle.Simulation.Loot;
using ChaosIdle.Simulation.Random;
using Xunit;

namespace ChaosIdle.Tests;

public class MapTests
{
    private static MapInstance With(params MapModifier[] mods) => new() { Map = Maps.SunkenCauseway, Modifiers = mods };

    [Fact]
    public void Enemy_modifiers_transform_enemies()
    {
        var m = With(MapModifiers.Hardened, MapModifiers.Savage, MapModifiers.Frenzied, MapModifiers.Ironclad, MapModifiers.Warded);
        var brute = Monsters.BogBrute;
        var e = m.Apply(brute);

        Assert.Equal(brute.MaxLife * 1.4, e.MaxLife, 6);
        Assert.Equal(brute.ActionTimeSeconds / 1.25, e.ActionTimeSeconds, 6);
        Assert.Equal(brute.Armour + 600, e.Armour);
        Assert.Equal(brute.FireResistance + 25, e.FireResistance);
        Assert.Equal(brute.Attacks[0].Damage[0].Max * 1.25, e.Attacks[0].Damage[0].Max, 6);
        Assert.Equal(brute.MaxLife, Monsters.BogBrute.MaxLife); // content untouched
    }

    [Fact]
    public void Extra_damage_modifier_adds_an_element_to_every_attack()
    {
        var e = With(MapModifiers.Frostbitten).Apply(Monsters.BogBrute);
        var slam = e.Attacks[0];
        var cold = Assert.Single(slam.Damage, d => d.Type == DamageType.Cold);
        Assert.Equal(55 * 0.3, cold.Max, 6);
    }

    [Fact]
    public void Player_modifiers_apply_only_inside_the_map()
    {
        var build = ReferenceBuilds.IronBastion().ToBuild();
        var inMap = With(MapModifiers.SearingAir).ApplyTo(build);
        Assert.Equal(build.ComputeStats().Flat(StatId.FireResistance) - 25, inMap.ComputeStats().Flat(StatId.FireResistance));
        Assert.NotSame(build.Modifiers, inMap.Modifiers);
    }

    [Fact]
    public void Boss_is_optional_in_the_expedition()
    {
        var m = With();
        Assert.Equal(2, m.ToExpedition(includeBoss: false).Encounters.Count);
        Assert.Equal(3, m.ToExpedition(includeBoss: true).Encounters.Count);
        Assert.Equal(Maps.SunkenCauseway.AreaLevel, m.ToExpedition(true).AreaLevel);
    }

    [Fact]
    public void Map_instances_validate_modifier_rules()
    {
        Assert.Empty(With(MapModifiers.Hardened, MapModifiers.Savage).Validate());
        Assert.NotEmpty(With(MapModifiers.Hardened, MapModifiers.Hardened).Validate());
        Assert.NotEmpty(With(MapModifiers.Hardened, MapModifiers.Savage, MapModifiers.Frenzied, MapModifiers.Warded).Validate());
    }

    [Fact]
    public void Map_content_has_three_tiers_three_bosses_and_risk_reward_modifiers()
    {
        Assert.Equal(new[] { 1, 2, 3 }, Maps.All.Select(m => m.Tier));
        Assert.True(Maps.All.Select(m => m.AreaLevel).SequenceEqual(Maps.All.Select(m => m.AreaLevel).OrderBy(x => x)));
        Assert.All(Maps.All, m => Assert.True(m.Boss.Enemies.Single().IsBoss));
        Assert.Equal(3, Maps.All.Select(m => m.Boss.Enemies[0].Id).Distinct().Count());
        Assert.All(Maps.All.SelectMany(m => m.Packs.SelectMany(p => p.Enemies).Concat(m.Boss.Enemies)), e => Assert.Empty(e.Validate()));

        Assert.All(MapModifiers.All, m =>
        {
            bool risk = m.EnemyLifeMore > 0 || m.EnemyDamageMore > 0 || m.EnemySpeedMore > 0 || m.ExtraDamagePercent > 0
                        || m.EnemyArmour > 0 || m.EnemyElementalResistance > 0 || m.PlayerModifiers.Count > 0;
            bool reward = m.ItemQuantityPercent > 0 || m.ItemRarityPercent > 0;
            Assert.True(risk && reward, m.Id);
        });

        // With three modifiers per map: 3 maps x C(8,3) combinations, far beyond the 20-variant target.
        Assert.True(Maps.All.Length * 56 >= 20);
    }

    [Fact]
    public void Quantity_bonus_raises_drops_and_can_exceed_one_per_kill()
    {
        var gen = new LootGenerator(ItemBases.All, Affixes.All);
        var rng = new SplitMix64Random(1);
        var hound = Monsters.RustHound with { ItemDropChance = 0.5 };
        int plain = Enumerable.Range(0, 4000).Sum(_ => gen.RollDrops(hound, 5, rng).Count);
        int doubled = Enumerable.Range(0, 4000).Sum(_ => gen.RollDrops(hound, 5, rng, new LootBonus(100, 0)).Count);
        Assert.InRange(plain / 4000.0, 0.46, 0.54);
        Assert.InRange(doubled / 4000.0, 0.99, 1.01); // 100% chance -> exactly one

        var triple = Enumerable.Range(0, 1000).Select(_ => gen.RollDrops(hound, 5, rng, new LootBonus(250, 0)).Count).ToList();
        Assert.All(triple, n => Assert.InRange(n, 1, 2));
        Assert.InRange(triple.Average(), 1.7, 1.8); // 0.5 x 3.5 = 1.75
    }

    [Fact]
    public void Rarity_bonus_raises_the_share_of_rares()
    {
        var gen = new LootGenerator(ItemBases.All, Affixes.All);
        var rng = new SplitMix64Random(2);
        int plain = Enumerable.Range(0, 10000).Count(_ => gen.RollRarity(false, rng) == ChaosIdle.Domain.Items.ItemRarity.Rare);
        int bonus = Enumerable.Range(0, 10000).Count(_ => gen.RollRarity(false, rng, 100) == ChaosIdle.Domain.Items.ItemRarity.Rare);
        Assert.True(bonus > plain * 1.5);
    }
}

public class MapProgressTests
{
    [Fact]
    public void Only_the_highest_tier_boss_unlocks_the_next_tier()
    {
        var p = new MapProgress();
        Assert.True(p.CanRun(Maps.SunkenCauseway));
        Assert.False(p.CanRun(Maps.AshenTrail));

        Assert.True(p.RecordActiveBossKill(Maps.SunkenCauseway));
        Assert.True(p.CanRun(Maps.AshenTrail));
        Assert.False(p.CanRun(Maps.RimewoodHollow));

        Assert.False(p.RecordActiveBossKill(Maps.SunkenCauseway)); // repeat kill
        Assert.Equal(2, p.HighestUnlockedTier);
        Assert.Contains(Maps.SunkenCauseway.Id, p.BossesDefeated);
    }
}
