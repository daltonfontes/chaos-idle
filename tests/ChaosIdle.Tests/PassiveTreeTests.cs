using ChaosIdle.Content;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Stats;
using Xunit;

namespace ChaosIdle.Tests;

public class PassiveTreeTests
{
    private static PassiveTreeDefinition Tree => PassiveTrees.World;
    private static PassiveAllocation Fresh(int points = 30) => StarterCharacters.EmptyPassives(points);

    [Fact]
    public void World_tree_is_a_shared_round_tree_with_three_class_starts()
    {
        Assert.InRange(Tree.Nodes.Count, 100, 160);
        Assert.Equal(new[] { "class.guardian", "class.arcanist", "class.hunter" }, Tree.StartNodes.Select(s => s.ClassId));
        Assert.Equal(12, Tree.Nodes.Count(n => n.Kind == PassiveNodeKind.Notable));
        Assert.Equal(6, Tree.Nodes.Count(n => n.Kind == PassiveNodeKind.Keystone));
        Assert.All(Tree.Nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.Description)));
        Assert.All(Tree.Nodes.Where(n => n.Kind != PassiveNodeKind.Start), n => Assert.NotEmpty(n.Modifiers));
        Assert.Equal(Tree.Nodes.Count, Tree.Nodes.Select(n => (n.X, n.Y)).Distinct().Count()); // no two nodes on the same spot
        Assert.All(Tree.Links, l => Assert.True(Distance(l.A, l.B) < 400, $"{l.A}-{l.B} is drawn too long"));
        Assert.Equal("pas.start.guardian", Tree.StartNodeFor("class.guardian"));
    }

    [Fact]
    public void Keystones_sit_on_the_rim_and_starts_near_the_centre()
    {
        double R(PassiveNode n) => Math.Sqrt(n.X * n.X + n.Y * n.Y);
        Assert.All(Tree.Nodes.Where(n => n.Kind == PassiveNodeKind.Keystone), k => Assert.True(R(k) > 900));
        Assert.All(Tree.StartNodes, s => Assert.True(R(s) < 200));
    }

    [Fact]
    public void Empty_allocation_has_only_the_class_start()
    {
        var a = Fresh(5);
        Assert.Equal(new[] { "pas.start.guardian" }, a.Allocated);
        Assert.Equal(0, a.PointsSpent);
        Assert.Equal(5, a.PointsRemaining);
        Assert.Throws<ArgumentException>(() => PassiveAllocation.Empty(Tree, 5)); // several starts: the class must be named
    }

    [Fact]
    public void Single_nodes_must_touch_the_allocated_set()
    {
        var a = Fresh(5);
        Assert.Equal(AllocationError.None, a.CanAllocate("pas.ring.210"));
        Assert.Equal(AllocationError.NotConnected, a.CanAllocate(PassiveTrees.Notable("vitality")));
        Assert.Equal(AllocationError.UnknownNode, a.CanAllocate("pas.nope"));
        Assert.Equal(AllocationError.OtherClassStart, a.CanAllocate("pas.start.arcanist"));
        Assert.Equal(AllocationError.NoPointsLeft, PassiveAllocation.FromPath(Fresh(1), "pas.ring.210").CanAllocate("pas.spoke.210"));
    }

    [Fact]
    public void Shortest_path_is_found_priced_and_taken_in_one_go()
    {
        var a = Fresh(5);
        var path = a.PathTo(PassiveTrees.Notable("vitality"))!;
        Assert.Equal(5, path.Count);
        Assert.Equal(new[] { "pas.ring.210", "pas.spoke.210", PassiveTrees.Minor("vitality", 1) }, path.Take(3));
        Assert.Equal(PassiveTrees.Notable("vitality"), path[^1]);
        Assert.Equal(path, a.PathTo(PassiveTrees.Notable("vitality"))); // deterministic

        Assert.Equal((AllocationError.None, 5), a.CanAllocatePath(PassiveTrees.Notable("vitality")));
        var taken = a.AllocatePath(PassiveTrees.Notable("vitality"));
        Assert.Equal(5, taken.PointsSpent);
        Assert.All(path, id => Assert.True(taken.IsAllocated(id)));

        Assert.Equal((AllocationError.NotEnoughPoints, 5), Fresh(3).CanAllocatePath(PassiveTrees.Notable("vitality")));
        Assert.Equal(AllocationError.AlreadyAllocated, taken.CanAllocatePath("pas.ring.210").Error);
    }

    [Fact]
    public void Paths_never_walk_through_another_class_start()
    {
        var a = Fresh(40);
        foreach (var target in new[] { "pas.key.overcharge", PassiveTrees.Notable("swiftness"), "pas.ring.90" })
        {
            var path = a.PathTo(target)!;
            Assert.DoesNotContain(path, id => Tree.IsStart(id));
        }

        Assert.Null(a.PathTo("pas.start.hunter"));
    }

    [Fact]
    public void Far_clusters_cost_more_than_near_ones()
    {
        var a = Fresh(40);
        int near = a.PathTo(PassiveTrees.Notable("vitality"))!.Count;
        int far = a.PathTo(PassiveTrees.Notable("spellcraft"))!.Count;
        Assert.True(far > near + 2, $"near {near}, far {far}");
    }

    [Fact]
    public void Refunds_cannot_disconnect_the_tree_but_loops_allow_them()
    {
        var a = PassiveAllocation.FromTargets(Fresh(), PassiveTrees.Notable("vitality"));
        Assert.Equal(AllocationError.WouldDisconnect, a.CanRefund("pas.spoke.210"));
        Assert.Equal(AllocationError.CannotRefundStart, a.CanRefund("pas.start.guardian"));
        Assert.Equal(AllocationError.None, a.CanRefund(PassiveTrees.Notable("vitality")));

        // Close the cluster's ring: now the minor next to the notable can go.
        var loop = PassiveAllocation.FromTargets(a, PassiveTrees.Minor("vitality", 3), PassiveTrees.Minor("vitality", 4));
        Assert.Equal(AllocationError.None, loop.CanRefund(PassiveTrees.Minor("vitality", 2)));
    }

    [Fact]
    public void Starter_and_reference_allocations_spend_their_whole_budget()
    {
        Assert.Equal(StarterCharacters.StartingPassivePoints, StarterCharacters.StarterPassives().PointsSpent);
        Assert.Equal(ReferenceBuilds.PassivePoints, ReferenceBuilds.IronBastion().Passives.PointsSpent);
        Assert.Equal(ReferenceBuilds.PassivePoints, ReferenceBuilds.EmberLancer().Passives.PointsSpent);
        Assert.Throws<ArgumentException>(() => PassiveAllocation.FromTargets(Fresh(3), "pas.key.overcharge"));
    }

    [Fact]
    public void Allocated_node_modifiers_reach_the_build()
    {
        var character = StarterCharacters.NewGuardian();
        double before = character.ToBuild().ComputeStats().Flat(StatId.MaxLife);
        character.Passives = PassiveAllocation.FromPath(Fresh(), "pas.ring.210");
        Assert.Equal(before + 10, character.ToBuild().ComputeStats().Flat(StatId.MaxLife), 6);
    }

    [Fact]
    public void Rebuilding_from_a_set_drops_what_cannot_be_reached()
    {
        var set = new[] { "pas.ring.210", "pas.spoke.210", PassiveTrees.Minor("swiftness", 1), "pas.start.hunter", "pas.unknown" };
        var a = PassiveAllocation.FromSet(Tree, 10, "pas.start.guardian", set, out var dropped);
        Assert.True(a.IsAllocated("pas.spoke.210"));
        Assert.Equal(new[] { "pas.unknown", PassiveTrees.Minor("swiftness", 1), "pas.start.hunter" }.OrderBy(x => x), dropped.OrderBy(x => x));
    }

    [Fact]
    public void Invalid_trees_are_rejected()
    {
        var start = new PassiveNode { Id = "s", Name = "S", Kind = PassiveNodeKind.Start };
        var a = new PassiveNode { Id = "a", Name = "A" };
        var b = new PassiveNode { Id = "b", Name = "B" };
        var s1 = start with { Id = "s1", ClassId = "x" };
        var s2 = start with { Id = "s2", ClassId = "x" };

        Assert.Throws<ArgumentException>(() => new PassiveTreeDefinition("t", new[] { start, a, b }, new[] { ("s", "a") })); // b orphaned
        Assert.Throws<ArgumentException>(() => new PassiveTreeDefinition("t", new[] { start, a }, new[] { ("s", "x") }));   // unknown link
        Assert.Throws<ArgumentException>(() => new PassiveTreeDefinition("t", new[] { a, b }, new[] { ("a", "b") }));      // no start
        Assert.Throws<ArgumentException>(() => new PassiveTreeDefinition("t", new[] { start, start with { Id = "s9" }, a }, new[] { ("s", "a"), ("s9", "a") })); // starts without classes
        Assert.Throws<ArgumentException>(() => new PassiveTreeDefinition("t", new[] { s1, s2, a }, new[] { ("s1", "a"), ("s2", "a") })); // one class, two starts
        Assert.Throws<ArgumentException>(() => Tree.StartNodeFor("class.nobody"));
    }

    private static double Distance(string a, string b)
    {
        var x = Tree[a];
        var y = Tree[b];
        return Math.Sqrt((x.X - y.X) * (x.X - y.X) + (x.Y - y.Y) * (x.Y - y.Y));
    }
}
