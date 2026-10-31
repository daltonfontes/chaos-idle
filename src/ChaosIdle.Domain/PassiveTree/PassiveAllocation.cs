using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.PassiveTree
{
    public enum AllocationError
    {
        None = 0,
        UnknownNode = 1,
        AlreadyAllocated = 2,
        NotAllocated = 3,
        NotConnected = 4,
        NoPointsLeft = 5,
        CannotRefundStart = 6,

        /// <summary>Refunding would leave other allocated nodes disconnected from the start.</summary>
        WouldDisconnect = 7,
    }

    /// <summary>
    /// Immutable set of allocated nodes. The start node is always allocated and free; every other
    /// node costs one point and must stay connected to the start through allocated nodes.
    /// </summary>
    public sealed class PassiveAllocation
    {
        private readonly HashSet<string> _allocated;

        private PassiveAllocation(PassiveTreeDefinition tree, int totalPoints, HashSet<string> allocated)
        {
            Tree = tree;
            TotalPoints = totalPoints;
            _allocated = allocated;
        }

        public PassiveTreeDefinition Tree { get; }
        public int TotalPoints { get; }
        public IReadOnlyCollection<string> Allocated => _allocated;
        public int PointsSpent => _allocated.Count - 1;
        public int PointsRemaining => TotalPoints - PointsSpent;

        public static PassiveAllocation Empty(PassiveTreeDefinition tree, int totalPoints) =>
            new PassiveAllocation(tree, totalPoints, new HashSet<string> { tree.StartNodeId });

        /// <summary>Allocates nodes in order; throws if any step is illegal. Handy for content and tests.</summary>
        public static PassiveAllocation FromPath(PassiveTreeDefinition tree, int totalPoints, params string[] nodeIds)
        {
            var allocation = Empty(tree, totalPoints);
            foreach (var id in nodeIds)
            {
                var error = allocation.CanAllocate(id);
                if (error != AllocationError.None)
                {
                    throw new System.ArgumentException($"Cannot allocate {id}: {error}.");
                }

                allocation = allocation.Allocate(id);
            }

            return allocation;
        }

        public bool IsAllocated(string id) => _allocated.Contains(id);

        public AllocationError CanAllocate(string id)
        {
            if (!Tree.Contains(id)) return AllocationError.UnknownNode;
            if (_allocated.Contains(id)) return AllocationError.AlreadyAllocated;
            if (PointsRemaining <= 0) return AllocationError.NoPointsLeft;
            if (!Tree.Neighbours(id).Any(_allocated.Contains)) return AllocationError.NotConnected;
            return AllocationError.None;
        }

        public PassiveAllocation Allocate(string id)
        {
            var error = CanAllocate(id);
            if (error != AllocationError.None) throw new System.InvalidOperationException($"Cannot allocate {id}: {error}.");
            return new PassiveAllocation(Tree, TotalPoints, new HashSet<string>(_allocated) { id });
        }

        public AllocationError CanRefund(string id)
        {
            if (!Tree.Contains(id)) return AllocationError.UnknownNode;
            if (!_allocated.Contains(id)) return AllocationError.NotAllocated;
            if (id == Tree.StartNodeId) return AllocationError.CannotRefundStart;

            var remaining = new HashSet<string>(_allocated);
            remaining.Remove(id);
            return Tree.Reachable(remaining, Tree.StartNodeId).Count == remaining.Count
                ? AllocationError.None
                : AllocationError.WouldDisconnect;
        }

        public PassiveAllocation Refund(string id)
        {
            var error = CanRefund(id);
            if (error != AllocationError.None) throw new System.InvalidOperationException($"Cannot refund {id}: {error}.");
            var remaining = new HashSet<string>(_allocated);
            remaining.Remove(id);
            return new PassiveAllocation(Tree, TotalPoints, remaining);
        }

        public IEnumerable<Modifier> Modifiers() =>
            _allocated.OrderBy(id => id, System.StringComparer.Ordinal).SelectMany(id => Tree[id].Modifiers);
    }
}
