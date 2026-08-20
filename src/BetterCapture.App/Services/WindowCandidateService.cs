using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;

namespace BetterCapture.App.Services;

internal static class WindowCandidateService
{
    private const int DwmExtendedFrameBounds = 9;
    private const int DwmCloaked = 14;
    private const int ExtendedStyleIndex = -20;
    private const long ExtendedStyleToolWindow = 0x00000080L;
    private const uint WindowDisplayAffinityNone = 0;
    private static readonly HashSet<string> IgnoredWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    internal static IReadOnlyList<CaptureCandidate> GetCandidates(DisplayTarget target)
    {
        var candidates = new List<CaptureCandidate>();
        var currentProcessId = (uint)Environment.ProcessId;
        EnumWindows((window, _) =>
        {
            TryAddCandidate(window, currentProcessId, target, candidates);
            return true;
        }, 0);
        return candidates;
    }

    internal static CaptureCandidate? FindAt(
        IReadOnlyList<CaptureCandidate> candidates,
        PixelPoint monitorPoint) => candidates.FirstOrDefault(candidate =>
            monitorPoint.X >= candidate.Region.X &&
            monitorPoint.X < candidate.Region.Right &&
            monitorPoint.Y >= candidate.Region.Y &&
            monitorPoint.Y < candidate.Region.Bottom);

    internal static CaptureCandidate? FindBestOverlap(
        IReadOnlyList<CaptureCandidate> candidates,
        PixelRect region)
    {
        CaptureCandidate? best = null;
        long bestArea = 0;

        foreach (var candidate in candidates)
        {
            var intersection = candidate.Region.Intersect(region);
            var area = (long)intersection.Width * intersection.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = candidate;
            }
        }

        return best;
    }

    private static void TryAddCandidate(
        nint window,
        uint currentProcessId,
        DisplayTarget target,
        ICollection<CaptureCandidate> candidates)
    {
        if (!IsWindowVisible(window) || IsWindowCloaked(window))
        {
            return;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == currentProcessId)
        {
            return;
        }

        var className = GetClassName(window);
        if (IgnoredWindowClasses.Contains(className) ||
            (GetWindowLongPtr(window, ExtendedStyleIndex).ToInt64() & ExtendedStyleToolWindow) != 0)
        {
            return;
        }

        if (!TryGetWindowBounds(window, out var desktopBounds))
        {
            return;
        }

        var clipped = desktopBounds.Intersect(target.DesktopBounds);
        if (clipped.Width < 32 || clipped.Height < 32)
        {
            return;
        }

        var title = GetWindowTitle(window);
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var applicationName = GetApplicationName(processId);
        var localRegion = new PixelRect(
            clipped.X - target.DesktopBounds.X,
            clipped.Y - target.DesktopBounds.Y,
            clipped.Width,
            clipped.Height);
        var coversMonitor = clipped.Width >= target.DesktopBounds.Width - 2 &&
                            clipped.Height >= target.DesktopBounds.Height - 2;
        var kind = coversMonitor || IsZoomed(window)
            ? CaptureSelectionKind.Window
            : CaptureSelectionKind.Window;
        var source = new CaptureSourceInfo(kind, applicationName, title, clipped);
        candidates.Add(new CaptureCandidate(
            window,
            localRegion,
            source,
            IsZoomed(window),
            coversMonitor,
            IsCaptureProtected(window)));
    }

    private static bool TryGetWindowBounds(nint window, out PixelRect bounds)
    {
        NativeRect rectangle;
        if (DwmGetWindowAttribute(
                window,
                DwmExtendedFrameBounds,
                out rectangle,
                Marshal.SizeOf<NativeRect>()) != 0 &&
            !GetWindowRect(window, out rectangle))
        {
            bounds = default;
            return false;
        }

        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;
        if (width <= 0 || height <= 0)
        {
            bounds = default;
            return false;
        }

        bounds = new PixelRect(rectangle.Left, rectangle.Top, width, height);
        return true;
    }

    private static bool IsWindowCloaked(nint window)
    {
        return DwmGetWindowAttribute(window, DwmCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static bool IsCaptureProtected(nint window) =>
        GetWindowDisplayAffinity(window, out var affinity) && affinity != WindowDisplayAffinityNone;

    private static string GetWindowTitle(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        _ = GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    private static string GetClassName(nint window)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private static string GetApplicationName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var version = process.MainModule?.FileVersionInfo;
            return FirstNonEmpty(version?.FileDescription, version?.ProductName, process.ProcessName);
        }
        catch
        {
            return Localizer.Get("WindowsApplication");
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? Localizer.Get("WindowsApplication");

    internal sealed record CaptureCandidate(
        nint WindowHandle,
        PixelRect Region,
        CaptureSourceInfo Source,
        bool IsMaximized,
        bool CoversMonitor,
        bool IsCaptureProtected);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    private delegate bool EnumWindowsProcedure(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProcedure callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out NativeRect value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out int value,
        int valueSize);
}
