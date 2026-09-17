using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace ClickSaver2026.Core.GameData;

/// <summary>
/// Read-only access to the client's ResourceDatabase: the .idx index plus ResourceDatabase.dat,
/// .dat.001, .dat.002, ..., which are one logical stream split into parts.
/// </summary>
/// <remarks>
/// Adapted from AssetDecoder's ResourceDatabase (OmniCell/Tools/AssetDecoder, GPL-3.0). Files are
/// opened only for the length of a read, sharing read, write and delete, so nothing is held open
/// while the client starts, runs or patches. Safe to read from several threads at once.
/// </remarks>
public sealed class ResourceDatabase : IDisposable
{
    // Every record in the data files starts with this many header bytes.
    private const int RecordHeaderSize = 34;

    private readonly List<string> dataFiles = [];
    private readonly Dictionary<int, Dictionary<int, long>> offsets = [];
    private readonly long partSize;
    private readonly long partHeaderSize;

    public ResourceDatabase(string databaseFolder)
    {
        string indexPath = Path.Combine(databaseFolder, "ResourceDatabase.idx");
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("No ResourceDatabase.idx in " + databaseFolder, indexPath);
        }

        // .dat, then .dat.001, .dat.002, ... in order.
        IEnumerable<string> parts = Directory.GetFiles(databaseFolder, "ResourceDatabase.dat*")
            .Where(path => path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || char.IsAsciiDigit(path[^1]))
            .Order(StringComparer.OrdinalIgnoreCase);
        this.dataFiles.AddRange(parts);

        byte[] index;
        using (SafeFileHandle indexFile = OpenShared(indexPath))
        {
            index = new byte[RandomAccess.GetLength(indexFile)];
            RandomAccess.Read(indexFile, index, 0);
        }

        this.partHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(12));
        this.partSize = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(184));

        // The index is a chain of blocks. Each block: next-block offset, 4 unknown bytes,
        // entry count (int16), 18 unknown bytes, then 16-byte entries of
        // (data offset high/low, little-endian) and (type, instance, big-endian).
        uint block = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(72));
        uint next = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan((int)block));
        while (next > 0)
        {
            int count = BinaryPrimitives.ReadInt16LittleEndian(index.AsSpan((int)block + 8));
            int entry = (int)block + 28;
            for (int i = 0; i < count; i++, entry += 16)
            {
                long offset = ((long)BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(entry)) << 32)
                    | BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(entry + 4));
                int type = BinaryPrimitives.ReadInt32BigEndian(index.AsSpan(entry + 8));
                int instance = BinaryPrimitives.ReadInt32BigEndian(index.AsSpan(entry + 12));
                if (!this.offsets.TryGetValue(type, out Dictionary<int, long>? instances))
                {
                    instances = [];
                    this.offsets.Add(type, instances);
                }

                instances[instance] = offset;
            }

            block = next;
            next = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan((int)block));
        }
    }

    /// <summary>The database folder of a client install, the one containing cd_image.</summary>
    public static string FolderOf(string clientFolder) => Path.Combine(clientFolder, "cd_image", "data", "db");

    public IReadOnlyCollection<int> Types => this.offsets.Keys;

    public int Count(int type) => this.offsets.TryGetValue(type, out var instances) ? instances.Count : 0;

    public IEnumerable<int> Instances(int type) =>
        this.offsets.TryGetValue(type, out var instances) ? instances.Keys.Order() : [];

    public bool Contains(int type, int instance) =>
        this.offsets.TryGetValue(type, out var instances) && instances.ContainsKey(instance);

    /// <summary>A record's payload, or null when the database has no such record.</summary>
    public byte[]? TryRead(int type, int instance)
    {
        if (!this.offsets.TryGetValue(type, out var instances) || !instances.TryGetValue(instance, out long offset))
        {
            return null;
        }

        byte[] header = this.ReadAt(offset, RecordHeaderSize);
        int headerType = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(10));
        int headerInstance = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(14));
        if (headerType != type || headerInstance != instance)
        {
            throw new InvalidDataException($"Record {type}:{instance} points at a header for {headerType}:{headerInstance}.");
        }

        // The size field counts 12 header bytes that precede the payload.
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(18)) - 12;
        if (length < 0)
        {
            throw new InvalidDataException($"Record {type}:{instance} has a negative length.");
        }

        return this.ReadAt(offset + RecordHeaderSize, length);
    }

    public void Dispose()
    {
        // Nothing is held open between reads.
    }

    // Offsets are logical: every part after the first repeats a header of partHeaderSize bytes,
    // so part n holds logical bytes [n * (partSize - partHeaderSize), ...) starting at partHeaderSize.
    private byte[] ReadAt(long logicalOffset, int length)
    {
        byte[] buffer = new byte[length];
        int part = (int)(logicalOffset / this.partSize);
        long position = logicalOffset - (part * (this.partSize - this.partHeaderSize));
        int filled = 0;
        while (filled < length)
        {
            if (part >= this.dataFiles.Count)
            {
                throw new EndOfStreamException("Record runs past the last data file.");
            }

            int read;
            using (SafeFileHandle file = OpenShared(this.dataFiles[part]))
            {
                read = RandomAccess.Read(file, buffer.AsSpan(filled), position);
            }

            filled += read;
            if (filled < length)
            {
                part++;
                position = this.partHeaderSize;
            }
        }

        return buffer;
    }

    private static SafeFileHandle OpenShared(string path) =>
        File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
