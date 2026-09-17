namespace ClickSaver2026.Core.Hook;

/// <summary>How far the hook got installing itself in the client.</summary>
public enum HookStatus : ushort
{
    Hooked = 0,
    MessageProtocolNotLoaded = 1,
    ExportNotFound = 2,
    DetourFailed = 3,
}

public enum HookMessageKind : uint
{
    /// <summary>A data block exactly as the client passed it to DataBlockToMessage.</summary>
    IncomingMessage = 1,

    /// <summary>A uint32 count of messages the hook discarded because the app fell behind.</summary>
    Dropped = 2,
}

/// <summary>What a hook says about itself when it connects.</summary>
public sealed record HookHello(HookStatus Status, int ProcessId, uint HookVersion);

/// <summary>One frame from a hook, or one record read back from a capture file.</summary>
public sealed record HookMessage(int ProcessId, HookMessageKind Kind, DateTime TimestampUtc, byte[] Data);

public static class HookStatusText
{
    public static string Describe(HookStatus status) => status switch
    {
        HookStatus.Hooked => "Hooked",
        HookStatus.MessageProtocolNotLoaded => "MessageProtocol.dll is not loaded in the client",
        HookStatus.ExportNotFound => "DataBlockToMessage is not exported by this client version",
        HookStatus.DetourFailed => "Could not install the detour",
        _ => $"Unknown status {(ushort)status}",
    };
}
