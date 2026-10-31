using ChaosIdle.Domain.Combat;
using Xunit;

namespace ChaosIdle.Tests;

public class CombatFormulasTests
{
    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(100, 0, 0)]
    [InlineData(800, 100, 0.5)]      // 800 / (800 + 8*100)
    [InlineData(400, 100, 1.0 / 3)]  // 400 / (400 + 800)
    public void Armour_reduction_follows_formula(double armour, double hit, double expected)
    {
        Assert.Equal(expected, CombatFormulas.ArmourReduction(armour, hit), 9);
    }

    [Fact]
    public void Armour_reduction_is_capped()
    {
        Assert.Equal(CombatFormulas.MaxArmourReduction, CombatFormulas.ArmourReduction(1_000_000, 1));
    }

    [Fact]
    public void Armour_is_weaker_against_bigger_hits()
    {
        Assert.True(CombatFormulas.ArmourReduction(1000, 20) > CombatFormulas.ArmourReduction(1000, 200));
    }

    [Theory]
    [InlineData(40, 0, 40)]
    [InlineData(90, 0, 75)]     // capped
    [InlineData(90, 20, 55)]    // cap applies before penetration
    [InlineData(10, 30, -20)]   // penetration can go below zero
    [InlineData(-80, 50, -100)] // floor
    [InlineData(40, -10, 40)]   // negative penetration is ignored
    public void Effective_resistance_caps_then_penetrates(double res, double pen, double expected)
    {
        Assert.Equal(expected, CombatFormulas.EffectiveResistance(res, pen), 9);
    }

    [Theory]
    [InlineData(500, 0, 1.0)]
    [InlineData(0, 100, 0.05)]
    [InlineData(100, 400, 0.5)]        // 100 / (100 + 400*0.25)
    [InlineData(1, 1_000_000, 0.05)]   // floor
    public void Hit_chance_follows_formula(double accuracy, double evasion, double expected)
    {
        Assert.Equal(expected, CombatFormulas.HitChance(accuracy, evasion), 9);
    }

    [Theory]
    [InlineData(40, 75, 0.40)]
    [InlineData(90, 75, 0.75)]
    [InlineData(-5, 75, 0.0)]
    public void Percent_to_chance_clamps(double percent, double cap, double expected)
    {
        Assert.Equal(expected, CombatFormulas.PercentToChance(percent, cap), 9);
    }
}
