using ChaosIdle.Content;
using Xunit;

namespace ChaosIdle.Tests;

public class ContentTests
{
    [Fact]
    public void All_content_ids_are_unique_and_namespaced()
    {
        var ids = Skills.All.Select(s => s.Id)
            .Concat(Weapons.All.Select(w => w.Id))
            .Concat(Monsters.All.Select(m => m.Id))
            .Concat(Monsters.All.SelectMany(m => m.Attacks.Select(a => a.Id)))
            .Concat(Encounters.All.Select(e => e.Id))
            .Concat(SampleBuilds.All.Select(b => b.Id))
            .Append(Classes.Guardian.Id)
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(@"^[a-z]+\.[a-z0-9_.]+$", id));
    }

    [Fact]
    public void All_content_validates()
    {
        Assert.All(Skills.All, s => Assert.Empty(s.Validate()));
        Assert.All(Monsters.All, m => Assert.Empty(m.Validate()));
        Assert.All(SampleBuilds.All, b => Assert.Empty(b.Validate()));
    }
}
