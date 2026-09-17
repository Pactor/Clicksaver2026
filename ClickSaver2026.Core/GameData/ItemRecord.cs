using System.Buffers.Binary;
using System.Text;

namespace ClickSaver2026.Core.GameData;

/// <summary>The parts of an item that matter for missions.</summary>
public sealed record ItemRecord(int Id, string Name, int Quality, int Value, int IconId);

/// <summary>
/// Reads the start of an item record: three header ints, the attribute block and the name.
/// </summary>
/// <remarks>
/// The layout is the one Algorithman's NewParser.ParseItem reads: int32 x3, key 0x17, a 3F1
/// count, that many (attribute, value) int32 pairs, key 0x15 and 0x21, int16 name length,
/// int16 description length, then the name and description. The rest of the record (functions,
/// actions, requirements) is not needed here and is not read.
/// </remarks>
public static class ItemRecordParser
{
    public const int QualityAttribute = 54;
    public const int ValueAttribute = 74;
    public const int IconAttribute = 79;

    private const int AttributeBlockKey = 0x17;
    private const int NameBlockKey = 0x15;
    private const int NameBlockValue = 0x21;

    /// <summary>The item, or null when the record does not have the expected shape.</summary>
    public static ItemRecord? TryParse(int id, ReadOnlySpan<byte> record)
    {
        int at = 12;
        if (!TryReadInt32(record, ref at, out int key) || key != AttributeBlockKey
            || !TryReadInt32(record, ref at, out int encodedCount) || encodedCount < 1009 || encodedCount % 1009 != 0)
        {
            return null;
        }

        int attributeCount = (encodedCount / 1009) - 1;
        if (attributeCount > (record.Length - at) / 8)
        {
            return null;
        }

        int quality = 0, value = 0, icon = 0;
        for (int i = 0; i < attributeCount; i++)
        {
            int attribute = BinaryPrimitives.ReadInt32LittleEndian(record[at..]);
            int attributeValue = BinaryPrimitives.ReadInt32LittleEndian(record[(at + 4)..]);
            at += 8;
            switch (attribute)
            {
                case QualityAttribute:
                    quality = attributeValue;
                    break;
                case ValueAttribute:
                    value = attributeValue;
                    break;
                case IconAttribute:
                    icon = attributeValue;
                    break;
            }
        }

        if (!TryReadInt32(record, ref at, out int nameKey) || nameKey != NameBlockKey
            || !TryReadInt32(record, ref at, out int nameValue) || nameValue != NameBlockValue
            || record.Length - at < 4)
        {
            return null;
        }

        int nameLength = BinaryPrimitives.ReadInt16LittleEndian(record[at..]);
        at += 4; // name length, then description length
        if (nameLength < 0 || nameLength > record.Length - at)
        {
            return null;
        }

        string name = Encoding.Latin1.GetString(record.Slice(at, nameLength)).TrimEnd('\0');
        return new ItemRecord(id, name, quality, value, icon);
    }

    private static bool TryReadInt32(ReadOnlySpan<byte> record, ref int at, out int value)
    {
        if (record.Length - at < 4)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(record[at..]);
        at += 4;
        return true;
    }
}
