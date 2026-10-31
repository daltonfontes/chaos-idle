using ChaosIdle.Content;
using ChaosIdle.Domain.Items;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Reporting;
using Xunit;

namespace ChaosIdle.Tests;

/// <summary>
/// Milestone 3 gate: at least two viable builds have distinct strengths and weaknesses.
/// Both builds are assembled only from passives, real item affixes/uniques, and supports.
/// </summary>
public class Milestone3GateTests
{
    private const int Runs = 60;

    private static readonly BuildScenario[] Scenarios =
    {
        new("Ashen Road", Encounters.AshenRoad),
        new("Warden's Hearth", Encounters.WardensHearth),
    };

    private static readonly Lazy<(BuildProfile Bastion, BuildProfile Lancer)> Profiles = new(() => (
        BuildComparer.Profile(ReferenceBuilds.IronBastion().ToBuild(), Scenarios, 1, Runs),
        BuildComparer.Profile(ReferenceBuilds.EmberLancer().ToBuild(), Scenarios, 1, Runs)));

    [Fact]
    public void Both_reference_builds_are_viable()
    {
        Assert.True(Profiles.Value.Bastion.IsViable(0.8));
        Assert.True(Profiles.Value.Lancer.IsViable(0.8));
    }

    [Fact]
    public void Ember_lancer_clears_trash_faster()
    {
        var (bastion, lancer) = Profiles.Value;
        Assert.True(lancer["Ashen Road"].MedianClearTime * 1.4 < bastion["Ashen Road"].MedianClearTime);
    }

    [Fact]
    public void Iron_bastion_is_safer_against_the_boss()
    {
        var (bastion, lancer) = Profiles.Value;
        Assert.True(bastion["Warden's Hearth"].MeanFinalLifeFraction > lancer["Warden's Hearth"].MeanFinalLifeFraction + 0.15);
        Assert.True(bastion["Warden's Hearth"].MeanDamageTakenPerSecond < lancer["Warden's Hearth"].MeanDamageTakenPerSecond);
    }

    [Fact]
    public void Each_build_has_strengths_the_other_lacks()
    {
        var (bastion, lancer) = Profiles.Value;
        var contrasts = BuildComparer.Compare(bastion, lancer);
        Assert.Contains(contrasts, c => c.Leader == bastion.BuildId);
        Assert.Contains(contrasts, c => c.Leader == lancer.BuildId);
        Assert.Contains(contrasts, c => c.Metric == "Mana" && c.Leader == bastion.BuildId);

        var lines = ReportText.BuildComparison(bastion, lancer, contrasts);
        Assert.Contains(lines, l => l.StartsWith("Iron Bastion:"));
        Assert.Contains(lines, l => l.StartsWith("Ember Lancer:"));
    }

    [Fact]
    public void A_build_compared_with_itself_has_no_contrasts()
    {
        Assert.Empty(BuildComparer.Compare(Profiles.Value.Bastion, Profiles.Value.Bastion));
    }

    [Fact]
    public void Reference_builds_use_the_real_buildcraft_systems()
    {
        foreach (var c in new[] { ReferenceBuilds.IronBastion(), ReferenceBuilds.EmberLancer() })
        {
            var build = c.ToBuild();
            Assert.Empty(build.Validate());
            Assert.Equal(ReferenceBuilds.PassivePoints, c.Passives.PointsSpent);
            Assert.Equal(8, c.Equipment.Items.Count);
            Assert.True(build.SkillSupports.Values.Sum(s => s.Count) >= 3);
            Assert.All(c.Equipment.Items.Values, i => Assert.True(i.Rarity is ItemRarity.Rare or ItemRarity.Unique));
            Assert.All(c.Equipment.Items.Values.Where(i => i.Rarity == ItemRarity.Rare), i =>
                Assert.All(i.Affixes, a => Assert.Contains(i.Class, a.Affix.AllowedClasses)));
        }

        Assert.Contains(ReferenceBuilds.EmberLancer().Passives.Allocated, id => PassiveTrees.Guardian[id].Kind == PassiveNodeKind.Keystone);
    }
}
