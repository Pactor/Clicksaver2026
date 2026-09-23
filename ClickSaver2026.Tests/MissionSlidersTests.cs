using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Tests;

public sealed class MissionSlidersTests
{
    [Theory]
    [InlineData(50, 0x00)]   // default, centre
    [InlineData(0, 0x9C)]    // fully left pole = -100
    [InlineData(100, 0x64)]  // fully right pole = +100
    [InlineData(25, 0xCE)]   // -50
    [InlineData(75, 0x32)]   // +50
    public void EncodeMapsPercentToSignedByte(int percent, int expected)
    {
        Assert.Equal((byte)expected, MissionSliders.Encode(percent));
    }

    [Fact]
    public void ThereAreSixSlidersWithPoleLabels()
    {
        Assert.Equal(6, MissionSliders.Count);
        Assert.Equal(MissionSliders.Count, MissionSliders.Poles.Length);
        Assert.Equal(("Money", "XP"), MissionSliders.Poles[5]);
    }
}
