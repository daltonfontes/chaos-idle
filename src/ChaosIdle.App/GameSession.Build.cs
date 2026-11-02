using System;
using System.Collections.Generic;
using System.Linq;
using ChaosIdle.Content;
using ChaosIdle.Domain.Characters;
using ChaosIdle.Domain.PassiveTree;
using ChaosIdle.Domain.Skills;
using ChaosIdle.Simulation.Analysis;
using ChaosIdle.Simulation.Reporting;

namespace ChaosIdle.App
{
    public enum PassiveState
    {
        Allocated = 0,

        /// <summary>Connected and a point is free: can be taken now.</summary>
        Available = 1,

        /// <summary>Not connected yet, or no points left.</summary>
        Locked = 2,
    }

    public sealed record PassiveNodeView(
        string Id, string Name, string Description, PassiveNodeKind Kind, string Branch, PassiveState State, string Reason,
        double X, double Y, bool IsOwnStart);

    public sealed record PassiveTreeView(int PointsSpent, int PointsTotal, IReadOnlyList<PassiveNodeView> Nodes, IReadOnlyList<(string A, string B)> Links)
    {
        public int PointsRemaining => PointsTotal - PointsSpent;
    }

    /// <summary>The route to a node: the nodes to take in order (target last) and what they change together.</summary>
    public sealed record PassivePath(string TargetId, IReadOnlyList<string> NodeIds, bool CanTake, IReadOnlyList<string> Lines);

    public sealed record SkillView(string Id, string Name, bool IsPrimary, string Kind, IReadOnlyList<SupportOption> Linked, int FreeSockets);

    public enum SupportStatus
    {
        Linked = 0,
        Available = 1,

        /// <summary>The support's tag rules forbid this skill.</summary>
        Incompatible = 2,

        NoFreeSocket = 3,
    }

    public sealed record SupportOption(string Id, string Name, string Description, SupportStatus Status, string Reason);

    public sealed record SkillChoice(string Id, string Name, bool IsCurrent);

    /// <summary>Passive tree and skill/support screens.</summary>
    public sealed partial class GameSession
    {

        // ---- Passive tree ----

        public PassiveTreeView PassiveTree()
        {
            var a = Character.Passives;
            var nodes = a.Tree.Nodes
                .Select(n =>
                {
                    bool own = n.Id == a.StartNodeId;
                    if (a.IsAllocated(n.Id))
                    {
                        string why = own ? "Your starting point." : RefundReason(a.CanRefund(n.Id));
                        return new PassiveNodeView(n.Id, n.Name, n.Description, n.Kind, Branch(n), PassiveState.Allocated, why, n.X, n.Y, own);
                    }

                    var error = a.CanAllocate(n.Id);
                    string reason = error == AllocationError.None ? AllocateReason(error) : LockedReason(a, n.Id, error);
                    return new PassiveNodeView(n.Id, n.Name, n.Description, n.Kind, Branch(n),
                        error == AllocationError.None ? PassiveState.Available : PassiveState.Locked, reason, n.X, n.Y, false);
                })
                .OrderBy(n => BranchRank(n.Branch))
                .ThenBy(n => n.Branch, StringComparer.Ordinal)
                .ThenBy(n => n.State)
                .ThenBy(n => n.Id, StringComparer.Ordinal)
                .ToList();
            return new PassiveTreeView(a.PointsSpent, a.TotalPoints, nodes, a.Tree.Links);
        }

        /// <summary>
        /// The shortest route from what you have to <paramref name="nodeId"/>, its cost, and its
        /// combined effect. For an allocated node, the same as <see cref="PreviewPassive"/>.
        /// </summary>
        public PassivePath PreviewPath(string nodeId)
        {
            var a = Character.Passives;
            if (!a.Tree.Contains(nodeId)) throw new ArgumentException($"Unknown passive {nodeId}.");
            if (a.IsAllocated(nodeId)) return new PassivePath(nodeId, Array.Empty<string>(), false, PreviewPassive(nodeId));

            var node = a.Tree[nodeId];
            var lines = new List<string> { $"{node.Name} ({node.Kind})", node.Description };
            var (error, cost) = a.CanAllocatePath(nodeId);
            var path = a.PathTo(nodeId) ?? Array.Empty<string>();
            if (error == AllocationError.OtherClassStart)
            {
                lines.Add("Another class begins here; you can't take it or walk through it.");
                return new PassivePath(nodeId, Array.Empty<string>(), false, lines);
            }

            if (error == AllocationError.None) lines.Add(cost == 1 ? "1 point: right next to your passives." : $"{cost} points along the shortest path.");
            else lines.Add(LockedReason(a, nodeId, error));
            var after = error == AllocationError.None ? a.AllocatePath(nodeId) : PassiveAllocation.FromPath(Unlimited(a), path.ToArray());
            lines.Add(error == AllocationError.None ? "If you take the path:" : "With enough points, the path would give:");
            lines.AddRange(DiffLines(Character.ToBuild(), WithPassives(after)));
            return new PassivePath(nodeId, path, error == AllocationError.None, lines);
        }

