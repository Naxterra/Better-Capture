using System.Runtime.InteropServices;
using BetterCapture.Core.Geometry;

namespace BetterCapture.App.Services;

internal static class WindowScrollService
{
    private const uint WindowMessageMouseWheel = 0x020A;
    private const int WheelDelta = -480;

    internal static bool TryScrollDown(nint windowHandle, PixelRect desktopBounds)
    {
        if (windowHandle == 0 || !IsWindow(windowHandle))
        {
            return false;
        }

        _ = SetForegroundWindow(windowHandle);
        Thread.Sleep(80);
        if (GetForegroundWindow() != windowHandle)
        {
            return false;
        }

        var point = new Point
        {
            X = desktopBounds.X + Math.Max(1, desktopBounds.Width / 2),
            Y = desktopBounds.Y + Math.Max(1, desktopBounds.Height / 2),
        };
        var recipient = WindowFromPoint(point);
        if (recipient == 0 || (recipient != windowHandle && !IsChild(windowHandle, recipient)))
        {
            recipient = windowHandle;
        }

        var wordParameterValue = (long)(unchecked((uint)(ushort)(short)WheelDelta) << 16);
        var wordParameter = (nint)wordParameterValue;
        var longParameter = (nint)(unchecked((uint)(ushort)point.X) |
            (unchecked((uint)(ushort)point.Y) << 16));
        _ = SendMessage(recipient, WindowMessageMouseWheel, wordParameter, longParameter);
        return true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(nint parentWindow, nint childWindow);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(
        nint windowHandle,
        uint message,
        nint wordParameter,
        nint longParameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }
}
