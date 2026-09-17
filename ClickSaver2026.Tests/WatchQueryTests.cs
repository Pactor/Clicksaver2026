using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Tests;

public sealed class WatchQueryTests
{
    [Theory]
    [InlineData("decus", "Primus Decus Armor Boots", true)]
    [InlineData("decus armor", "Primus Decus Armor Boots", true)]
    [InlineData("decus armor", "Decus Body Boots", false)]     // "armor" missing
    [InlineData("decus -gloves", "Decus Armor Boots", true)]
    [InlineData("decus -gloves", "Decus Armor Gloves", false)] // excluded
    [InlineData("\"decus armor\"", "Decus Armor Boots", true)]
    [InlineData("\"decus armor\"", "Decus Body Armor", false)] // phrase not contiguous
    [InlineData("AXE", "Cast-Off Mini Axe", true)]             // case insensitive
    public void MatchesLikeClickSaver(string query, string text, bool expected)
    {
        Assert.Equal(expected, WatchQuery.Parse(query).Matches(text));
    }

    [Fact]
    public void EmptyQueryMatchesNothing()
    {
        Assert.True(WatchQuery.Parse("").IsEmpty);
        Assert.True(WatchQuery.Parse("   ").IsEmpty);
        Assert.False(WatchQuery.Parse("").Matches("anything"));
    }

    [Fact]
    public void MatchesAnyOfSeveralTexts()
    {
        WatchQuery query = WatchQuery.Parse("plasteel");

        Assert.True(query.MatchesAny(["Cast-Off Mini Axe", "Inferior Vito's Plasteel Armor Gloves"]));
        Assert.False(query.MatchesAny(["Cast-Off Mini Axe", "Loose Blackjack"]));
    }
}
