using System.Buffers.Binary;
using System.Text;
using ClickSaver2026.Core.GameData;
using ClickSaver2026.Core.Settings;

namespace ClickSaver2026.Tests;

public sealed class GameDataTests
{
    [Fact]
    public void ItemRecordGivesNameQualityValueAndIcon()
    {
        byte[] record = ItemRecordBytes("Cast-Off Mini Axe", (54, 30), (74, 15798), (79, 13345), (1, 2));

        ItemRecord? item = ItemRecordParser.TryParse(121762, record);

        Assert.Equal(new ItemRecord(121762, "Cast-Off Mini Axe", 30, 15798, 13345), item);
    }

    [Fact]
    public void ItemRecordWithTheWrongShapeIsRefused()
    {
        byte[] record = ItemRecordBytes("Anything", (54, 1));
        record[12] = 0x18; // not the attribute block key

        Assert.Null(ItemRecordParser.TryParse(1, record));
        Assert.Null(ItemRecordParser.TryParse(1, record.AsSpan(0, 10)));
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), "ClickSaver2026.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            new AppSettings { ClientFolder = @"D:\Games\Anarchy Online" }.Save(path);

            Assert.Equal(@"D:\Games\Anarchy Online", AppSettings.Load(path).ClientFolder);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void MissingOrBrokenSettingsGiveDefaults()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ClickSaver2026.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string broken = Path.Combine(folder, "broken.json");
            File.WriteAllText(broken, "{ not json");

            Assert.Null(AppSettings.Load(Path.Combine(folder, "missing.json")).ClientFolder);
            Assert.Null(AppSettings.Load(broken).ClientFolder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // Three header ints, key 0x17, a 3F1 count and the pairs, then 0x15 0x21 and the name.
    private static byte[] ItemRecordBytes(string name, params (int Attribute, int Value)[] attributes)
    {
        var bytes = new List<byte>();
        void Int32(int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            bytes.AddRange(buffer);
        }

        Int32(1);
        Int32(2);
        Int32(3);
        Int32(0x17);
        Int32((attributes.Length + 1) * 1009);
        foreach (var (attribute, value) in attributes)
        {
            Int32(attribute);
            Int32(value);
        }

        Int32(0x15);
        Int32(0x21);
        byte[] text = Encoding.Latin1.GetBytes(name);
        bytes.AddRange(BitConverter.GetBytes((short)text.Length));
        bytes.AddRange(BitConverter.GetBytes((short)0));
        bytes.AddRange(text);
        return [.. bytes];
    }
}
