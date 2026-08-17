using NaxCapture.Capture.Interop;
using NaxCapture.Core.Capture;
using NaxCapture.Core.Geometry;

namespace NaxCapture.Capture.Displays;

internal static class WindowsDisplayCatalog
{
    internal static IReadOnlyList<DisplayTarget> GetDisplays()
    {
        var displays = new List<DisplayTarget>();
        NativeMethods.MonitorEnumProcedure callback = (
            nint monitor,
            nint deviceContext,
            ref NativeRect monitorRect,
            nint data) =>
        {
            displays.Add(CreateTarget(monitor));
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0))
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }

        return displays
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.DesktopBounds.X)
            .ThenBy(display => display.DesktopBounds.Y)
            .ToArray();
    }

    internal static DisplayTarget GetDisplayUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }

        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        return CreateTarget(monitor);
    }

    private static DisplayTarget CreateTarget(nint monitor)
    {
        var information = NativeMethods.GetMonitorInformation(monitor);
        var dpiX = 96u;
        var dpiY = 96u;
        _ = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.EffectiveDpi, out dpiX, out dpiY);
        dpiX = dpiX == 0 ? 96 : dpiX;
        dpiY = dpiY == 0 ? 96 : dpiY;

        return new DisplayTarget(
            monitor,
            information.DeviceName,
            new PixelRect(
                information.Monitor.Left,
                information.Monitor.Top,
                checked(information.Monitor.Right - information.Monitor.Left),
                checked(information.Monitor.Bottom - information.Monitor.Top)),
            (information.Flags & NativeMethods.MonitorInfoPrimary) != 0,
            dpiX,
            dpiY);
    }
}
