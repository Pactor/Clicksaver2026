using System.Text;

namespace ClickSaver2026.Core;

public static class HexDump
{
    /// <summary>Classic 16-per-line dump with offsets and printable ASCII.</summary>
    public static string Format(ReadOnlySpan<byte> data, int maxBytes = int.MaxValue)
    {
        int length = Math.Min(data.Length, maxBytes);
        var text = new StringBuilder((length / 16 + 2) * 76);
        for (int line = 0; line < length; line += 16)
        {
            ReadOnlySpan<byte> row = data.Slice(line, Math.Min(16, length - line));
            text.Append(line.ToString("X6", System.Globalization.CultureInfo.InvariantCulture)).Append("  ");
            for (int i = 0; i < 16; i++)
            {
                text.Append(i < row.Length ? row[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture) : "  ");
                text.Append(i == 7 ? "  " : " ");
            }

            text.Append(' ');
            foreach (byte b in row)
            {
                text.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            text.AppendLine();
        }

        if (data.Length > length)
        {
            text.Append("... ").Append(data.Length - length).AppendLine(" more bytes");
        }

        return text.ToString();
    }

    /// <summary>The first bytes as a single line of hex.</summary>
    public static string Preview(ReadOnlySpan<byte> data, int maxBytes) =>
        Convert.ToHexString(data[..Math.Min(data.Length, maxBytes)]);
}
