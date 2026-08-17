using System.Runtime.InteropServices;

namespace BetterCapture.Capture.Interop;

internal static partial class NativeMethods
{
    internal const uint MonitorDefaultToNearest = 2;
    internal const uint MonitorInfoPrimary = 1;
    internal const int EffectiveDpi = 0;

    internal delegate bool MonitorEnumProcedure(nint monitor, nint deviceContext, ref NativeRect monitorRect, nint data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(
        nint deviceContext,
        nint clipRect,
        MonitorEnumProcedure callback,
        nint data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx information);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("d3d11.dll")]
    internal static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    internal static MonitorInfoEx GetMonitorInformation(nint monitor)
    {
        var information = new MonitorInfoEx
        {
            Size = (uint)Marshal.SizeOf<MonitorInfoEx>(),
        };

        if (!GetMonitorInfo(monitor, ref information))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        return information;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    internal int X;
    internal int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfoEx
{
    internal uint Size;
    internal NativeRect Monitor;
    internal NativeRect WorkArea;
    internal uint Flags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    internal string DeviceName;
}
