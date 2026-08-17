using System.Runtime.InteropServices;
using NaxCapture.Capture.Direct3D;
using NaxCapture.Capture.Displays;
using NaxCapture.Capture.Interop;
using NaxCapture.Core.Capture;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace NaxCapture.Capture;

public sealed class WindowsGraphicsCaptureService : IDisplayCaptureService
{
    private bool _disposed;

    public bool IsSupported => GraphicsCaptureSession.IsSupported();

    public IReadOnlyList<DisplayTarget> GetDisplays()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return WindowsDisplayCatalog.GetDisplays();
    }

    public DisplayTarget GetDisplayUnderCursor()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return WindowsDisplayCatalog.GetDisplayUnderCursor();
    }

    public async Task<ScRgbFrame> CaptureAsync(
        DisplayTarget target,
        CaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        if (!IsSupported)
        {
            throw new CaptureException("Windows Graphics Capture is not supported by this system.");
        }

        try
        {
            using var captureDevice = D3D11CaptureDevice.Create(target.MonitorHandle);
            var item = GraphicsCaptureItemFactory.CreateForMonitor(target.MonitorHandle);
            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                captureDevice.WinRtDevice,
                DirectXPixelFormat.R16G16B16A16Float,
                2,
                item.Size);
            using var session = framePool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = options.IncludeCursor;

            if (options.RequestBorderlessCapture)
            {
                TryDisableCaptureBorder(session);
            }

            var frameSource = new TaskCompletionSource<Direct3D11CaptureFrame>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void FrameArrived(Direct3D11CaptureFramePool sender, object args)
            {
                var frame = sender.TryGetNextFrame();
                if (!frameSource.TrySetResult(frame))
                {
                    frame.Dispose();
                }
            }

            framePool.FrameArrived += FrameArrived;
            try
            {
                session.StartCapture();
                using var frame = await frameSource.Task
                    .WaitAsync(options.FrameTimeout, cancellationToken)
                    .ConfigureAwait(false);
                return ReadFrame(captureDevice, frame, target);
            }
            finally
            {
                framePool.FrameArrived -= FrameArrived;
            }
        }
        catch (TimeoutException exception)
        {
            throw new CaptureException("Timed out while waiting for the first FP16 capture frame.", exception);
        }
        catch (CaptureException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CaptureException("The Windows Graphics Capture pipeline failed.", exception);
        }
    }

    public void Dispose() => _disposed = true;

    private static void TryDisableCaptureBorder(GraphicsCaptureSession session)
    {
        try
        {
            session.IsBorderRequired = false;
        }
        catch (UnauthorizedAccessException)
        {
            // Windows keeps the standard capture border when consent has not yet been granted.
        }
        catch (COMException)
        {
            // Borderless capture is optional; pixel format correctness is not.
        }
    }

    private static unsafe ScRgbFrame ReadFrame(
        D3D11CaptureDevice captureDevice,
        Direct3D11CaptureFrame frame,
        DisplayTarget target)
    {
        var width = frame.ContentSize.Width;
        var height = frame.ContentSize.Height;
        if (width <= 0 || height <= 0)
        {
            throw new CaptureException("Windows returned an empty capture frame.");
        }

        using var sourceTexture = Direct3DSurfaceInterop.GetTexture(frame.Surface);
        var sourceDescription = sourceTexture.Description;
        if (sourceDescription.Format != Format.R16G16B16A16_Float)
        {
            throw new CaptureException(
                $"Expected an FP16 scRGB surface, but Windows returned {sourceDescription.Format}.");
        }

        var stagingDescription = sourceDescription;
        stagingDescription.Width = checked((uint)width);
        stagingDescription.Height = checked((uint)height);
        stagingDescription.Usage = ResourceUsage.Staging;
        stagingDescription.BindFlags = BindFlags.None;
        stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
        stagingDescription.MiscFlags = ResourceOptionFlags.None;

        using var stagingTexture = captureDevice.Device.CreateTexture2D(stagingDescription);
        captureDevice.Context.CopyResource(stagingTexture, sourceTexture);
        captureDevice.Context.Map(
            stagingTexture,
            0,
            MapMode.Read,
            Vortice.Direct3D11.MapFlags.None,
            out var mapped).CheckError();

        try
        {
            var pixels = new Half[checked(width * height * ScRgbFrame.ChannelCount)];
            var rowBytes = checked(width * ScRgbFrame.ChannelCount * sizeof(ushort));

            fixed (Half* destinationStart = pixels)
            {
                var sourceStart = (byte*)mapped.DataPointer;
                var destinationBytes = (byte*)destinationStart;

                for (var row = 0; row < height; row++)
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
                target.DeviceName,
                captureDevice.AdapterName,
                target.DesktopBounds,
                CaptureColorSpace.LinearScRgb,
                captureDevice.AdvancedColorEnabled,
                captureDevice.MaximumLuminanceNits,
                $"Windows.Graphics.Capture / D3D11 FL {captureDevice.FeatureLevel}");
            return new ScRgbFrame(width, height, pixels, metadata);
        }
        finally
        {
            captureDevice.Context.Unmap(stagingTexture, 0);
        }
    }
}
