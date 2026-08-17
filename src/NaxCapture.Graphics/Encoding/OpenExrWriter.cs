using System.Text;
using NaxCapture.Core.Capture;
using NaxCapture.Core.Geometry;

namespace NaxCapture.Graphics.Encoding;

public static class OpenExrWriter
{
    private const int Magic = 20000630;
    private const int Version = 2;
    private static readonly string[] ChannelNames = ["B", "G", "R", "A"];
    private static readonly int[] ChannelOffsets = [2, 1, 0, 3];

    public static void Write(string path, ScRgbFrame frame, PixelRect? region = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(frame);

        var sourceRegion = (region ?? new PixelRect(0, 0, frame.Width, frame.Height)).ClampTo(frame.Size);
        if (sourceRegion.IsEmpty)
        {
            throw new ArgumentException("The output region does not intersect the frame.", nameof(region));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = path + ".partial";

        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(Version);
                WriteHeader(writer, sourceRegion.Width, sourceRegion.Height, frame.Metadata.DisplayMaxLuminanceNits);
                WriteScanlines(writer, frame, sourceRegion);
                writer.Flush();
                output.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteHeader(BinaryWriter writer, int width, int height, float? displayMaxLuminanceNits)
    {
        using var channels = new MemoryStream();
        using (var channelWriter = new BinaryWriter(channels, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            foreach (var channel in ChannelNames)
            {
                WriteNullTerminated(channelWriter, channel);
                channelWriter.Write(1); // HALF
                channelWriter.Write((byte)0);
                channelWriter.Write(new byte[3]);
                channelWriter.Write(1);
                channelWriter.Write(1);
            }

            channelWriter.Write((byte)0);
        }

        WriteAttribute(writer, "channels", "chlist", channels.ToArray());
        WriteAttribute(writer, "compression", "compression", [0]);
        WriteAttribute(writer, "dataWindow", "box2i", Box(0, 0, width - 1, height - 1));
        WriteAttribute(writer, "displayWindow", "box2i", Box(0, 0, width - 1, height - 1));
        WriteAttribute(writer, "lineOrder", "lineOrder", [0]);
        WriteAttribute(writer, "pixelAspectRatio", "float", Floats(1f));
        WriteAttribute(writer, "screenWindowCenter", "v2f", Floats(0f, 0f));
        WriteAttribute(writer, "screenWindowWidth", "float", Floats(1f));
        WriteAttribute(writer, "chromaticities", "chromaticities", Floats(
            0.6400f, 0.3300f,
            0.3000f, 0.6000f,
            0.1500f, 0.0600f,
            0.3127f, 0.3290f));
        WriteAttribute(writer, "betterCaptureColorSpace", "string", System.Text.Encoding.ASCII.GetBytes("linear scRGB; 1.0 = 80 nits"));

        if (displayMaxLuminanceNits is > 0 and var maxNits)
        {
            WriteAttribute(writer, "whiteLuminance", "float", Floats(maxNits));
        }

        writer.Write((byte)0);
    }

    private static void WriteScanlines(BinaryWriter writer, ScRgbFrame frame, PixelRect region)
    {
        var bytesPerScanline = checked(region.Width * ChannelNames.Length * sizeof(ushort));
        var chunkSize = checked(sizeof(int) + sizeof(int) + bytesPerScanline);
        var firstChunkOffset = checked(writer.BaseStream.Position + (region.Height * sizeof(long)));

        for (var y = 0; y < region.Height; y++)
        {
            writer.Write(checked(firstChunkOffset + ((long)y * chunkSize)));
        }

        var source = frame.Pixels.Span;
        var sourceStride = checked(frame.Width * ScRgbFrame.ChannelCount);

        for (var y = 0; y < region.Height; y++)
        {
            writer.Write(y);
            writer.Write(bytesPerScanline);
            var rowOffset = checked(((region.Y + y) * sourceStride) + (region.X * ScRgbFrame.ChannelCount));

            foreach (var channelOffset in ChannelOffsets)
            {
                for (var x = 0; x < region.Width; x++)
                {
                    var value = source[rowOffset + (x * ScRgbFrame.ChannelCount) + channelOffset];
                    writer.Write(BitConverter.HalfToUInt16Bits(value));
                }
            }
        }
    }

    private static void WriteAttribute(BinaryWriter writer, string name, string type, ReadOnlySpan<byte> value)
    {
        WriteNullTerminated(writer, name);
        WriteNullTerminated(writer, type);
        writer.Write(value.Length);
        writer.Write(value);
    }

    private static void WriteNullTerminated(BinaryWriter writer, string value)
    {
        writer.Write(System.Text.Encoding.ASCII.GetBytes(value));
        writer.Write((byte)0);
    }

    private static byte[] Box(int xMin, int yMin, int xMax, int yMax)
    {
        using var memory = new MemoryStream(16);
        using var writer = new BinaryWriter(memory);
        writer.Write(xMin);
        writer.Write(yMin);
        writer.Write(xMax);
        writer.Write(yMax);
        return memory.ToArray();
    }

    private static byte[] Floats(params float[] values)
    {
        using var memory = new MemoryStream(values.Length * sizeof(float));
        using var writer = new BinaryWriter(memory);
        foreach (var value in values)
        {
            writer.Write(value);
        }

        return memory.ToArray();
    }
}
