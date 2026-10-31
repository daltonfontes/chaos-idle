using ChaosIdle.Domain.Stats;
using Xunit;

namespace ChaosIdle.Tests;

public class StatBlockTests
{
    [Fact]
    public void Flat_sums_increased_sums_more_multiplies()
    {
        var stats = new StatBlock(new[]
        {
            new Modifier(StatId.MaxLife, ModifierKind.Flat, 100),
            new Modifier(StatId.MaxLife, ModifierKind.Flat, 50),
            new Modifier(StatId.MaxLife, ModifierKind.Increased, 20),
            new Modifier(StatId.MaxLife, ModifierKind.Increased, 30),
            new Modifier(StatId.MaxLife, ModifierKind.More, 10),
            new Modifier(StatId.MaxLife, ModifierKind.More, 20),
        });

        Assert.Equal(150, stats.Flat(StatId.MaxLife));
        Assert.Equal(50, stats.IncreasedPercent(StatId.MaxLife));
        Assert.Equal(1.32, stats.MoreMultiplier(StatId.MaxLife), 9);
        Assert.Equal(150 * 1.5 * 1.32, stats.Resolve(StatId.MaxLife), 9);
    }

    [Fact]
    public void Missing_stats_resolve_to_neutral_values()
    {
        var stats = new StatBlock(Array.Empty<Modifier>());
        Assert.Equal(0, stats.Resolve(StatId.Armour));
        Assert.Equal(1, stats.ScalingMultiplier(StatId.AttackSpeed));
    }

    [Fact]
    public void Modifier_order_does_not_change_result()
    {
        var mods = new[]
        {
            new Modifier(StatId.Armour, ModifierKind.More, 15),
            new Modifier(StatId.Armour, ModifierKind.Flat, 300),
            new Modifier(StatId.Armour, ModifierKind.Increased, 40),
            new Modifier(StatId.Armour, ModifierKind.More, -10),
        };
        var a = new StatBlock(mods).Resolve(StatId.Armour);
        var b = new StatBlock(mods.Reverse()).Resolve(StatId.Armour);
        Assert.Equal(a, b, 9);
    }
}
