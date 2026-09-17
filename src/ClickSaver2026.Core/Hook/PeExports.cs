using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ClickSaver2026.Core.Hook;

/// <summary>Looks up exports in a DLL on disk, without loading it.</summary>
public static class PeExports
{
    /// <summary>The relative virtual address of a named export.</summary>
    /// <exception cref="EntryPointNotFoundException">The DLL does not export <paramref name="name"/>.</exception>
    public static uint GetExportRva(string path, string name)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);

        DirectoryEntry directory = pe.PEHeaders.PEHeader?.ExportTableDirectory
            ?? throw new BadImageFormatException($"{path} has no optional header.");
        if (directory.RelativeVirtualAddress == 0)
        {
            throw new EntryPointNotFoundException($"{path} exports nothing.");
        }

        int exports = directory.RelativeVirtualAddress;
        uint nameCount = ReadUInt32(pe, exports + 0x18);
        int functions = (int)ReadUInt32(pe, exports + 0x1C);
        int names = (int)ReadUInt32(pe, exports + 0x20);
        int ordinals = (int)ReadUInt32(pe, exports + 0x24);

        for (int i = 0; i < nameCount; i++)
        {
            int nameRva = (int)ReadUInt32(pe, names + (4 * i));
            if (ReadAsciiZ(pe, nameRva) != name)
            {
                continue;
            }

            ushort ordinal = BinaryPrimitives.ReadUInt16LittleEndian(Read(pe, ordinals + (2 * i), 2));
            uint rva = ReadUInt32(pe, functions + (4 * ordinal));
            if (rva >= directory.RelativeVirtualAddress && rva < directory.RelativeVirtualAddress + directory.Size)
            {
                throw new EntryPointNotFoundException($"{name} in {path} is forwarded to another DLL.");
            }

            return rva;
        }

        throw new EntryPointNotFoundException($"{path} does not export {name}.");
    }

    private static ReadOnlySpan<byte> Read(PEReader pe, int rva, int length) =>
        pe.GetSectionData(rva).GetContent(0, length).AsSpan();

    private static uint ReadUInt32(PEReader pe, int rva) =>
        BinaryPrimitives.ReadUInt32LittleEndian(Read(pe, rva, 4));

    private static string ReadAsciiZ(PEReader pe, int rva)
    {
        ReadOnlySpan<byte> rest = pe.GetSectionData(rva).GetContent().AsSpan();
        int end = rest.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? rest : rest[..end]);
    }
}