        /// <summary>Takes every node on the shortest path to <paramref name="nodeId"/>.</summary>
        public string AllocatePath(string nodeId, DateTime nowUtc)
        {
            var a = Character.Passives;
            var (error, cost) = a.CanAllocatePath(nodeId);
            if (error != AllocationError.None) return LockedReason(a, nodeId, error);
            Character.Passives = a.AllocatePath(nodeId);
            Save(nowUtc);
            return $"Took {a.Tree[nodeId].Name} ({cost} point{(cost == 1 ? "" : "s")}). {Character.Passives.PointsRemaining} point(s) left.";
        }

        /// <summary>What taking (or, if allocated, refunding) this node changes on the character sheet.</summary>
        public IReadOnlyList<string> PreviewPassive(string nodeId)
        {
            var a = Character.Passives;
            var node = a.Tree.Contains(nodeId) ? a.Tree[nodeId] : throw new ArgumentException($"Unknown passive {nodeId}.");
            var lines = new List<string> { $"{node.Name} ({node.Kind})", node.Description };

            PassiveAllocation? other = null;
            if (a.IsAllocated(nodeId))
            {
                var check = a.CanRefund(nodeId);
                if (check == AllocationError.None) other = a.Refund(nodeId);
                else lines.Add(RefundReason(check));
            }
            else
            {
                var check = a.CanAllocate(nodeId);
                if (check == AllocationError.None) other = a.Allocate(nodeId);
                else lines.Add(AllocateReason(check));
            }

            if (other != null)
            {
                lines.Add(a.IsAllocated(nodeId) ? "If you refund it:" : "If you take it:");
                lines.AddRange(DiffLines(Character.ToBuild(), WithPassives(other)));
            }

            return lines;
        }

        public string AllocatePassive(string nodeId, DateTime nowUtc)
        {
            var a = Character.Passives;
            var error = a.CanAllocate(nodeId);
            if (error != AllocationError.None) return a.Tree.Contains(nodeId) ? LockedReason(a, nodeId, error) : AllocateReason(error);
            Character.Passives = a.Allocate(nodeId);
            Save(nowUtc);
            return $"Took {a.Tree[nodeId].Name}. {Character.Passives.PointsRemaining} point(s) left.";
        }

        /// <summary>Refunds are free for now (there is no currency yet).</summary>
        public string RefundPassive(string nodeId, DateTime nowUtc)
        {
            var a = Character.Passives;
            var error = a.CanRefund(nodeId);
            if (error != AllocationError.None) return RefundReason(error);
            Character.Passives = a.Refund(nodeId);
            Save(nowUtc);
            return $"Refunded {a.Tree[nodeId].Name}. {Character.Passives.PointsRemaining} point(s) left.";
        }

        // ---- Skills and supports ----

        /// <summary>Skills the character can use as its main skill (triggered skills are not choices).</summary>
        public IReadOnlyList<SkillChoice> PrimarySkillChoices() =>
            Skills.All.Where(s => s.Trigger == SkillTrigger.None)
                .Select(s => new SkillChoice(s.Id, s.Name, s.Id == Character.PrimarySkill.Id)).ToList();

        public string SetPrimarySkill(string skillId, DateTime nowUtc)
        {
            var skill = Skills.All.FirstOrDefault(s => s.Id == skillId && s.Trigger == SkillTrigger.None)
                        ?? throw new ArgumentException($"{skillId} cannot be a main skill.");
            if (skill.Id == Character.PrimarySkill.Id) return $"{skill.Name} is already your main skill.";
            Character.PrimarySkill = skill;
            Save(nowUtc);
            string weaponNote = skill.IsSpell ? "" : (Character.Weapon == PlayerCharacter.Unarmed ? " You have no weapon equipped." : "");
            return $"{skill.Name} is now your main skill.{weaponNote}";
        }

