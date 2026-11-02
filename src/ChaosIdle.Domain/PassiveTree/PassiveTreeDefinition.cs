using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.PassiveTree
{
    public enum PassiveNodeKind
    {
        /// <summary>A class's starting point (free, owned by <see cref="PassiveNode.ClassId"/>).</summary>
        Start = 0,
        Minor = 1,
        Notable = 2,

        /// <summary>Changes a rule of the game and carries a real cost.</summary>
        Keystone = 3,
    }

    public sealed record PassiveNode
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public PassiveNodeKind Kind { get; init; } = PassiveNodeKind.Minor;
        public IReadOnlyList<Modifier> Modifiers { get; init; } = Array.Empty<Modifier>();

        /// <summary>Layout position for drawing the tree (arbitrary units, origin at the centre).</summary>
        public double X { get; init; }
        public double Y { get; init; }

        /// <summary>Start nodes only: the class that begins here.</summary>
        public string? ClassId { get; init; }
    }

    /// <summary>
    /// An undirected graph of passive nodes shared by all classes. Each class begins at its own
    /// start node; other classes' start nodes can never be allocated or walked through.
    /// </summary>
    public sealed class PassiveTreeDefinition
    {
        private readonly Dictionary<string, PassiveNode> _nodes;
        private readonly List<PassiveNode> _ordered;
        private readonly Dictionary<string, List<string>> _adjacency;
        private readonly List<(string A, string B)> _links = new List<(string, string)>();

        public PassiveTreeDefinition(string id, IEnumerable<PassiveNode> nodes, IEnumerable<(string A, string B)> links)
        {
            Id = id;
            _ordered = nodes.ToList();
            var errors = new List<string>();

            _nodes = new Dictionary<string, PassiveNode>();
            foreach (var n in _ordered)
            {
                if (_nodes.ContainsKey(n.Id)) errors.Add($"Duplicate passive node id {n.Id}.");
                _nodes[n.Id] = n;
            }

            _adjacency = _nodes.Keys.ToDictionary(k => k, _ => new List<string>());
            foreach (var (a, b) in links)
            {
                if (!_nodes.ContainsKey(a) || !_nodes.ContainsKey(b))
                {
                    errors.Add($"Link {a} - {b} references an unknown node.");
                    continue;
                }

                if (a == b) errors.Add($"Node {a} links to itself.");
                if (_adjacency[a].Contains(b)) continue;
                _adjacency[a].Add(b);
                _adjacency[b].Add(a);
                _links.Add((a, b));
            }

            StartNodes = _ordered.Where(n => n.Kind == PassiveNodeKind.Start).ToList();
            if (StartNodes.Count == 0) errors.Add($"Tree {id} has no start node.");
            if (StartNodes.Count > 1)
            {
                if (StartNodes.Any(s => string.IsNullOrWhiteSpace(s.ClassId)))
                    errors.Add($"Tree {id} has several start nodes; each needs a ClassId.");
                foreach (var dup in StartNodes.GroupBy(s => s.ClassId).Where(g => g.Count() > 1))
                    errors.Add($"Class {dup.Key} has more than one start node.");
            }

            if (errors.Count == 0)
            {
                var reachable = Reachable(new HashSet<string>(_nodes.Keys), StartNodes[0].Id);
                foreach (var orphan in _nodes.Keys.Where(k => !reachable.Contains(k)))
                {
                    errors.Add($"Node {orphan} is not connected to the rest of the tree.");
                }
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException($"Invalid passive tree {id}:\n" + string.Join("\n", errors));
            }
        }

        public string Id { get; }

        /// <summary>All nodes, in definition order.</summary>
        public IReadOnlyList<PassiveNode> Nodes => _ordered;

        /// <summary>Every connection once, for drawing.</summary>
        public IReadOnlyList<(string A, string B)> Links => _links;

        public IReadOnlyList<PassiveNode> StartNodes { get; }

        public PassiveNode this[string id] => _nodes[id];

        public bool Contains(string id) => _nodes.ContainsKey(id);

        public IReadOnlyList<string> Neighbours(string id) => _adjacency[id];

        /// <summary>The start node of a class, or the only start node of a single-start tree.</summary>
        public string StartNodeFor(string? classId)
        {
            if (StartNodes.Count == 1 && (classId == null || StartNodes[0].ClassId == null || StartNodes[0].ClassId == classId))
                return StartNodes[0].Id;
            return StartNodes.FirstOrDefault(s => s.ClassId == classId)?.Id
                   ?? throw new ArgumentException($"Tree {Id} has no start node for class {classId}.");
        }

        public bool IsStart(string id) => _nodes.TryGetValue(id, out var n) && n.Kind == PassiveNodeKind.Start;

        /// <summary>Nodes reachable from <paramref name="from"/> moving only through <paramref name="within"/>.</summary>
        internal HashSet<string> Reachable(ISet<string> within, string from)
        {
            var seen = new HashSet<string>();
            if (!within.Contains(from)) return seen;

            var stack = new Stack<string>();
            stack.Push(from);
            seen.Add(from);
            while (stack.Count > 0)
            {
                foreach (var next in _adjacency[stack.Pop()])
                {
                    if (within.Contains(next) && seen.Add(next)) stack.Push(next);
                }
            }

            return seen;
        }
    }
}
