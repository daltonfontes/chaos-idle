using ChaosIdle.Content;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.Combat;
using ChaosIdle.Domain.Monsters;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Tests;

internal static class TestContent
{
    public static EnemyDefinition Dummy(double life = 1_000_000, params EnemyAttackDefinition[] attacks) => new()
    {
        Id = "test.dummy",
        Name = "Training Dummy",
        MaxLife = life,
        Accuracy = 1000,
        ActionTimeSeconds = 1,
        Attacks = attacks.Length > 0 ? attacks : new[] { Harmless },
    };

    public static readonly EnemyAttackDefinition Harmless = new() { Id = "test.poke", Name = "Poke" };

    public static EnemyAttackDefinition FireBolt(double amount) => new()
    {
        Id = "test.fire_bolt", Name = "Fire Bolt", IsSpell = true,
        Damage = new[] { new DamageRange(DamageType.Fire, amount, amount) },
    };

    public static Encounter Fight(params EnemyDefinition[] enemies) => new() { Id = "test.fight", Name = "Test", Enemies = enemies };

    /// <summary>Eight harmless, fragile enemies that always drop one item each.</summary>
    public static Encounter LootPinata() => Fight(Enumerable.Range(0, 8)
        .Select(i => Dummy(50) with { Id = $"test.pinata{i}", ItemDropChance = 1 }).ToArray());

    public static ChaosIdle.Domain.Characters.PlayerCharacter StarterWithPassives()
    {
        var c = StarterCharacters.NewGuardian();
        c.Passives = ChaosIdle.Domain.PassiveTree.PassiveAllocation.FromPath(
            PassiveTrees.Guardian, StarterCharacters.StartingPassivePoints, StarterCharacters.BulwarkPath);
        return c;
    }

    public static CharacterBuild With(this CharacterBuild build, params Modifier[] extra) =>
        build with { Modifiers = build.Modifiers.Concat(extra).ToArray() };
}
