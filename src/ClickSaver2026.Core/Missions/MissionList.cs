namespace ClickSaver2026.Core.Missions;

/// <summary>A reward item: a low and high item template, interpolated to a quality level.</summary>
public sealed record MissionRewardItem(int LowId, int HighId, int Quality);

public sealed record Mission(
    int Instance,
    string ShortDescription,
    string Description,
    int Cash,
    int Experience,
    IReadOnlyList<MissionRewardItem> Rewards,
    int Quality,
    int TypeCode,
    int PlayfieldId,
    float X,
    float Z)
{
    public MissionType Type => MissionTypes.FromCode(this.TypeCode);

    /// <summary>The item a find or return mission is about, read from its description.</summary>
    public string? FindItem => this.Type is MissionType.FindItem or MissionType.ReturnItem
        ? MissionDescriptions.FindItem(this.Description)
        : null;
}

/// <summary>One answer from a mission terminal: the settings it was rolled with and what it offers.</summary>
public sealed record MissionList(
    int Difficulty,
    IReadOnlyList<int> Sliders,
    int TerminalInstance,
    IReadOnlyList<Mission> Missions);

public enum MissionType
{
    Unknown,
    Repair,
    ReturnItem,
    FindPerson,
    FindItem,
    KillPerson,
}

public static class MissionTypes
{
    // The codes ClickSaver 2.x used; they are the mission icon.
    public static MissionType FromCode(int code) => code switch
    {
        0x2C4E => MissionType.Repair,
        0x2C41 => MissionType.ReturnItem,
        0x2C47 => MissionType.FindPerson,
        0x2C49 => MissionType.FindItem,
        0x2C42 => MissionType.KillPerson,
        _ => MissionType.Unknown,
    };

    public static string Describe(MissionType type) => type switch
    {
        MissionType.Repair => "Repair",
        MissionType.ReturnItem => "Return item",
        MissionType.FindPerson => "Find person",
        MissionType.FindItem => "Find item",
        MissionType.KillPerson => "Kill person",
        _ => "Unknown",
    };
}
