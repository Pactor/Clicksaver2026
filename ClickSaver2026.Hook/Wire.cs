// The wire format between the hook and the ClickSaver2026 app.
// Mirrors ClickSaver2026.Core/Hook/HookProtocol.cs - change both together.
//
// Two pipes per hooked client:
//   <name>           hook -> app: a Hello, then frames (messages, recorded requests, results)
//   <name>.commands  app -> hook: after the hook writes its uint32 process id, commands

namespace ClickSaver2026.Hook;

internal static class Wire
{
    public const string DefaultPipeName = "ClickSaver2026";
    public const string CommandPipeSuffix = ".commands";
    public const string PipeNameVariable = "CLICKSAVER2026_PIPE";

    public const uint HelloMagic = 0x36325343; // "CS26"
    public const ushort ProtocolVersion = 2;
    public const uint HookVersion = 2;

    public const int MaxMessageSize = 4 * 1024 * 1024;

    // MissionGenerateInfo_t, as n3EngineClientAnarchy_t::N3Msg_GenerateMissions reads it:
    // +0x00 difficulty slider, +0x04..+0x18 the six mission sliders, +0x1C originator,
    // +0x20 the terminal's Identity.
    public const int MissionGenerateInfoSize = 0x28;
}

internal enum HookStatus : ushort
{
    Hooked = 0,
    MessageProtocolNotLoaded = 1,
    ExportNotFound = 2,
    DetourFailed = 3,
}

[System.Flags]
internal enum HookCapabilities : uint
{
    None = 0,
    CanRequestMissions = 1,
}

internal enum FrameKind : uint
{
    IncomingMessage = 1,
    Dropped = 2,
    MissionRequested = 3,
    CommandResult = 4,
}

internal enum RequestSource : uint
{
    Player = 0,
    ClickSaver = 1,
}

internal enum CommandKind : uint
{
    RequestMissions = 1,
}

internal enum CommandStatus : uint
{
    Done = 0,
    NothingRecorded = 1,
    WindowNotFound = 2,
    NotSupported = 3,
    Busy = 4,
    BadCommand = 5,
}
