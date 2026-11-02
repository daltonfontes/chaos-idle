using ChaosIdle.App;
using ChaosIdle.Content;
using ChaosIdle.Domain.PassiveTree;
using Xunit;

namespace ChaosIdle.Tests;

public class GameSessionBuildTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chaos-idle-build-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 7, 2, 18, 0, 0, DateTimeKind.Utc);
    private ulong _seed = 1;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private GameSession Open() => GameSession.LoadOrCreate(_dir, Now, () => _seed++);

    [Fact]
    public void Tree_view_shows_every_node_with_its_state_and_reason()
    {
        var view = Open().PassiveTree();

        Assert.Equal(PassiveTrees.Guardian.Nodes.Count, view.Nodes.Count);
        Assert.Equal(StarterCharacters.StartingPassivePoints, view.PointsSpent);
        Assert.Equal(0, view.PointsRemaining);
        Assert.Equal("Start", view.Nodes[0].Branch);

        var frame = view.Nodes.Single(n => n.Id == "pas.def.frame1");
        Assert.Equal(PassiveState.Allocated, frame.State);
        Assert.Contains("depend on it", frame.Reason);

        var kindling = view.Nodes.Single(n => n.Id == "pas.spl.kindling");
        Assert.Equal(PassiveState.Locked, kindling.State);
        Assert.Contains("No passive points left", kindling.Reason);

        var focus = view.Nodes.Single(n => n.Id == "pas.spl.focus");
        Assert.Contains("No passive points", focus.Reason); // points are checked before connection
        Assert.All(view.Nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.Reason)));
        Assert.Equal("Keystones", view.Nodes.Single(n => n.Id == "pas.key.bastion").Branch);
    }

    [Fact]
    public void Refund_then_allocate_elsewhere_and_it_is_saved()
    {
        var s = Open();
        Assert.Contains("Other passives depend on it", s.RefundPassive("pas.def.frame1", Now));
        Assert.StartsWith("Refunded Emberward. 1 point(s) left.", s.RefundPassive("pas.wrd.ember", Now));

        var view = s.PassiveTree();
        Assert.Equal(PassiveState.Available, view.Nodes.Single(n => n.Id == "pas.spl.kindling").State);
        Assert.Contains("Not connected", s.AllocatePassive("pas.spl.focus", Now));
        Assert.StartsWith("Took Kindling. 0 point(s) left.", s.AllocatePassive("pas.spl.kindling", Now));

        var reloaded = Open();
        Assert.True(reloaded.Character.Passives.IsAllocated("pas.spl.kindling"));
        Assert.False(reloaded.Character.Passives.IsAllocated("pas.wrd.ember"));
    }

    [Fact]
    public void Passive_preview_shows_the_effect_without_changing_anything()
    {
        var s = Open();
        var before = s.Character.Passives;
        var lines = s.PreviewPassive("pas.wrd.ember");

        Assert.Equal("Emberward (Minor)", lines[0]);
        Assert.Contains("If you refund it:", lines);
        Assert.Contains(lines, l => l.Contains("▼ worse") && l.Contains("Fire resistance"));
        Assert.Same(before, s.Character.Passives);

        Assert.Contains(s.PreviewPassive("pas.spl.focus"), l => l.Contains("No passive points left"));
        Assert.Throws<ArgumentException>(() => s.PreviewPassive("pas.nope"));
    }

    [Fact]
    public void Skills_screen_lists_skills_sockets_and_support_fit()
    {
        var s = Open();
        var skills = s.SkillsInUse();
        Assert.Equal(new[] { "skill.iron_cleave", "skill.bulwark_retort" }, skills.Select(k => k.Id));
        Assert.True(skills[0].IsPrimary);
        Assert.Equal("attack", skills[0].Kind);
        Assert.Equal("triggered (on block)", skills[1].Kind);
        Assert.Equal(3, skills[0].FreeSockets);

        var options = s.SupportOptions("skill.iron_cleave");
        Assert.Equal(Supports.All.Length, options.Count);
        var focused = options.Single(o => o.Id == "sup.focused_intent");
        Assert.Equal(SupportStatus.Incompatible, focused.Status);
        Assert.Equal("Does not fit Iron Cleave: it needs spell skills.", focused.Reason);
        Assert.Equal(SupportStatus.Incompatible, s.SupportOptions("skill.bulwark_retort").Single(o => o.Id == "sup.frugal_casting").Status);
        Assert.Contains("cannot support triggered", s.SupportOptions("skill.bulwark_retort").Single(o => o.Id == "sup.frugal_casting").Reason);
    }

    [Fact]
    public void Link_and_unlink_supports_with_sockets_enforced_and_saved()
    {
        var s = Open();
        Assert.Equal("Linked Heavy Impact to Iron Cleave.", s.LinkSupport("skill.iron_cleave", "sup.heavy_impact", Now));
        s.LinkSupport("skill.iron_cleave", "sup.siphon", Now);
        s.LinkSupport("skill.iron_cleave", "sup.keen_edge", Now);
        Assert.Contains("no free socket", s.LinkSupport("skill.iron_cleave", "sup.sundering_focus", Now));
        Assert.Equal(SupportStatus.NoFreeSocket, s.SupportOptions("skill.iron_cleave").Single(o => o.Id == "sup.sundering_focus").Status);
        Assert.Contains("needs spell", s.LinkSupport("skill.iron_cleave", "sup.focused_intent", Now));

        Assert.Equal("Unlinked Siphon from Iron Cleave.", s.UnlinkSupport("skill.iron_cleave", "sup.siphon", Now));
        Assert.Equal("That support is not linked to this skill.", s.UnlinkSupport("skill.iron_cleave", "sup.siphon", Now));

        var reloaded = Open();
        Assert.Equal(new[] { "sup.heavy_impact", "sup.keen_edge" }, reloaded.Character.SupportsFor(Skills.IronCleave).Select(x => x.Id));
        Assert.Throws<ArgumentException>(() => s.SupportOptions("skill.cinder_lance"));
    }

    [Fact]
    public void Support_preview_shows_its_cost_and_benefit()
    {
        var s = Open();
        var lines = s.PreviewSupport("skill.iron_cleave", "sup.heavy_impact");
        Assert.Contains("If you link it to Iron Cleave:", lines);
        Assert.Contains(lines, l => l.Contains("▲ better") && l.Contains("Damage per second"));
        Assert.Contains(lines, l => l.Contains("Actions per second") && l.Contains("▼ worse"));
        Assert.Empty(s.Character.SupportsFor(Skills.IronCleave));

        Assert.Contains(s.PreviewSupport("skill.iron_cleave", "sup.focused_intent"), l => l.Contains("needs spell"));
        Assert.Contains(s.PreviewSupport("skill.bulwark_retort", "sup.ready_retort"), l => l.Contains("Triggered skills are not on the character sheet"));
    }

    [Fact]
    public void Switching_the_main_skill_keeps_each_skills_supports()
    {
        var s = Open();
        s.LinkSupport("skill.iron_cleave", "sup.heavy_impact", Now);
        Assert.Equal("Cinder Lance is now your main skill.", s.SetPrimarySkill("skill.cinder_lance", Now));
        Assert.Contains(s.PrimarySkillChoices(), c => c.Id == "skill.cinder_lance" && c.IsCurrent);
        Assert.DoesNotContain(s.PrimarySkillChoices(), c => c.Id == "skill.bulwark_retort");
        Assert.Equal("spell", s.SkillsInUse()[0].Kind);
        Assert.Empty(s.Character.ToBuild().Validate());

        var reloaded = Open(); // the Iron Cleave links must survive a save while Cinder Lance is in use
        reloaded.SetPrimarySkill("skill.iron_cleave", Now);
        Assert.Equal(new[] { "sup.heavy_impact" }, reloaded.SkillsInUse()[0].Linked.Select(l => l.Id));
        Assert.Throws<ArgumentException>(() => s.SetPrimarySkill("skill.bulwark_retort", Now));
    }
}
