using System.Buffers.Binary;

namespace ClickSaver2026.Core.Hook;

/// <summary>
/// The wire format between the hook DLL and the app. Mirrors src/ClickSaver2026.Hook/protocol.h;
/// change both together.
/// </summary>
/// <remarks>
/// The hook connects, sends one 20-byte hello, then a stream of frames: a 16-byte header
/// (kind, payload length, FILETIME timestamp) followed by the payload. Little-endian throughout.
/// </remarks>
public static class HookProtocol
{
    public const string PipeName = "ClickSaver2026";

    public const uint HelloMagic = 0x36325343; // "CS26"
    public const ushort ProtocolVersion = 1;
    public const int HelloSize = 20;
    public const int FrameHeaderSize = 16;
    public const int MaxMessageSize = 4 * 1024 * 1024;

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
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]));
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
}
