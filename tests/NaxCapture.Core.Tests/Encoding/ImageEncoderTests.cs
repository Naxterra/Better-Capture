using System.IO.Compression;
using NaxCapture.Core.Capture;
using NaxCapture.Core.Geometry;
using NaxCapture.Graphics.Encoding;
using NaxCapture.Graphics.Images;

namespace NaxCapture.Core.Tests.Encoding;

public sealed class ImageEncoderTests
{
    [Fact]
    public void PngWriter_WritesValidRgbaScanline()
    {
        var path = TemporaryPath("png");
        try
        {
            PngWriter.Write(path, new Bgra8Image(1, 1, [10, 20, 30, 255]));
            var bytes = File.ReadAllBytes(path);

            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
            var idatOffset = FindChunk(bytes, "IDAT");
            var length = ReadBigEndianInt32(bytes, idatOffset - 4);
            using var compressed = new MemoryStream(bytes, idatOffset + 4, length);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            zlib.CopyTo(decoded);
            Assert.Equal(new byte[] { 0, 30, 20, 10, 255 }, decoded.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PngWriter_EmbedsUtf8SourceDescription()
    {
        var path = TemporaryPath("png");
        try
        {
            PngWriter.Write(
                path,
                new Bgra8Image(1, 1, [0, 0, 0, 255]),
                new Dictionary<string, string> { ["Description"] = "Brave — Angebote" });

            var bytes = File.ReadAllBytes(path);
            Assert.True(FindChunk(bytes, "iTXt") > 0);
            var descriptionBytes = System.Text.Encoding.UTF8.GetBytes("Brave — Angebote");
            Assert.True(bytes.AsSpan().IndexOf(descriptionBytes) >= 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpenExrWriter_WritesMagicAndHalfFloatChannels()
    {
        var path = TemporaryPath("exr");
        try
        {
            var frame = new ScRgbFrame(
                1,
                1,
                [(Half)1f, (Half)2f, (Half)3f, (Half)1f],
                new CaptureFrameMetadata(
                    DateTimeOffset.UnixEpoch,
                    "display",
                    "adapter",
                    new PixelRect(0, 0, 1, 1),
                    CaptureColorSpace.LinearScRgb,
                    true,
                    1000f,
                    "test"));

            OpenExrWriter.Write(path, frame);
            using var reader = new BinaryReader(File.OpenRead(path));
            Assert.Equal(20000630, reader.ReadInt32());
            Assert.Equal(2, reader.ReadInt32());
            Assert.True(new FileInfo(path).Length > 200);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TemporaryPath(string extension) => Path.Combine(
        Path.GetTempPath(),
        $"naxcapture-{Guid.NewGuid():N}.{extension}");

    private static int FindChunk(byte[] png, string type)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        for (var index = 8; index <= png.Length - 4; index++)
        {
            if (png.AsSpan(index, 4).SequenceEqual(typeBytes))
            {
                return index;
            }
        }

        throw new InvalidDataException($"PNG chunk {type} was not found.");
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));

}
