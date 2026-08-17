using System.Runtime.InteropServices;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace BetterCapture.Video.Encoding;

internal sealed class H264Mp4Writer : IDisposable
{
    private readonly IMFSinkWriter _writer;
    private readonly int _streamIndex;
    private bool _finalized;
    private bool _disposed;

    internal H264Mp4Writer(
        string outputPath,
        int width,
        int height,
        int framesPerSecond,
        int bitRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitRate);

        MFStartup().CheckError();
        try
        {
            using var attributes = MFCreateAttributes(3);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u).CheckError();
            attributes.Set(SinkWriterAttributeKeys.DisableThrottling, 1u).CheckError();
            attributes.Set(SinkWriterAttributeKeys.LowLatency, true).CheckError();

            _writer = MFCreateSinkWriterFromURL(outputPath, null!, attributes);
            using var outputType = CreateVideoType(
                VideoFormatGuids.H264,
                width,
                height,
                framesPerSecond,
                bitRate,
                compressed: true);
            _streamIndex = _writer.AddStream(outputType);

            using var inputType = CreateVideoType(
                VideoFormatGuids.Rgb32,
                width,
                height,
                framesPerSecond,
                bitRate: 0,
                compressed: false);
            inputType.Set(MediaTypeAttributeKeys.DefaultStride, checked((uint)(width * 4))).CheckError();
            inputType.Set(MediaTypeAttributeKeys.FixedSizeSamples, 1u).CheckError();
            inputType.Set(MediaTypeAttributeKeys.SampleSize, checked((uint)(width * height * 4))).CheckError();
            _writer.SetInputMediaType(_streamIndex, inputType, null!);
            _writer.BeginWriting();
        }
        catch
        {
            MFShutdown();
            throw;
        }
    }

    internal unsafe void WriteFrame(ReadOnlySpan<byte> bgraPixels, long timestamp, long duration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finalized)
        {
            throw new InvalidOperationException("The MP4 writer has already been finalized.");
        }

        using var buffer = MFCreateMemoryBuffer(bgraPixels.Length);
        buffer.Lock(out var destination, out _, out _);
        try
        {
            fixed (byte* source = bgraPixels)
            {
                Buffer.MemoryCopy(source, (void*)destination, bgraPixels.Length, bgraPixels.Length);
            }
        }
        finally
        {
            buffer.Unlock();
        }

        buffer.CurrentLength = bgraPixels.Length;
        using var sample = MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = timestamp;
        sample.SampleDuration = duration;
        _writer.WriteSample(_streamIndex, sample);
    }

    internal void FinalizeFile()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finalized)
        {
            return;
        }

        _writer.Finalize();
        _finalized = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_finalized)
            {
                _writer.Finalize();
            }
        }
        finally
        {
            _disposed = true;
            _writer.Dispose();
            MFShutdown();
        }
    }

    private static IMFMediaType CreateVideoType(
        Guid subtype,
        int width,
        int height,
        int framesPerSecond,
        int bitRate,
        bool compressed)
    {
        var mediaType = MFCreateMediaType();
        try
        {
            mediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            mediaType.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
            mediaType.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive).CheckError();
            MFSetAttributeSize(
                mediaType,
                MediaTypeAttributeKeys.FrameSize,
                checked((uint)width),
                checked((uint)height)).CheckError();
            MFSetAttributeRatio(
                mediaType,
                MediaTypeAttributeKeys.FrameRate,
                checked((uint)framesPerSecond),
                1).CheckError();
            MFSetAttributeRatio(mediaType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
            if (compressed)
            {
                mediaType.Set(MediaTypeAttributeKeys.AvgBitrate, checked((uint)bitRate)).CheckError();
            }

            return mediaType;
        }
        catch
        {
            mediaType.Dispose();
            throw;
        }
    }
}
