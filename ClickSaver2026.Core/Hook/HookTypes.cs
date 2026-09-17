namespace ClickSaver2026.Core.Hook;

/// <summary>How far the hook got installing itself in the client.</summary>
public enum HookStatus : ushort
{
    Hooked = 0,
    MessageProtocolNotLoaded = 1,
    ExportNotFound = 2,
    DetourFailed = 3,
}

/// <summary>Extra things the hook can do beyond forwarding messages.</summary>
[Flags]
public enum HookCapabilities : uint
{
    None = 0,

    /// <summary>The Request-missions call is hooked, so the buying agent can roll missions.</summary>
    CanRequestMissions = 1,
}

public enum HookMessageKind : uint
{
    /// <summary>A data block exactly as the client passed it to DataBlockToMessage.</summary>
    IncomingMessage = 1,

    /// <summary>A uint32 count of messages the hook discarded because the app fell behind.</summary>
    Dropped = 2,

    /// <summary>uint32 <see cref="RequestSource"/>, then the MissionGenerateInfo bytes.</summary>
    MissionRequested = 3,

    /// <summary>uint32 command id, uint32 <see cref="CommandStatus"/>.</summary>
    CommandResult = 4,
}

/// <summary>Who asked for a roll: the player at the terminal, or the buying agent.</summary>
public enum RequestSource : uint
{
    Player = 0,
    ClickSaver = 1,
}

public enum CommandStatus : uint
{
    Done = 0,
    NothingRecorded = 1,
    WindowNotFound = 2,
    NotSupported = 3,
    Busy = 4,
    BadCommand = 5,
}

/// <summary>What a hook says about itself when it connects.</summary>
public sealed record HookHello(HookStatus Status, int ProcessId, uint HookVersion, HookCapabilities Capabilities)
{
    public bool CanRequestMissions => this.Capabilities.HasFlag(HookCapabilities.CanRequestMissions);
}

/// <summary>One frame from a hook, or one record read back from a capture file.</summary>
public sealed record HookMessage(int ProcessId, HookMessageKind Kind, DateTime TimestampUtc, byte[] Data);

public static class HookStatusText
{
    public static string Describe(HookStatus status) => status switch
    {
        HookStatus.Hooked => "Hooked",
        HookStatus.MessageProtocolNotLoaded => "MessageProtocol.dll is not loaded in the client",
        HookStatus.ExportNotFound => "DataBlockToMessage is not exported by this client version",
        HookStatus.DetourFailed => "Could not install the hook",
        _ => $"Unknown status {(ushort)status}",
    };
}
