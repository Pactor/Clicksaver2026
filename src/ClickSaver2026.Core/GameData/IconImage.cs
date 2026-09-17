using System.IO.Compression;

namespace ClickSaver2026.Core.GameData;

/// <summary>
/// Icons are stored as ordinary PNGs (one is a JPEG) with no alpha channel: the client treats
/// a bright green as transparent. This turns an icon record into a browser-ready PNG with real
/// transparency, using only the PNG format itself - no imaging library.
/// </summary>
/// <remarks>From AssetDecoder's IconImage (OmniCell/Tools/AssetDecoder, GPL-3.0).</remarks>
public static class IconImage
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPng(ReadOnlySpan<byte> data) => data.StartsWith(PngSignature);

    public static bool IsJpeg(ReadOnlySpan<byte> data) => data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8;

    /// <summary>Width and height from a PNG's IHDR or a JPEG's start-of-frame marker.</summary>
    public static bool TryGetSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (IsPng(data) && data.Length >= 24)
        {
            width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return true;
        }

        if (IsJpeg(data))
        {
            int at = 2;
            while (at + 9 < data.Length && data[at] == 0xFF)
            {
                byte marker = data[at + 1];
                int length = (data[at + 2] << 8) | data[at + 3];
                // SOF0..SOF15, except DHT (C4), JPG (C8) and DAC (CC), carry the frame size.
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    height = (data[at + 5] << 8) | data[at + 6];
                    width = (data[at + 7] << 8) | data[at + 8];
                    return true;
                }

                at += 2 + length;
            }
        }

        return false;
    }

    // The colour key: the same range the Extractor Serializer keyed out.
    private static bool IsTransparentKey(byte r, byte g, byte b) => r <= 33 && g >= 0xDE && b <= 5;

    /// <summary>
    /// A PNG with the colour key replaced by transparency. Anything that is not an 8-bit
    /// RGB or RGBA PNG (the single JPEG, for instance) comes back unchanged.
    /// </summary>
    public static byte[] ToTransparentPng(byte[] record) => ToTransparentPng(record, out _);

    public static byte[] ToTransparentPng(byte[] record, out int keyedPixels)
    {
        keyedPixels = 0;
        if (!IsPng(record))
        {
            return record;
        }

        int width = 0, height = 0, bitDepth = 0, colourType = 0, interlace = 0;
        using var compressed = new MemoryStream();
        int at = PngSignature.Length;
        while (at + 8 <= record.Length)
        {
            int length = ReadInt32BigEndian(record, at);
            string type = System.Text.Encoding.ASCII.GetString(record, at + 4, 4);
            int body = at + 8;
            if (type == "IHDR")
            {
                width = ReadInt32BigEndian(record, body);
                height = ReadInt32BigEndian(record, body + 4);
                bitDepth = record[body + 8];
                colourType = record[body + 9];
                interlace = record[body + 12];
            }
            else if (type == "IDAT")
            {
                compressed.Write(record, body, length);
            }
            else if (type == "IEND")
            {
                break;
            }

            at = body + length + 4; // skip the CRC
        }

        int channels = colourType switch { 2 => 3, 6 => 4, _ => 0 };
        if (bitDepth != 8 || channels == 0 || interlace != 0)
        {
            return record;
        }

        byte[] pixels = Unfilter(Inflate(compressed.ToArray()), width, height, channels);
        byte[] rgba = new byte[width * height * 4];
        for (int source = 0, target = 0; target < rgba.Length; source += channels, target += 4)
        {
            byte r = pixels[source], g = pixels[source + 1], b = pixels[source + 2];
            byte a = channels == 4 ? pixels[source + 3] : (byte)255;
            if (IsTransparentKey(r, g, b))
            {
                keyedPixels++;
                r = g = b = a = 0;
            }

            rgba[target] = r;
            rgba[target + 1] = g;
            rgba[target + 2] = b;
            rgba[target + 3] = a;
        }

        return EncodeRgba(rgba, width, height);
    }

    private static byte[] Inflate(byte[] zlib)
    {
        using var input = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    // Reverses PNG's per-scanline filters (None, Sub, Up, Average, Paeth).
    private static byte[] Unfilter(byte[] filtered, int width, int height, int bytesPerPixel)
    {
        int stride = width * bytesPerPixel;
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int filter = filtered[y * (stride + 1)];
            int source = y * (stride + 1) + 1;
            int row = y * stride;
            for (int x = 0; x < stride; x++)
            {
                int left = x >= bytesPerPixel ? pixels[row + x - bytesPerPixel] : 0;
                int up = y > 0 ? pixels[row - stride + x] : 0;
                int upLeft = y > 0 && x >= bytesPerPixel ? pixels[row - stride + x - bytesPerPixel] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException("Unknown PNG filter " + filter),
                };
                pixels[row + x] = (byte)(filtered[source + x] + predictor);
            }
        }

        return pixels;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] EncodeRgba(byte[] rgba, int width, int height)
    {
        int stride = width * 4;
        byte[] raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            // Filter type 0 (None) on every scanline; icons are tiny, so size does not matter.
            Buffer.BlockCopy(rgba, y * stride, raw, y * (stride + 1) + 1, stride);
        }

        using var zlib = new MemoryStream();
        using (var deflate = new ZLibStream(zlib, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        byte[] header = new byte[13];
        WriteInt32BigEndian(header, 0, width);
        WriteInt32BigEndian(header, 4, height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA

        using var png = new MemoryStream();
        png.Write(PngSignature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", zlib.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] body)
    {
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        byte[] length = new byte[4];
        WriteInt32BigEndian(length, 0, body.Length);
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(body);
        uint crc = Crc32.Append(Crc32.Append(0xFFFFFFFF, typeBytes), body) ^ 0xFFFFFFFF;
        byte[] crcBytes = new byte[4];
        WriteInt32BigEndian(crcBytes, 0, (int)crc);
        stream.Write(crcBytes);
    }

    private static int ReadInt32BigEndian(byte[] data, int at) =>
        (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];

    private static void WriteInt32BigEndian(byte[] data, int at, int value)
    {
        data[at] = (byte)(value >> 24);
        data[at + 1] = (byte)(value >> 16);
        data[at + 2] = (byte)(value >> 8);
        data[at + 3] = (byte)value;
    }

    // The CRC-32 PNG chunks carry (polynomial 0xEDB88320), without the final inversion.
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Append(uint crc, byte[] data)
        {
            foreach (byte value in data)
            {
                crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
