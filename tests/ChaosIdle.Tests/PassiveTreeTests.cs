using ChaosIdle.Content;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Stats;
using Xunit;

namespace ChaosIdle.Tests;

public class PassiveTreeTests
{
    private static PassiveTreeDefinition Tree => PassiveTrees.Guardian;

    [Fact]
    public void Content_tree_is_valid_and_sized_for_the_slice()
    {
        Assert.Equal("pas.oathstone", Tree.StartNodeId);
        Assert.InRange(Tree.Nodes.Count, 20, 60);
        Assert.Equal(2, Tree.Nodes.Count(n => n.Kind == PassiveNodeKind.Keystone));
        Assert.All(Tree.Nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.Description)));
        Assert.All(Tree.Nodes.Where(n => n.Kind != PassiveNodeKind.Start), n => Assert.NotEmpty(n.Modifiers));
    }

    [Fact]
    public void Empty_allocation_has_only_the_free_start_node()
    {
        var a = PassiveAllocation.Empty(Tree, 5);
        Assert.Equal(new[] { Tree.StartNodeId }, a.Allocated);
        Assert.Equal(0, a.PointsSpent);
        Assert.Equal(5, a.PointsRemaining);
    }

    [Fact]
    public void Nodes_must_connect_to_the_allocated_set()
    {
        var a = PassiveAllocation.Empty(Tree, 5);
        Assert.Equal(AllocationError.NotConnected, a.CanAllocate("pas.def.unbroken"));
        Assert.Equal(AllocationError.None, a.CanAllocate("pas.def.frame1"));
        Assert.Equal(AllocationError.UnknownNode, a.CanAllocate("pas.nope"));
        Assert.Throws<InvalidOperationException>(() => a.Allocate("pas.def.unbroken"));
    }

    [Fact]
    public void Points_are_limited()
    {
        var a = PassiveAllocation.FromPath(Tree, 1, "pas.def.frame1");
        Assert.Equal(AllocationError.NoPointsLeft, a.CanAllocate("pas.def.skin1"));
        Assert.Equal(AllocationError.AlreadyAllocated, a.CanAllocate("pas.def.frame1"));
    }

    [Fact]
    public void Allocation_is_immutable()
    {
        var a = PassiveAllocation.Empty(Tree, 5);
        var b = a.Allocate("pas.def.frame1");
        Assert.False(a.IsAllocated("pas.def.frame1"));
        Assert.True(b.IsAllocated("pas.def.frame1"));
    }

    [Fact]
    public void Refunds_cannot_disconnect_the_tree()
    {
        var a = PassiveAllocation.FromPath(Tree, 5, "pas.def.frame1", "pas.def.skin1", "pas.def.skin2");
        Assert.Equal(AllocationError.WouldDisconnect, a.CanRefund("pas.def.skin1"));
        Assert.Equal(AllocationError.CannotRefundStart, a.CanRefund(Tree.StartNodeId));
        Assert.Equal(AllocationError.NotAllocated, a.CanRefund("pas.def.guard1"));

        var b = a.Refund("pas.def.skin2");
        Assert.Equal(2, b.PointsSpent);
        Assert.Equal(AllocationError.None, b.CanRefund("pas.def.skin1"));
    }

    [Fact]
    public void Refund_is_allowed_when_a_loop_keeps_nodes_connected()
    {
        // skin1 -> skin2 -> unbroken and skin1 -> frame2 -> unbroken form a loop.
        var a = PassiveAllocation.FromPath(Tree, 6, "pas.def.frame1", "pas.def.skin1", "pas.def.skin2", "pas.def.unbroken", "pas.def.frame2");
        Assert.Equal(AllocationError.None, a.CanRefund("pas.def.skin2"));
    }

    [Fact]
    public void Allocated_node_modifiers_reach_the_build()
    {
        var character = StarterCharacters.NewGuardian();
        double before = character.ToBuild().ComputeStats().Resolve(StatId.MaxLife);
        character.Passives = PassiveAllocation.FromPath(Tree, 5, "pas.def.frame1");
        double after = character.ToBuild().ComputeStats().Resolve(StatId.MaxLife);
        Assert.Equal(before * 1.08, after, 6);
    }

    [Fact]
    public void Bulwark_path_is_a_legal_allocation_of_all_starting_points()
    {
        var a = PassiveAllocation.FromPath(Tree, StarterCharacters.StartingPassivePoints, StarterCharacters.BulwarkPath);
        Assert.Equal(0, a.PointsRemaining);
    }

    [Fact]
    public void Invalid_trees_are_rejected()
    {
        var start = new PassiveNode { Id = "s", Name = "S", Kind = PassiveNodeKind.Start };
        var a = new PassiveNode { Id = "a", Name = "A" };
        var b = new PassiveNode { Id = "b", Name = "B" };

        Assert.Throws<ArgumentException>(() => new PassiveTreeDefinition("t", new[] { start, a, b }, new[] { ("s", "a") })); // b orphaned
        Assert.Throws<ArgumentException>(() => new PassiveTreeDefinition("t", new[] { start, a }, new[] { ("s", "x") }));   // unknown link
        Assert.Throws<ArgumentException>(() => new PassiveTreeDefinition("t", new[] { a, b }, new[] { ("a", "b") }));      // no start
        Assert.Throws<ArgumentException>(() => new PassiveTreeDefinition("t", new[] { start, start }, Array.Empty<(string, string)>()));
    }
}
