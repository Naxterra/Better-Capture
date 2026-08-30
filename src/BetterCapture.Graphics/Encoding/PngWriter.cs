using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using BetterCapture.Core.Storage;
using BetterCapture.Graphics.Images;

namespace BetterCapture.Graphics.Encoding;

public static class PngWriter
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(
        string path,
        Bgra8Image image,
        IReadOnlyDictionary<string, string>? textMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(image);

        AtomicFileWriter.Write(path, output =>
        {
            output.Write(Signature);
            WriteHeader(output, image.Width, image.Height);
            WriteTextMetadata(output, textMetadata);
            WritePixels(output, image);
            WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        });
    }

    private static void WriteTextMetadata(
        Stream output,
        IReadOnlyDictionary<string, string>? textMetadata)
    {
        if (textMetadata is null)
        {
            return;
        }

        foreach (var (keyword, value) in textMetadata)
        {
            if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var safeKeyword = new string(keyword
                .Where(character => character is >= ' ' and <= '~' && character != '\0')
                .Take(79)
                .ToArray());
            if (safeKeyword.Length == 0)
            {
                continue;
            }

            using var data = new MemoryStream();
            data.Write(System.Text.Encoding.ASCII.GetBytes(safeKeyword));
            data.WriteByte(0); // keyword terminator
            data.WriteByte(0); // uncompressed
            data.WriteByte(0); // compression method
            data.WriteByte(0); // empty language tag
            data.WriteByte(0); // empty translated keyword
            data.Write(System.Text.Encoding.UTF8.GetBytes(value));
            WriteChunk(output, "iTXt", data.GetBuffer().AsSpan(0, checked((int)data.Length)));
        }
    }

    private static void WriteHeader(Stream output, int width, int height)
    {
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR", header);
    }

    private static void WritePixels(Stream output, Bgra8Image image)
    {
        using var uncompressed = new MemoryStream(checked((image.Width * image.Height * 4) + image.Height));
        var pixels = image.Pixels.AsSpan();
        var sourceStride = checked(image.Width * 4);

        for (var y = 0; y < image.Height; y++)
        {
            uncompressed.WriteByte(0);
            var rowOffset = y * sourceStride;

            for (var x = 0; x < image.Width; x++)
            {
                var offset = rowOffset + (x * 4);
                uncompressed.WriteByte(pixels[offset + 2]);
                uncompressed.WriteByte(pixels[offset + 1]);
                uncompressed.WriteByte(pixels[offset]);
                uncompressed.WriteByte(pixels[offset + 3]);
            }
        }

        uncompressed.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            uncompressed.CopyTo(zlib);
        }

        WriteChunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        System.Text.Encoding.ASCII.GetBytes(type, typeBytes);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32.Start;
        crc = Crc32.Append(crc, typeBytes);
        crc = Crc32.Append(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32.Finish(crc));
        output.Write(crcBytes);
    }

    private static class Crc32
    {
        public const uint Start = 0xffffffffu;

        public static uint Append(uint crc, ReadOnlySpan<byte> bytes)
        {
            foreach (var value in bytes)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
                }
            }

            return crc;
        }

        public static uint Finish(uint crc) => ~crc;
    }
}
