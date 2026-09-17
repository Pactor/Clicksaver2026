using System.Buffers.Binary;

namespace ClickSaver2026.Core.Hook;

/// <summary>
/// The wire format between the hook DLL and the app. Mirrors ClickSaver2026.Hook/protocol.h;
/// change both together.
/// </summary>
/// <remarks>
/// The hook connects, sends one 20-byte hello, then a stream of frames: a 16-byte header
/// (kind, payload length, FILETIME timestamp) followed by the payload. On the command pipe
/// (<see cref="CommandPipeName"/>) the hook writes its uint32 process id, then reads commands:
/// a 12-byte header (kind, id, length) and that many payload bytes. Little-endian throughout.
/// </remarks>
public static class HookProtocol
{
    public const string PipeName = "ClickSaver2026";
    public const string CommandPipeName = "ClickSaver2026.commands";

    public const uint HelloMagic = 0x36325343; // "CS26"
    public const ushort ProtocolVersion = 2;
    public const int HelloSize = 20;
    public const int FrameHeaderSize = 16;
    public const int CommandHeaderSize = 12;
    public const int MaxMessageSize = 4 * 1024 * 1024;

    /// <summary>MissionGenerateInfo_t: difficulty, six sliders, originator, terminal Identity.</summary>
    public const int MissionGenerateInfoSize = 0x28;

    public const uint RequestMissionsCommand = 1;

    public static HookHello ParseHello(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HelloSize)
        {
            throw new InvalidDataException("The hook's hello is too short.");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (magic != HelloMagic)
        {
            throw new InvalidDataException($"Not a ClickSaver2026 hook (magic 0x{magic:X8}).");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        if (version != ProtocolVersion)
        {
            throw new InvalidDataException($"The hook speaks protocol {version}; this app speaks {ProtocolVersion}. Rebuild both together.");
        }

        return new HookHello(
            (HookStatus)BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
            (HookCapabilities)BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]));
    }

    public static (HookMessageKind Kind, int Length, DateTime TimestampUtc) ParseFrameHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FrameHeaderSize)
        {
            throw new InvalidDataException("Frame header is too short.");
        }

        var kind = (HookMessageKind)BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (length > MaxMessageSize)
        {
            throw new InvalidDataException($"Frame of {length} bytes is larger than the {MaxMessageSize} the hook sends.");
        }

        long fileTime = BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]);
        return (kind, (int)length, DateTime.FromFileTimeUtc(fileTime));
    }

    public static byte[] EncodeHello(HookHello hello)
    {
        var bytes = new byte[HelloSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, HelloMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)hello.Status);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)hello.ProcessId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), hello.HookVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)hello.Capabilities);
        return bytes;
    }

    public static byte[] EncodeFrame(HookMessageKind kind, DateTime timestampUtc, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[FrameHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)kind);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), timestampUtc.ToFileTimeUtc());
        payload.CopyTo(bytes.AsSpan(FrameHeaderSize));
        return bytes;
    }

    /// <summary>A command for the hook: a 12-byte header (kind, id, length) then the payload.</summary>
    public static byte[] EncodeCommand(uint kind, uint id, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[CommandHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, kind);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)payload.Length);
        payload.CopyTo(bytes.AsSpan(CommandHeaderSize));
        return bytes;
    }

    /// <summary>A MissionRequested frame's source and its MissionGenerateInfo bytes.</summary>
    public static (RequestSource Source, byte[] Info) ParseMissionRequested(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4 + MissionGenerateInfoSize)
        {
            throw new InvalidDataException("MissionRequested frame is too short.");
        }

        var source = (RequestSource)BinaryPrimitives.ReadUInt32LittleEndian(payload);
        return (source, payload.Slice(4, MissionGenerateInfoSize).ToArray());
    }

    /// <summary>A CommandResult frame's command id and status.</summary>
    public static (uint Id, CommandStatus Status) ParseCommandResult(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            throw new InvalidDataException("CommandResult frame is too short.");
        }

        return (BinaryPrimitives.ReadUInt32LittleEndian(payload), (CommandStatus)BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]));
    }
}
