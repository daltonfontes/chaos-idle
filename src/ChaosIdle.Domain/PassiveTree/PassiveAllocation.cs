using System;
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

        /// <summary>Another class's starting point: never allocatable or passable.</summary>
        OtherClassStart = 8,

        /// <summary>A path exists but costs more points than are left.</summary>
        NotEnoughPoints = 9,

        /// <summary>No route from the allocated nodes (should not happen in a connected tree).</summary>
        Unreachable = 10,
    }

    /// <summary>
    /// Immutable set of allocated nodes. The class's start node is always allocated and free;
    /// every other node costs one point and must stay connected to it through allocated nodes.
    /// </summary>
    public sealed class PassiveAllocation
    {
        private readonly HashSet<string> _allocated;

        private PassiveAllocation(PassiveTreeDefinition tree, int totalPoints, string startNodeId, HashSet<string> allocated)
        {
            Tree = tree;
            TotalPoints = totalPoints;
            StartNodeId = startNodeId;
            _allocated = allocated;
        }

        public PassiveTreeDefinition Tree { get; }
        public int TotalPoints { get; }
        public string StartNodeId { get; }
        public IReadOnlyCollection<string> Allocated => _allocated;
        public int PointsSpent => _allocated.Count - 1;
        public int PointsRemaining => TotalPoints - PointsSpent;

        /// <summary>Single-start trees only.</summary>
        public static PassiveAllocation Empty(PassiveTreeDefinition tree, int totalPoints) =>
            Empty(tree, totalPoints, tree.StartNodeFor(null));

        public static PassiveAllocation Empty(PassiveTreeDefinition tree, int totalPoints, string startNodeId)
        {
            if (!tree.IsStart(startNodeId)) throw new ArgumentException($"{startNodeId} is not a start node.");
            return new PassiveAllocation(tree, totalPoints, startNodeId, new HashSet<string> { startNodeId });
        }

        /// <summary>Allocates nodes in order; throws if any step is illegal. Handy for content and tests.</summary>
        public static PassiveAllocation FromPath(PassiveTreeDefinition tree, int totalPoints, params string[] nodeIds) =>
            FromPath(Empty(tree, totalPoints), nodeIds);

        public static PassiveAllocation FromPath(PassiveAllocation start, params string[] nodeIds)
        {
            var allocation = start;
            foreach (var id in nodeIds)
            {
                var error = allocation.CanAllocate(id);
                if (error != AllocationError.None) throw new ArgumentException($"Cannot allocate {id}: {error}.");
                allocation = allocation.Allocate(id);
            }

            return allocation;
        }

        /// <summary>Walks the shortest path to each target in turn (content helper). Throws if any target can't be reached.</summary>
        public static PassiveAllocation FromTargets(PassiveAllocation start, params string[] targets)
        {
            var allocation = start;
            foreach (var target in targets)
            {
                if (allocation.IsAllocated(target)) continue;
                var (error, _) = allocation.CanAllocatePath(target);
                if (error != AllocationError.None) throw new ArgumentException($"Cannot reach {target}: {error}.");
                allocation = allocation.AllocatePath(target);
            }

            return allocation;
        }

        /// <summary>
        /// Rebuilds an allocation from an unordered set (e.g. a save). Unknown nodes, nodes that
        /// cannot be reached from the start through the set, other classes' starts, and nodes
        /// beyond the point budget are left out and returned in <paramref name="dropped"/>.
        /// </summary>
        public static PassiveAllocation FromSet(PassiveTreeDefinition tree, int totalPoints, string startNodeId, IEnumerable<string> nodeIds, out List<string> dropped)
        {
            var wanted = new List<string>(nodeIds.Where(id => id != startNodeId).Distinct());
            dropped = wanted.Where(id => !tree.Contains(id)).ToList();
            var pending = new List<string>(wanted.Where(tree.Contains));
            var allocation = Empty(tree, totalPoints, startNodeId);

            bool progress = true;
            while (progress && pending.Count > 0)
            {
                progress = false;
                foreach (var id in pending.ToList())
                {
                    if (allocation.CanAllocate(id) == AllocationError.None)
                    {
                        allocation = allocation.Allocate(id);
                        pending.Remove(id);
                        progress = true;
                    }
                }
            }

            dropped.AddRange(pending);
            return allocation;
        }

        public static PassiveAllocation FromSet(PassiveTreeDefinition tree, int totalPoints, IEnumerable<string> nodeIds, out List<string> dropped) =>
            FromSet(tree, totalPoints, tree.StartNodeFor(null), nodeIds, out dropped);

        public bool IsAllocated(string id) => _allocated.Contains(id);

        public AllocationError CanAllocate(string id)
        {
            if (!Tree.Contains(id)) return AllocationError.UnknownNode;
            if (_allocated.Contains(id)) return AllocationError.AlreadyAllocated;
            if (Tree.IsStart(id)) return AllocationError.OtherClassStart;
            if (PointsRemaining <= 0) return AllocationError.NoPointsLeft;
            if (!Tree.Neighbours(id).Any(_allocated.Contains)) return AllocationError.NotConnected;
            return AllocationError.None;
        }

        public PassiveAllocation Allocate(string id)
        {
            var error = CanAllocate(id);
            if (error != AllocationError.None) throw new InvalidOperationException($"Cannot allocate {id}: {error}.");
            return new PassiveAllocation(Tree, TotalPoints, StartNodeId, new HashSet<string>(_allocated) { id });
        }

        /// <summary>
        /// The fewest unallocated nodes to take, in order, so that <paramref name="target"/> becomes
        /// allocated (target last). Never passes through another class's start. Null if unreachable.
        /// Ties are broken by node definition order, so the answer is deterministic.
        /// </summary>
        public IReadOnlyList<string>? PathTo(string target)
        {
            if (!Tree.Contains(target) || Tree.IsStart(target)) return null;
            if (_allocated.Contains(target)) return Array.Empty<string>();

            var previous = new Dictionary<string, string?>();
            var queue = new Queue<string>();
            foreach (var id in _allocated)
            {
                previous[id] = null;
            }

            // Expand from allocated nodes in a stable order.
            foreach (var id in Tree.Nodes.Select(n => n.Id).Where(_allocated.Contains)) queue.Enqueue(id);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in Tree.Neighbours(current))
                {
                    if (previous.ContainsKey(next) || Tree.IsStart(next)) continue;
                    previous[next] = current;
                    if (next == target)
                    {
                        var path = new List<string>();
                        for (string? at = target; at != null && !_allocated.Contains(at); at = previous[at]) path.Add(at);
                        path.Reverse();
                        return path;
                    }

                    queue.Enqueue(next);
                }
            }

            return null;
        }

        /// <summary>Whether the whole path to <paramref name="target"/> can be taken now, and its cost in points.</summary>
        public (AllocationError Error, int Cost) CanAllocatePath(string target)
        {
            if (!Tree.Contains(target)) return (AllocationError.UnknownNode, 0);
            if (_allocated.Contains(target)) return (AllocationError.AlreadyAllocated, 0);
            if (Tree.IsStart(target)) return (AllocationError.OtherClassStart, 0);
            var path = PathTo(target);
            if (path == null) return (AllocationError.Unreachable, 0);
            if (path.Count > PointsRemaining) return (AllocationError.NotEnoughPoints, path.Count);
            return (AllocationError.None, path.Count);
        }

        public PassiveAllocation AllocatePath(string target)
        {
            var (error, _) = CanAllocatePath(target);
            if (error != AllocationError.None) throw new InvalidOperationException($"Cannot allocate path to {target}: {error}.");
            var set = new HashSet<string>(_allocated);
            foreach (var id in PathTo(target)!) set.Add(id);
            return new PassiveAllocation(Tree, TotalPoints, StartNodeId, set);
        }

        public AllocationError CanRefund(string id)
        {
            if (!Tree.Contains(id)) return AllocationError.UnknownNode;
            if (!_allocated.Contains(id)) return AllocationError.NotAllocated;
            if (id == StartNodeId) return AllocationError.CannotRefundStart;

            var remaining = new HashSet<string>(_allocated);
            remaining.Remove(id);
            return Tree.Reachable(remaining, StartNodeId).Count == remaining.Count
                ? AllocationError.None
                : AllocationError.WouldDisconnect;
        }

        public PassiveAllocation Refund(string id)
        {
            var error = CanRefund(id);
            if (error != AllocationError.None) throw new InvalidOperationException($"Cannot refund {id}: {error}.");
            var remaining = new HashSet<string>(_allocated);
            remaining.Remove(id);
            return new PassiveAllocation(Tree, TotalPoints, StartNodeId, remaining);
        }

        public IEnumerable<Modifier> Modifiers() =>
            _allocated.OrderBy(id => id, StringComparer.Ordinal).SelectMany(id => Tree[id].Modifiers);
    }
}
