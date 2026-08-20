using System.Threading.Channels;
using BetterCapture.Capture.Direct3D;
using BetterCapture.Capture.Interop;
using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace BetterCapture.Capture;

public sealed class ContinuousDisplayCaptureSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly DisplayTarget _target;
    private readonly PixelRect _region;
    private readonly D3D11CaptureDevice _captureDevice;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _captureSession;
    private readonly ID3D11Texture2D _stagingTexture;
    private readonly Channel<ScRgbFrame> _frames = Channel.CreateBounded<ScRgbFrame>(
        new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
    private bool _disposed;

    internal ContinuousDisplayCaptureSession(
        DisplayTarget target,
        PixelRect region,
        bool includeCursor)
    {
        _target = target;
        _region = region.ClampTo(target.DesktopBounds.Size);
        if (_region.IsEmpty)
        {
            throw new ArgumentException("The continuous capture region is empty.", nameof(region));
        }

        try
        {
            _captureDevice = D3D11CaptureDevice.Create(target.MonitorHandle);
            var item = GraphicsCaptureItemFactory.CreateForMonitor(target.MonitorHandle);
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _captureDevice.WinRtDevice,
                DirectXPixelFormat.R16G16B16A16Float,
                3,
                item.Size);
            _captureSession = _framePool.CreateCaptureSession(item);
            _captureSession.IsCursorCaptureEnabled = includeCursor;
            TryDisableCaptureBorder(_captureSession);

            var stagingDescription = new Texture2DDescription(
                Format.R16G16B16A16_Float,
                checked((uint)_region.Width),
                checked((uint)_region.Height),
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
            _captureSession.StartCapture();
        }
        catch
        {
            CleanupPartiallyConstructed();
            throw;
        }
    }

    public void DiscardPendingFrames()
    {
        while (_frames.Reader.TryRead(out _))
        {
        }
    }

    public async Task<ScRgbFrame> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        var latest = await _frames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        while (_frames.Reader.TryRead(out var newer))
        {
            latest = newer;
        }

        return latest;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _framePool.FrameArrived -= OnFrameArrived;
            _captureSession.Dispose();
            _framePool.Dispose();
            _stagingTexture.Dispose();
            _captureDevice.Dispose();
            _frames.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _frames.Writer.TryWrite(ReadFrame(frame));
            }
            catch (Exception exception)
            {
                _frames.Writer.TryComplete(exception);
            }
        }
    }

    private unsafe ScRgbFrame ReadFrame(Direct3D11CaptureFrame frame)
    {
        using var sourceTexture = Direct3DSurfaceInterop.GetTexture(frame.Surface);
        var sourceBox = new Box(
            _region.X,
            _region.Y,
            0,
            _region.Right,
            _region.Bottom,
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
            var pixels = new Half[checked(_region.Width * _region.Height * ScRgbFrame.ChannelCount)];
            var rowBytes = checked(_region.Width * ScRgbFrame.ChannelCount * sizeof(ushort));
            fixed (Half* destinationStart = pixels)
            {
                var sourceStart = (byte*)mapped.DataPointer;
                var destinationBytes = (byte*)destinationStart;
                for (var row = 0; row < _region.Height; row++)
                {
                    Buffer.MemoryCopy(
                        sourceStart + (row * mapped.RowPitch),
                        destinationBytes + (row * rowBytes),
                        rowBytes,
                        rowBytes);
                }
            }

            var metadata = new CaptureFrameMetadata(
                DateTimeOffset.Now,
                _target.DeviceName,
                _captureDevice.AdapterName,
                _target.DesktopBounds,
                CaptureColorSpace.LinearScRgb,
                _captureDevice.AdvancedColorEnabled,
                _captureDevice.MaximumLuminanceNits,
                $"Windows.Graphics.Capture continuous / D3D11 FL {_captureDevice.FeatureLevel}");
            return new ScRgbFrame(_region.Width, _region.Height, pixels, metadata);
        }
        finally
        {
            _captureDevice.Context.Unmap(_stagingTexture, 0);
        }
    }

    private void CleanupPartiallyConstructed()
    {
        try { _captureSession?.Dispose(); } catch { }
        try { _framePool?.Dispose(); } catch { }
        try { _stagingTexture?.Dispose(); } catch { }
        try { _captureDevice?.Dispose(); } catch { }
    }

    private static void TryDisableCaptureBorder(GraphicsCaptureSession session)
    {
        try
        {
            session.IsBorderRequired = false;
        }
        catch
        {
        }
    }
}
