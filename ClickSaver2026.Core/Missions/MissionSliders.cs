namespace ClickSaver2026.Core.Missions;

/// <summary>
/// The six mission-terminal sliders, in the order they appear in the roll request, and how a slider
/// percent maps to its wire byte. Each byte is signed: <c>(percent - 50) * 2</c>, so 50% (the
/// default) = 0, 0% (fully left pole) = -100, 100% (fully right pole) = +100.
/// </summary>
public static class MissionSliders
{
    public const int Count = 6;

    /// <summary>Left/right pole labels for each slider, in wire order.</summary>
    public static readonly (string Left, string Right)[] Poles =
    [
        ("Good", "Bad"),
        ("Order", "Chaos"),
        ("Open", "Hidden"),
        ("Physical", "Mystical"),
        ("Head-on", "Stealth"),
        ("Money", "XP"),
    ];

    /// <summary>The signed wire byte for a slider percent (0 = fully left pole, 100 = fully right).</summary>
    public static byte Encode(int percent) => (byte)(sbyte)Math.Clamp((percent - 50) * 2, -100, 100);
}