        public IReadOnlyList<SkillView> SkillsInUse() =>
            Character.TriggeredSkills.Prepend(Character.PrimarySkill)
                .Select(s =>
                {
                    var linked = Character.SupportsFor(s);
                    string kind = s.Trigger != SkillTrigger.None ? $"triggered ({TriggerText(s.Trigger)})" : (s.IsSpell ? "spell" : "attack");
                    return new SkillView(s.Id, s.Name, s.Id == Character.PrimarySkill.Id, kind,
                        linked.Select(l => new SupportOption(l.Id, l.Name, l.Description, SupportStatus.Linked, "Linked.")).ToList(),
                        SupportDefinition.MaxSupportsPerSkill - linked.Count);
                })
                .ToList();

        /// <summary>Every support, and whether it can go on this skill right now.</summary>
        public IReadOnlyList<SupportOption> SupportOptions(string skillId)
        {
            var skill = SkillInUse(skillId);
            var linked = Character.SupportsFor(skill).ToList();
            return Supports.All.Select(s =>
            {
                if (linked.Any(l => l.Id == s.Id)) return new SupportOption(s.Id, s.Name, s.Description, SupportStatus.Linked, "Linked.");
                var check = s.CanLink(skill, linked);
                var status = check == SupportCompatibility.Compatible ? SupportStatus.Available
                    : check == SupportCompatibility.NoFreeSocket ? SupportStatus.NoFreeSocket
                    : SupportStatus.Incompatible;
                return new SupportOption(s.Id, s.Name, s.Description, status, SupportReason(check, s, skill));
            })
            .OrderBy(o => o.Status)
            .ToList();
        }

        public IReadOnlyList<string> PreviewSupport(string skillId, string supportId)
        {
            var skill = SkillInUse(skillId);
            var support = Supports.All.FirstOrDefault(s => s.Id == supportId) ?? throw new ArgumentException($"Unknown support {supportId}.");
            var lines = new List<string> { support.Name, support.Description };
            var linked = Character.SupportsFor(skill).ToList();
            bool isLinked = linked.Any(l => l.Id == supportId);

            if (!isLinked)
            {
                var check = support.CanLink(skill, linked);
                if (check != SupportCompatibility.Compatible)
                {
                    lines.Add(SupportReason(check, support, skill));
                    return lines;
                }
            }

            var trial = Character.CloneLoadout();
            if (isLinked) trial.UnlinkSupport(skill, trial.SupportsFor(skill).First(s => s.Id == supportId));
            else trial.LinkSupport(skill, support);

            lines.Add(isLinked ? $"If you unlink it from {skill.Name}:" : $"If you link it to {skill.Name}:");
            if (skill.Trigger != SkillTrigger.None) lines.Add("(Triggered skills are not on the character sheet; effects show in fights.)");
            lines.AddRange(DiffLines(Character.ToBuild(), trial.ToBuild()));
            return lines;
        }

        public string LinkSupport(string skillId, string supportId, DateTime nowUtc)
        {
            var skill = SkillInUse(skillId);
            var support = Supports.All.FirstOrDefault(s => s.Id == supportId) ?? throw new ArgumentException($"Unknown support {supportId}.");
            var result = Character.LinkSupport(skill, support);
            if (result != SupportCompatibility.Compatible) return SupportReason(result, support, skill);
            Save(nowUtc);
            return $"Linked {support.Name} to {skill.Name}.";
        }

        public string UnlinkSupport(string skillId, string supportId, DateTime nowUtc)
        {
            var skill = SkillInUse(skillId);
            var support = Character.SupportsFor(skill).FirstOrDefault(s => s.Id == supportId);
            if (support == null) return "That support is not linked to this skill.";
            Character.UnlinkSupport(skill, support);
            Save(nowUtc);
            return $"Unlinked {support.Name} from {skill.Name}.";
        }

        // ---- helpers ----

        private SkillDefinition SkillInUse(string skillId) =>
            Character.TriggeredSkills.Prepend(Character.PrimarySkill).FirstOrDefault(s => s.Id == skillId)
            ?? throw new ArgumentException($"{skillId} is not one of your skills.");

        private CharacterBuild WithPassives(PassiveAllocation passives)
        {
            var trial = Character.CloneLoadout();
            trial.Passives = passives;
            return trial.ToBuild();
        }

