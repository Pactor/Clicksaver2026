using System.Buffers.Binary;
using ClickSaver2026.Core.Hook;

namespace ClickSaver2026.Core.Capture;

/// <summary>
/// The capture file format: an 16-byte header ("CS26CAP\0", uint16 version, 6 reserved bytes),
/// then records of int64 FILETIME (UTC), uint32 process id, uint32 kind, uint32 length and the
/// payload. Little-endian throughout.
/// </summary>
public static class CaptureFormat
{
    public const string Extension = ".cs26cap";
    public const ushort Version = 1;
    public const int HeaderSize = 16;
    public const int RecordHeaderSize = 20;

    internal static ReadOnlySpan<byte> Magic => "CS26CAP\0"u8;
}

/// <summary>Appends hook messages to a capture file. Safe to call from several threads.</summary>
public sealed class CaptureWriter : IDisposable
{
    private readonly FileStream stream;
    private readonly Lock gate = new();
    private readonly byte[] recordHeader = new byte[CaptureFormat.RecordHeaderSize];

    public CaptureWriter(string path)
    {
        this.Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        this.stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16);

        Span<byte> header = stackalloc byte[CaptureFormat.HeaderSize];
        header.Clear();
        CaptureFormat.Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], CaptureFormat.Version);
        this.stream.Write(header);
        this.stream.Flush();
    }

    public string Path { get; }

    public long MessageCount { get; private set; }

    public void Write(HookMessage message)
    {
        lock (this.gate)
        {
            Span<byte> header = this.recordHeader;
            BinaryPrimitives.WriteInt64LittleEndian(header, message.TimestampUtc.ToFileTimeUtc());
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)message.ProcessId);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)message.Kind);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)message.Data.Length);
            this.stream.Write(header);
            this.stream.Write(message.Data);

            // Flushed per record so a crash in the client or the app loses nothing already seen.
            this.stream.Flush();
            this.MessageCount++;
        }
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.stream.Dispose();
        }
    }
}

public static class CaptureReader
{
    public static IEnumerable<HookMessage> Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var header = new byte[CaptureFormat.HeaderSize];
        stream.ReadExactly(header);
        if (!header.AsSpan(0, 8).SequenceEqual(CaptureFormat.Magic))
        {
            throw new InvalidDataException($"{path} is not a ClickSaver2026 capture.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
        if (version != CaptureFormat.Version)
        {
            throw new InvalidDataException($"{path} is capture version {version}; this build reads {CaptureFormat.Version}.");
        }

        var record = new byte[CaptureFormat.RecordHeaderSize];
        while (true)
        {
            int read = stream.ReadAtLeast(record, record.Length, throwOnEndOfStream: false);
            if (read < record.Length)
            {
                // A record cut short by a crash ends the capture.
                yield break;
            }

            var timestamp = DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(record));
            int processId = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8));
            var kind = (HookMessageKind)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(12));
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16));
            if (length > HookProtocol.MaxMessageSize)
            {
                throw new InvalidDataException($"{path} has a record of {length} bytes, larger than any message.");
            }

            var data = new byte[length];
            if (stream.ReadAtLeast(data, length, throwOnEndOfStream: false) < length)
            {
                yield break;
            }

            yield return new HookMessage(processId, kind, timestamp, data);
        }
    }
}
