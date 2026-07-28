using System.IO.Compression;

namespace SampleTests.Support;

/// <summary>
/// Generates a real, decodable PNG so the fixture carries a realistically sized image
/// attachment (a 1x1 placeholder would not exercise base64 inflation or the report's
/// thumbnail/lightbox paths). Encoded here rather than committed as a binary asset so the
/// fixture stays reproducible from source, and rendered deterministically so re-running the
/// sample produces identical bytes.
/// </summary>
internal static class SamplePng
{
    private const int Width = 640;
    private const int Height = 400;

    public static byte[] Bytes()
    {
        var raw = BuildScanlines();

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, Width);
        WriteBigEndian(ihdr, 4, Height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type: truecolour RGB
        ihdr[10] = 0; // compression: deflate
        ihdr[11] = 0; // filter: adaptive
        ihdr[12] = 0; // interlace: none
        WriteChunk(png, "IHDR", ihdr);

        using (var compressed = new MemoryStream())
        {
            using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(raw, 0, raw.Length);
            WriteChunk(png, "IDAT", compressed.ToArray());
        }

        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A diagonal gradient with a darker band, so the image is visibly "something"
    /// in a report rather than a flat colour.</summary>
    private static byte[] BuildScanlines()
    {
        var stride = (Width * 3) + 1; // one filter byte per scanline
        var raw = new byte[stride * Height];

        for (var y = 0; y < Height; y++)
        {
            var rowStart = y * stride;
            raw[rowStart] = 0; // filter type: None

            for (var x = 0; x < Width; x++)
            {
                var i = rowStart + 1 + (x * 3);
                var band = (y / 40) % 2 == 0 ? 0 : 40;

                raw[i + 0] = (byte)Math.Clamp((x * 255 / Width) - band, 0, 255);
                raw[i + 1] = (byte)Math.Clamp((y * 255 / Height) - band, 0, 255);
                raw[i + 2] = (byte)Math.Clamp(160 - band, 0, 255);
            }
        }

        return raw;
    }

    private static void WriteChunk(Stream target, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, 0, data.Length);
        target.Write(length);

        var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        target.Write(typeBytes);
        target.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBigEndian(crcBytes, 0, unchecked((int)crc));
        target.Write(crcBytes);
    }

    private static void WriteBigEndian(Span<byte> target, int offset, int value)
    {
        target[offset + 0] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
