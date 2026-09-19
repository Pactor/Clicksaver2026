using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Tests;

public sealed class MissionMatcherTests
{
    // No database, so area names fall back to "Playfield <id>" and item matches use the find-item.
    private static readonly MissionMatcher Matcher = new(null);

    // A find-item mission whose description names the item (parsed by MissionDescriptions).
    private static Mission Find(string item, int playfield) =>
        new(1, "s", $"Would you please find the {item} in the caves.", 100, 10, [], 100, 0x2C49, playfield, 0, 0);

    private static MissionList List(params Mission[] missions) => new(6, [0, 0, 0, 0, 0, 0], 55, missions);

    [Fact]
    public void ItemWatchAloneMatchesTheFindItem()
    {
        var list = List(Find("Spider Amulet", 800));
        MissionMatch? m = Matcher.FirstMatch(list, WatchList.Parse("spider"), WatchList.Parse(""));
        Assert.NotNull(m);
        Assert.Equal("Spider Amulet", m!.Description);
    }

    [Fact]
    public void AreaWatchAloneMatchesThePlayfield()
    {
        var list = List(Find("Spider Amulet", 800));
        MissionMatch? m = Matcher.FirstMatch(list, WatchList.Parse(""), WatchList.Parse("Playfield 800"));
        Assert.NotNull(m);
        Assert.Equal("a mission in Playfield 800", m!.Description);
    }

    [Fact]
    public void ItemAndAreaMustBothMatchTheSameMission()
    {
        // The item is on the pf 800 mission; the area watch names pf 550 - no single mission has both.
        var list = List(Find("Spider Amulet", 800), Find("Health Kit", 550));
        Assert.Null(Matcher.FirstMatch(list, WatchList.Parse("spider"), WatchList.Parse("Playfield 550")));

        // Both on the pf 800 mission - matches, and the description carries both.
        MissionMatch? m = Matcher.FirstMatch(list, WatchList.Parse("spider"), WatchList.Parse("Playfield 800"));
        Assert.NotNull(m);
        Assert.Equal("Spider Amulet in Playfield 800", m!.Description);
    }

    [Fact]
    public void AreaMissDoesNotMatch()
    {
        var list = List(Find("Spider Amulet", 800));
        Assert.Null(Matcher.FirstMatch(list, WatchList.Parse("spider"), WatchList.Parse("Playfield 999")));
    }

    [Fact]
    public void BothWatchesEmptyMatchesNothing()
    {
        var list = List(Find("Spider Amulet", 800));
        Assert.False(Matcher.Matches(list, WatchList.Parse(""), WatchList.Parse("")));
    }
}
