using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Tests;

public sealed class MissionListParserTests
{
    private static readonly Mission Single = new(
        0x55D0C9B7,
        "You are just what this node ...",
        "Fight your way inside, pick up the Radioactive Isotope Container, and destroy it safely.",
        5374,
        992,
        [new MissionRewardItem(121762, 121763, 30)],
        30,
        0x2C49,
        687,
        760.7f,
        1154.5f);

    private static readonly Mission Team = Single with
    {
        Instance = 0x55D0C9B8,
        Description = "Welcome!",
        Rewards = [new MissionRewardItem(1, 2, 40), new MissionRewardItem(3, 4, 41), new MissionRewardItem(5, 5, 42)],
        TypeCode = 0x2C42,
    };

    [Fact]
    public void ReadsTheTerminalSettingsAndEveryMission()
    {
        byte[] message = MissionMessageBuilder.Build(6, [11, 70, 1, -35, 92, -2], Single, Team);

        MissionList? list = MissionListParser.TryParse(message);

        Assert.NotNull(list);
        Assert.Equal(6, list.Difficulty);
        Assert.Equal([11, 70, 1, -35, 92, -2], list.Sliders);
        Assert.Equal(2, list.Missions.Count);
        AssertMission(Single, list.Missions[0]);
        AssertMission(Team, list.Missions[1]);
    }

    [Fact]
    public void KeepsTheLegacySignatureAtItsOffset()
    {
        // ClickSaver 2.x found mission lists by the first mission's identity at 0x33.
        Assert.True(LegacyMissionSignature.Matches(MissionMessageBuilder.Build(6, new sbyte[6], Single)));
    }

    [Fact]
    public void OtherMessagesAreNotMissionLists()
    {
        byte[] message = MissionMessageBuilder.Build(6, new sbyte[6], Single);
        message[16] = 0x00;

        Assert.False(MissionListParser.IsMissionList(message));
        Assert.Null(MissionListParser.TryParse(message));
    }

    [Fact]
    public void TruncatedMessageKeepsTheMissionsThatAreWhole()
    {
        byte[] message = MissionMessageBuilder.Build(6, new sbyte[6], Single, Team);

        MissionList? list = MissionListParser.TryParse(message.AsSpan(0, message.Length - 100));

        Mission only = Assert.Single(list!.Missions);
        AssertMission(Single, only);
    }

    [Theory]
    [InlineData("Fight your way inside, pick up the Radioactive Isotope Container, and destroy it safely in your sub-space containment field.", "Radioactive Isotope Container")]
    [InlineData("Last night, one Nano Crystal (Upgraded Android) was stolen from a production facility and brought to Stret East Bank.", "Nano Crystal (Upgraded Android)")]
    [InlineData("I will pay you 13273 credits to get me that Worn Steel-Ribbed Armor Pants! You right-click the item on this mission booth.", "Worn Steel-Ribbed Armor Pants")]
    [InlineData("Somewhere in Galway County you should be able to find the Encrypted Info Capsule.  Please bring it back so we can examine it more closely.", "Encrypted Info Capsule")]
    [InlineData("Welcome! Your service is invaluable.", null)]
    public void FindsTheItemInTheDescription(string description, string? item)
    {
        Assert.Equal(item, MissionDescriptions.FindItem(description));
    }

    [Fact]
    public void OnlyFindAndReturnMissionsHaveAFindItem()
    {
        Assert.Equal("Radioactive Isotope Container", Single.FindItem);
        Assert.Null((Single with { TypeCode = 0x2C42 }).FindItem);
    }

    private static void AssertMission(Mission expected, Mission actual)
    {
        Assert.Equal(expected.Instance, actual.Instance);
        Assert.Equal(expected.ShortDescription, actual.ShortDescription);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.Cash, actual.Cash);
        Assert.Equal(expected.Experience, actual.Experience);
        Assert.Equal(expected.Rewards, actual.Rewards);
        Assert.Equal(expected.Quality, actual.Quality);
        Assert.Equal(expected.TypeCode, actual.TypeCode);
        Assert.Equal(expected.PlayfieldId, actual.PlayfieldId);
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Z, actual.Z);
    }
}
