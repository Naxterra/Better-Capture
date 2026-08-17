using System.Diagnostics;
using System.Runtime.InteropServices;
using NaxCapture.Capture.Direct3D;
using NaxCapture.Capture.Interop;
using NaxCapture.Core.Capture;
using NaxCapture.Core.Geometry;
using NaxCapture.Core.Video;
using NaxCapture.Video.Encoding;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace NaxCapture.Video;

public sealed class WindowsGraphicsVideoRecorder
{
    public VideoRecordingSession Start(
        DisplayTarget target,
        SmartCaptureSelection selection,
        string outputPath,
        string metadataPath,
        VideoRecordingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);
        options ??= VideoRecordingOptions.Default;

        return new VideoRecordingSession(target, selection, outputPath, metadataPath, options);
    }
}

public sealed class VideoRecordingSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly DisplayTarget _target;
    private readonly SmartCaptureSelection _selection;
    private readonly VideoRecordingOptions _options;
    private readonly string _outputPath;
    private readonly string _metadataPath;
    private readonly string _partialPath;
    private readonly PixelRect _recordingRegion;
    private readonly long _frameDuration;
    private readonly Stopwatch _clock = new();
    private byte[] _captureBuffer;
    private byte[] _latestBuffer;
    private readonly byte[] _encodeBuffer;
    private readonly CancellationTokenSource _encoderCancellation = new();
    private readonly D3D11CaptureDevice _captureDevice;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _captureSession;
    private readonly ID3D11Texture2D _stagingTexture;
    private readonly H264Mp4Writer _writer;
    private readonly Task _encoderTask;
    private bool _recording;
    private bool _stopped;
    private long _nextFrameTimestamp;
    private bool _hasLatestFrame;
    private long _encodedFrames;
    private long _droppedFrames;
    private Exception? _captureFault;

    internal VideoRecordingSession(
        DisplayTarget target,
        SmartCaptureSelection selection,
        string outputPath,
        string metadataPath,
        VideoRecordingOptions options)
    {
        ValidateOptions(options);
        _target = target;
        _selection = selection;
        _options = options;
        _outputPath = Path.GetFullPath(outputPath);
        _metadataPath = Path.GetFullPath(metadataPath);
        _partialPath = Path.Combine(
            Path.GetDirectoryName(_outputPath)!,
            Path.GetFileNameWithoutExtension(_outputPath) + ".partial.mp4");

        var clipped = selection.Region.ClampTo(target.DesktopBounds.Size);
        var width = clipped.Width & ~1;
        var height = clipped.Height & ~1;
        if (width < 2 || height < 2)
        {
            throw new ArgumentException("The selected video area is too small.", nameof(selection));
        }

        _recordingRegion = new PixelRect(clipped.X, clipped.Y, width, height);
        _frameDuration = TimeSpan.TicksPerSecond / options.FramesPerSecond;
        _captureBuffer = new byte[checked(width * height * 4)];
        _latestBuffer = new byte[_captureBuffer.Length];
        _encodeBuffer = new byte[_captureBuffer.Length];
        Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
        if (File.Exists(_partialPath))
        {
            File.Delete(_partialPath);
        }

        try
        {
            _writer = new H264Mp4Writer(
                _partialPath,
                width,
                height,
                options.FramesPerSecond,
                options.BitRate);
            _captureDevice = D3D11CaptureDevice.Create(target.MonitorHandle);
            var item = GraphicsCaptureItemFactory.CreateForMonitor(target.MonitorHandle);
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _captureDevice.WinRtDevice,
                DirectXPixelFormat.R16G16B16A16Float,
                3,
                item.Size);
            _captureSession = _framePool.CreateCaptureSession(item);
            _captureSession.IsCursorCaptureEnabled = options.IncludeCursor;
            TryDisableCaptureBorder(_captureSession);

            var stagingDescription = new Texture2DDescription(
                Format.R16G16B16A16_Float,
                checked((uint)width),
                checked((uint)height),
                arraySize: 1,
                mipLevels: 1,
                BindFlags.None,
                ResourceUsage.Staging,
                CpuAccessFlags.Read,
                sampleCount: 1,
                sampleQuality: 0,
                ResourceOptionFlags.None);
            _stagingTexture = _captureDevice.Device.CreateTexture2D(stagingDescription);
            _framePool.FrameArrived += OnFrameArrived;
            _recording = true;
            _clock.Start();
            _captureSession.StartCapture();
            _encoderTask = Task.Run(() => EncoderLoopAsync(_encoderCancellation.Token));
        }
        catch
        {
            CleanupPartiallyConstructed();
            throw;
        }
    }

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _recording;
            }
        }
    }

    public VideoRecordingProgress GetProgress()
    {
        lock (_gate)
        {
            return new VideoRecordingProgress(_clock.Elapsed, _encodedFrames, _droppedFrames);
        }
    }

    public async Task<VideoRecordingResult> StopAsync()
    {
        VideoRecordingResult result;
        lock (_gate)
        {
            if (_stopped)
            {
                throw new InvalidOperationException("This video recording has already been stopped.");
            }

            _recording = false;
            _stopped = true;
        }

        _framePool.FrameArrived -= OnFrameArrived;
        _captureSession.Dispose();
        _framePool.Dispose();
        _encoderCancellation.Cancel();
        try
        {
            await _encoderTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        lock (_gate)
        {
            _clock.Stop();
            _writer.FinalizeFile();
            _writer.Dispose();
            _stagingTexture.Dispose();
            _captureDevice.Dispose();
            _encoderCancellation.Dispose();

            File.Move(_partialPath, _outputPath, overwrite: true);
            var desktopBounds = new PixelRect(
                _recordingRegion.X + _target.DesktopBounds.X,
                _recordingRegion.Y + _target.DesktopBounds.Y,
                _recordingRegion.Width,
                _recordingRegion.Height);
            result = new VideoRecordingResult(
                _outputPath,
                _metadataPath,
                _recordingRegion.Width,
                _recordingRegion.Height,
                _clock.Elapsed,
                _encodedFrames,
                _droppedFrames,
                _selection.Source,
                desktopBounds,
                _options.FramesPerSecond,
                _options.BitRate,
                _options.SdrWhiteLevelNits);
        }

        if (_captureFault is not null)
        {
            throw new InvalidOperationException("Video capture stopped after an encoding failure.", _captureFault);
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
        {
            await StopAsync();
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        lock (_gate)
        {
            if (!_recording)
            {
                return;
            }

            var timestamp = _clock.Elapsed.Ticks;
            if (timestamp < _nextFrameTimestamp)
            {
                return;
            }

            try
            {
                CaptureLatestFrame(frame);
                (_captureBuffer, _latestBuffer) = (_latestBuffer, _captureBuffer);
                _hasLatestFrame = true;
                _nextFrameTimestamp = timestamp + _frameDuration;
            }
            catch (Exception exception)
            {
                _captureFault = exception;
                _recording = false;
                _droppedFrames++;
            }
        }
    }

    private unsafe void CaptureLatestFrame(Direct3D11CaptureFrame frame)
    {
        using var sourceTexture = Direct3DSurfaceInterop.GetTexture(frame.Surface);
        var sourceBox = new Box(
            _recordingRegion.X,
            _recordingRegion.Y,
            0,
            _recordingRegion.Right,
            _recordingRegion.Bottom,
            1);
        _captureDevice.Context.CopySubresourceRegion(
            _stagingTexture,
            0,
            0,
            0,
            0,
            sourceTexture,
            0,
            sourceBox);
        _captureDevice.Context.Map(
            _stagingTexture,
            0,
            MapMode.Read,
            Vortice.Direct3D11.MapFlags.None,
            out var mapped).CheckError();

        try
        {
            ConvertScRgbToBgra(mapped.DataPointer, checked((int)mapped.RowPitch));
        }
        finally
        {
            _captureDevice.Context.Unmap(_stagingTexture, 0);
        }

    }

    private async Task EncoderLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromTicks(_frameDuration));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    if (!_recording)
                    {
                        return;
                    }

                    if (!_hasLatestFrame)
                    {
                        _droppedFrames++;
                        continue;
                    }

                    Buffer.BlockCopy(_latestBuffer, 0, _encodeBuffer, 0, _encodeBuffer.Length);
                    try
                    {
                        _writer.WriteFrame(_encodeBuffer, _clock.Elapsed.Ticks, _frameDuration);
                        _encodedFrames++;
                    }
                    catch (Exception exception)
                    {
                        _captureFault = exception;
                        _recording = false;
                        _droppedFrames++;
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private unsafe void ConvertScRgbToBgra(nint sourcePointer, int sourceRowPitch)
    {
        var inverseWindowsSdrBoost = 80f / _options.SdrWhiteLevelNits;
        fixed (byte* destinationStart = _captureBuffer)
        {
            for (var y = 0; y < _recordingRegion.Height; y++)
            {
                var sourceRow = (Half*)((byte*)sourcePointer + (y * sourceRowPitch));
                var destinationRow = destinationStart + (y * _recordingRegion.Width * 4);

                for (var x = 0; x < _recordingRegion.Width; x++)
                {
                    var red = Sanitize((float)sourceRow[(x * 4)]) * inverseWindowsSdrBoost;
                    var green = Sanitize((float)sourceRow[(x * 4) + 1]) * inverseWindowsSdrBoost;
                    var blue = Sanitize((float)sourceRow[(x * 4) + 2]) * inverseWindowsSdrBoost;
                    var maximum = Math.Max(red, Math.Max(green, blue));
                    if (maximum > 1f)
                    {
                        red /= maximum;
                        green /= maximum;
                        blue /= maximum;
                    }

                    var offset = x * 4;
                    destinationRow[offset] = ToByte(LinearToSrgb(blue));
                    destinationRow[offset + 1] = ToByte(LinearToSrgb(green));
                    destinationRow[offset + 2] = ToByte(LinearToSrgb(red));
                    destinationRow[offset + 3] = 255;
                }
            }
        }
    }

    private void CleanupPartiallyConstructed()
    {
        try { _captureSession?.Dispose(); } catch { }
        try { _framePool?.Dispose(); } catch { }
        try { _stagingTexture?.Dispose(); } catch { }
        try { _captureDevice?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
    }

    private static void TryDisableCaptureBorder(GraphicsCaptureSession session)
    {
        try
        {
            session.IsBorderRequired = false;
        }
        catch
        {
            // The standard Windows border remains when borderless permission is unavailable.
        }
    }

    private static void ValidateOptions(VideoRecordingOptions options)
    {
        if (options.FramesPerSecond is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Frame rate must be between 1 and 60 FPS.");
        }

        if (options.BitRate < 500_000)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Bit rate must be at least 500 kbps.");
        }

        if (!float.IsFinite(options.SdrWhiteLevelNits) || options.SdrWhiteLevelNits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SDR white level must be greater than zero.");
        }
    }

    private static float Sanitize(float value) => float.IsFinite(value) ? Math.Max(0f, value) : 0f;

    private static float LinearToSrgb(float value) => value <= 0.0031308f
        ? value * 12.92f
        : (1.055f * MathF.Pow(value, 1f / 2.4f)) - 0.055f;

    private static byte ToByte(float value) => (byte)Math.Clamp(
        (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f),
        0,
        255);
}
