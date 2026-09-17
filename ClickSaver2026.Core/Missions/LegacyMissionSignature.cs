namespace ClickSaver2026.Core.Missions;

/// <summary>
/// How ClickSaver 2.x recognised a mission list. Kept to check against live captures whether
/// that still holds; the rewritten parser is built from what the captures show.
/// </summary>
public static class LegacyMissionSignature
{
    /// <summary>The offset AOHook.dll tested, unchanged since the 14.4.0.2 client.</summary>
    public const int Offset = 0x33;

    // AOHook.dll compared the uint32 at Offset with 0xC3DA0000; mission.c then scanned for the
    // big-endian 0x0000DAC3 that starts each mission. Both are these four bytes.
    private static ReadOnlySpan<byte> Marker => [0x00, 0x00, 0xDA, 0xC3];

    /// <summary>True when AOHook.dll would have forwarded this message as a mission list.</summary>
    public static bool Matches(ReadOnlySpan<byte> message) =>
        message.Length > 0x40 && message.Slice(Offset, Marker.Length).SequenceEqual(Marker);

    /// <summary>Every offset where the marker occurs, which is where mission.c looked for missions.</summary>
    public static IReadOnlyList<int> FindMarkers(ReadOnlySpan<byte> message)
    {
        var offsets = new List<int>();
        int start = 0;
        int found;
        while ((found = message[start..].IndexOf(Marker)) >= 0)
        {
            offsets.Add(start + found);
            start += found + 1;
        }

        return offsets;
    }
}
