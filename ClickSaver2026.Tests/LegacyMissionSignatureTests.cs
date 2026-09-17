using ClickSaver2026.Core;
using ClickSaver2026.Core.Missions;

namespace ClickSaver2026.Tests;

public sealed class LegacyMissionSignatureTests
{
    [Fact]
    public void MatchesTheBytesAOHookTested()
    {
        var message = new byte[0x41];
        message[0x35] = 0xDA;
        message[0x36] = 0xC3;

        Assert.True(LegacyMissionSignature.Matches(message));
    }

    [Fact]
    public void ShortMessagesNeverMatch()
    {
        // AOHook.dll only looked at messages longer than 0x40 bytes.
        var message = new byte[0x40];
        message[0x35] = 0xDA;
        message[0x36] = 0xC3;

        Assert.False(LegacyMissionSignature.Matches(message));
    }

    [Fact]
    public void OtherBytesDoNotMatch()
    {
        var message = new byte[0x80];
        message[0x35] = 0xDA;
        message[0x36] = 0xC4;

        Assert.False(LegacyMissionSignature.Matches(message));
    }

    [Fact]
    public void FindsEveryMarker()
    {
        byte[] message = [0xFF, 0x00, 0x00, 0xDA, 0xC3, 0x00, 0x00, 0xDA, 0xC3, 0x00, 0x00, 0xDA];

        Assert.Equal([1, 5], LegacyMissionSignature.FindMarkers(message));
    }

    [Fact]
    public void HexDumpShowsOffsetsBytesAndText()
    {
        string dump = HexDump.Format("ClickSaver2026!!AB"u8);

        Assert.StartsWith("000000  43 6C 69 63 6B 53 61 76  65 72 32 30 32 36 21 21  ClickSaver2026!!", dump, StringComparison.Ordinal);
        Assert.Contains("000010  41 42", dump, StringComparison.Ordinal);
    }
}
