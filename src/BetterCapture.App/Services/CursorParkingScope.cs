using System.Runtime.InteropServices;
using BetterCapture.Core.Geometry;

namespace BetterCapture.App.Services;

internal sealed class CursorParkingScope : IDisposable
{
    private readonly Point _originalPosition;
    private bool _disposed;

    private CursorParkingScope(PixelRect captureViewport)
    {
        _ = GetCursorPosition(out _originalPosition);
        // The browser chrome contains tabs, bookmarks, and extension buttons that
        // display tooltips or URL previews when hovered. The scrollbar gutter is
        // non-content, does not trigger link previews, and is outside our sampled
        // horizontal margins during overlap matching.
        _ = SetCursorPosition(
            Math.Max(captureViewport.X, captureViewport.Right - 2),
            captureViewport.Y + Math.Max(1, captureViewport.Height / 2));
    }

    internal static CursorParkingScope ParkAboveCaptureViewport(PixelRect captureViewport) =>
        new(captureViewport);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = SetCursorPosition(_originalPosition.X, _originalPosition.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPosition(out Point point);

    [DllImport("user32.dll", EntryPoint = "SetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPosition(int x, int y);
}