        private static IReadOnlyList<string> DiffLines(CharacterBuild before, CharacterBuild after)
        {
            var changes = ItemComparer.Diff(BuildSummary.Compute(before), BuildSummary.Compute(after));
            if (changes.Count == 0) return new[] { "  No change to the character sheet." };
            return changes.Select(c =>
            {
                string arrow = !c.AffectsVerdict ? "• info  " : (c.IsImprovement ? "▲ better" : "▼ worse");
                return $"  {arrow}  {c.Label}: {c.Before.ToString(c.Format, System.Globalization.CultureInfo.InvariantCulture)} → {c.After.ToString(c.Format, System.Globalization.CultureInfo.InvariantCulture)}";
            }).ToList();
        }

        /// <summary>Start, Roads (ring, spokes, bridges, rim paths), one branch per cluster theme, Keystones.</summary>
        private static string Branch(PassiveNode n)
        {
            if (n.Kind == PassiveNodeKind.Start) return "Start";
            if (n.Kind == PassiveNodeKind.Keystone) return "Keystones";
            var parts = n.Id.Split('.');
            if (parts.Length < 3) return "Other";
            switch (parts[1])
            {
                case "ring":
                case "spoke":
                case "bridge":
                case "reach":
                    return "Roads";
                default:
                    return char.ToUpperInvariant(parts[1][0]) + parts[1].Substring(1);
            }
        }

        private static int BranchRank(string branch) => branch == "Start" ? 0 : branch == "Keystones" ? 3 : branch == "Roads" ? 2 : 1;

        /// <summary>Why a node can't be taken right now, including how far away it is.</summary>
        private static string LockedReason(PassiveAllocation a, string id, AllocationError error)
        {
            if (error == AllocationError.OtherClassStart) return "Another class begins here.";
            var (pathError, cost) = a.CanAllocatePath(id);
            if (pathError == AllocationError.None) return $"{cost} points away: take the path to reach it.";
            if (pathError == AllocationError.NotEnoughPoints) return $"{cost} points away, but you have {a.PointsRemaining} left: refund some passives first.";
            return AllocateReason(error);
        }

        /// <summary>The same allocation with points to spare, to preview paths you can't afford yet.</summary>
        private static PassiveAllocation Unlimited(PassiveAllocation a) =>
            PassiveAllocation.FromSet(a.Tree, a.TotalPoints + 1000, a.StartNodeId, a.Allocated, out _);

        private static string AllocateReason(AllocationError e)
        {
            switch (e)
            {
                case AllocationError.None: return "You can take this.";
                case AllocationError.NotConnected: return "Not connected: take a neighbouring passive first.";
                case AllocationError.NoPointsLeft: return "No passive points left: refund one to take this.";
                case AllocationError.AlreadyAllocated: return "Already taken.";
                default: return $"Cannot take this ({e}).";
            }
        }

        private static string RefundReason(AllocationError e)
        {
            switch (e)
            {
                case AllocationError.None: return "Taken. You can refund it for free.";
                case AllocationError.WouldDisconnect: return "Taken. Other passives depend on it: refund those first.";
                case AllocationError.CannotRefundStart: return "Your starting point.";
                default: return $"Cannot refund this ({e}).";
            }
        }

        private static string SupportReason(SupportCompatibility c, SupportDefinition s, SkillDefinition skill)
        {
            switch (c)
            {
                case SupportCompatibility.Compatible: return $"Can be linked to {skill.Name}.";
                case SupportCompatibility.MissingTag: return $"Does not fit {skill.Name}: it needs {TagText(s.RequiredTags)} skills.";
                case SupportCompatibility.ExcludedTag: return $"Does not fit {skill.Name}: it cannot support {TagText(s.ExcludedTags)} skills.";
                case SupportCompatibility.AlreadyLinked: return "Already linked.";
                case SupportCompatibility.NoFreeSocket: return $"{skill.Name} has no free socket ({SupportDefinition.MaxSupportsPerSkill} max): unlink one first.";
                default: return c.ToString();
            }
        }

        private static string TagText(SkillTags tags) =>
            string.Join(" ", Enum.GetValues(typeof(SkillTags)).Cast<SkillTags>()
                .Where(t => t != SkillTags.None && (tags & t) == t)
                .Select(t => t.ToString().ToLowerInvariant()));

        private static string TriggerText(SkillTrigger t) => t == SkillTrigger.OnBlock ? "on block" : t.ToString();
    }
}
