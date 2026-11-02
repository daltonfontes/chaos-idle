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
        string Id, string Name, string Description, PassiveNodeKind Kind, string Branch, PassiveState State, string Reason);

    public sealed record PassiveTreeView(int PointsSpent, int PointsTotal, IReadOnlyList<PassiveNodeView> Nodes)
    {
        public int PointsRemaining => PointsTotal - PointsSpent;
    }

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
        private static readonly string[] BranchOrder = { "Start", "Defence", "Melee", "Spell", "Wards", "Keystones" };

        // ---- Passive tree ----

        public PassiveTreeView PassiveTree()
        {
            var a = Character.Passives;
            var nodes = a.Tree.Nodes
                .Select(n =>
                {
                    if (a.IsAllocated(n.Id))
                    {
                        string why = n.Id == a.Tree.StartNodeId ? "Your starting point." : RefundReason(a.CanRefund(n.Id));
                        return new PassiveNodeView(n.Id, n.Name, n.Description, n.Kind, Branch(n), PassiveState.Allocated, why);
                    }

                    var error = a.CanAllocate(n.Id);
                    return new PassiveNodeView(n.Id, n.Name, n.Description, n.Kind, Branch(n),
                        error == AllocationError.None ? PassiveState.Available : PassiveState.Locked, AllocateReason(error));
                })
                .OrderBy(n => Array.IndexOf(BranchOrder, n.Branch))
                .ThenBy(n => n.State)
                .ThenBy(n => n.Id, StringComparer.Ordinal)
                .ToList();
            return new PassiveTreeView(a.PointsSpent, a.TotalPoints, nodes);
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
            if (error != AllocationError.None) return AllocateReason(error);
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

        private static string Branch(PassiveNode n)
        {
            if (n.Kind == PassiveNodeKind.Start) return "Start";
            if (n.Kind == PassiveNodeKind.Keystone) return "Keystones";
            if (n.Id.StartsWith("pas.def.", StringComparison.Ordinal)) return "Defence";
            if (n.Id.StartsWith("pas.mel.", StringComparison.Ordinal)) return "Melee";
            if (n.Id.StartsWith("pas.spl.", StringComparison.Ordinal)) return "Spell";
            if (n.Id.StartsWith("pas.wrd.", StringComparison.Ordinal)) return "Wards";
            return "Other";
        }

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
