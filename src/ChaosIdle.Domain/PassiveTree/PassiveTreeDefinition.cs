using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Domain.Stats;

namespace ChaosIdle.Domain.PassiveTree
{
    public enum PassiveNodeKind
    {
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
    }

    /// <summary>An undirected graph of passive nodes rooted at a single start node.</summary>
    public sealed class PassiveTreeDefinition
    {
        private readonly Dictionary<string, PassiveNode> _nodes;
        private readonly Dictionary<string, List<string>> _adjacency;

        public PassiveTreeDefinition(string id, IEnumerable<PassiveNode> nodes, IEnumerable<(string A, string B)> links)
        {
            Id = id;
            var nodeList = nodes.ToList();
            var errors = new List<string>();

            _nodes = new Dictionary<string, PassiveNode>();
            foreach (var n in nodeList)
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
                if (!_adjacency[a].Contains(b)) _adjacency[a].Add(b);
                if (!_adjacency[b].Contains(a)) _adjacency[b].Add(a);
            }

            var starts = nodeList.Where(n => n.Kind == PassiveNodeKind.Start).ToList();
            if (starts.Count != 1) errors.Add($"Tree {id} must have exactly one start node; found {starts.Count}.");

            if (errors.Count == 0)
            {
                StartNodeId = starts[0].Id;
                var reachable = Reachable(new HashSet<string>(_nodes.Keys), StartNodeId);
                foreach (var orphan in _nodes.Keys.Where(k => !reachable.Contains(k)))
                {
                    errors.Add($"Node {orphan} is not connected to the start.");
                }
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException($"Invalid passive tree {id}:\n" + string.Join("\n", errors));
            }
        }

        public string Id { get; }
        public string StartNodeId { get; } = "";
        public IReadOnlyCollection<PassiveNode> Nodes => _nodes.Values;

        public PassiveNode this[string id] => _nodes[id];

        public bool Contains(string id) => _nodes.ContainsKey(id);

        public IReadOnlyList<string> Neighbours(string id) => _adjacency[id];

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
