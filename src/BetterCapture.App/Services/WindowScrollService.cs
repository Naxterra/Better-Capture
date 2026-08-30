using System.Runtime.InteropServices;
using BetterCapture.Core.Geometry;
using Interop.UIAutomationClient;

namespace BetterCapture.App.Services;

internal static class WindowScrollService
{
    private const uint InputKeyboard = 1;
    private const ushort VirtualKeyDown = 0x28;
    private const uint KeyEventKeyUp = 0x0002;
    private const int FallbackArrowSteps = 6;
    private const double ScrollPatternNoScroll = -1d;
    private const int ScrollPatternId = 10004;
    private const int IsScrollPatternAvailablePropertyId = 30034;
    [ThreadStatic]
    private static IUIAutomation? _threadAutomation;

    // UI Automation COM objects are apartment-bound. Keeping one instance per
    // worker/UI thread avoids cross-apartment marshaling and lets scroll probes
    // run away from the selector's pointer event loop.
    private static IUIAutomation Automation =>
        _threadAutomation ??= new CUIAutomation8Class();

    internal static bool TryGetScrollableViewport(
        nint windowHandle,
        PixelRect windowBounds,
        out PixelRect viewport)
    {
        viewport = default;
        if (windowHandle == 0 || !IsWindow(windowHandle))
        {
            return false;
        }

        try
        {
            var root = Automation.ElementFromHandle(windowHandle);
            var condition = Automation.CreatePropertyCondition(
                IsScrollPatternAvailablePropertyId,
                true);
            var elements = root.FindAll(TreeScope.TreeScope_Subtree, condition);
            var minimumArea = (double)windowBounds.Width * windowBounds.Height / 4;
            double bestArea = 0;
            for (var index = 0; index < elements.Length; index++)
            {
                var element = elements.GetElement(index);
                if (element.GetCurrentPattern(ScrollPatternId) is not IUIAutomationScrollPattern pattern ||
                    pattern.CurrentVerticallyScrollable == 0 ||
                    pattern.CurrentVerticalViewSize >= 99.9)
                {
                    continue;
                }

                var bounds = element.CurrentBoundingRectangle;
                var left = Convert.ToInt32(bounds.left);
                var top = Convert.ToInt32(bounds.top);
                var right = Convert.ToInt32(bounds.right);
                var bottom = Convert.ToInt32(bounds.bottom);
                var candidate = new PixelRect(
                    left,
                    top,
                    Math.Max(0, right - left),
                    Math.Max(0, bottom - top))
                    .Intersect(windowBounds);
                var area = (double)candidate.Width * candidate.Height;
                if (area >= minimumArea && area > bestArea)
                {
                    bestArea = area;
                    viewport = candidate;
                }
            }

            return !viewport.IsEmpty;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            viewport = default;
            return false;
        }
    }

    internal static bool CanScroll(nint windowHandle, PixelRect desktopBounds)
    {
        return TryGetScrollableViewport(windowHandle, desktopBounds, out _);
    }

    internal static bool TryScrollToStart(
        nint windowHandle,
        PixelRect desktopBounds,
        out string details)
    {
        if (windowHandle == 0 || !IsWindow(windowHandle))
        {
            details = $"invalid target hwnd 0x{windowHandle:X}";
            return false;
        }

        _ = SetForegroundWindow(windowHandle);
        Thread.Sleep(80);
        if (GetForegroundWindow() != windowHandle)
        {
            details = $"foreground activation failed · target 0x{windowHandle:X}";
            return false;
        }

        try
        {
            var root = Automation.ElementFromHandle(windowHandle);
            var condition = Automation.CreatePropertyCondition(
                IsScrollPatternAvailablePropertyId,
                true);
            var elements = root.FindAll(TreeScope.TreeScope_Subtree, condition);
            var minimumArea = (double)desktopBounds.Width * desktopBounds.Height / 4;
            IUIAutomationScrollPattern? bestPattern = null;
            double bestArea = 0;
            for (var index = 0; index < elements.Length; index++)
            {
                var element = elements.GetElement(index);
                if (element.GetCurrentPattern(ScrollPatternId) is not IUIAutomationScrollPattern pattern ||
                    pattern.CurrentVerticallyScrollable == 0 ||
                    pattern.CurrentVerticalViewSize >= 99.9)
                {
                    continue;
                }

                var bounds = element.CurrentBoundingRectangle;
                var area = Math.Max(0, bounds.right - bounds.left) *
                    Math.Max(0, bounds.bottom - bounds.top);
                if (area >= minimumArea && area > bestArea)
                {
                    bestArea = area;
                    bestPattern = pattern;
                }
            }

            if (bestPattern is null)
            {
                details = "UI Automation exposed no full-window scroll container";
                return false;
            }

            var before = bestPattern.CurrentVerticalScrollPercent;
            bestPattern.SetScrollPercent(ScrollPatternNoScroll, 0d);
            Thread.Sleep(400);
            var after = bestPattern.CurrentVerticalScrollPercent;
            details = $"UI Automation reset to start · vertical {before:F2}% → {after:F2}% · area {bestArea:F0}";
            return after <= 0.1;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            details = $"UI Automation could not reset to start · {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    internal static bool TryScrollDown(
        nint windowHandle,
        PixelRect desktopBounds,
        out int expectedAdvance,
        out bool reachedEnd,
        out string details)
    {
        expectedAdvance = 0;
        reachedEnd = false;
        if (windowHandle == 0 || !IsWindow(windowHandle))
        {
            details = $"invalid target hwnd 0x{windowHandle:X}";
            return false;
        }

        _ = SetForegroundWindow(windowHandle);
        Thread.Sleep(80);
        if (GetForegroundWindow() != windowHandle)
        {
            details = $"foreground activation failed · target 0x{windowHandle:X} · foreground 0x{GetForegroundWindow():X}";
            return false;
        }

        if (TryAutomationScroll(
                windowHandle,
                desktopBounds,
                out expectedAdvance,
                out reachedEnd,
                out details))
        {
            return true;
        }

        var automationDetails = details;

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

        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(recipient, out _);
        _ = AttachThreadInput(currentThread, targetThread, true);
        try
        {
            _ = SetFocus(recipient);
        }
        finally
        {
            _ = AttachThreadInput(currentThread, targetThread, false);
        }

        var inputs = new Input[FallbackArrowSteps * 2];
        for (var step = 0; step < FallbackArrowSteps; step++)
        {
            inputs[step * 2] = new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = VirtualKeyDown,
                    },
                },
            };
            inputs[(step * 2) + 1] = new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = VirtualKeyDown,
                        Flags = KeyEventKeyUp,
                    },
                },
            };
        }
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        Thread.Sleep(220);
        details = $"{automationDetails} · renderer Down-arrow sequence dispatched · target 0x{windowHandle:X} · " +
            $"recipient 0x{recipient:X} · point {point.X},{point.Y} · sent {sent}";
        return sent == inputs.Length;
    }

    private static bool TryAutomationScroll(
        nint windowHandle,
        PixelRect desktopBounds,
        out int expectedAdvance,
        out bool reachedEnd,
        out string details)
    {
        expectedAdvance = 0;
        reachedEnd = false;
        try
        {
            var root = Automation.ElementFromHandle(windowHandle);
            var condition = Automation.CreatePropertyCondition(
                IsScrollPatternAvailablePropertyId,
                true);
            var elements = root.FindAll(TreeScope.TreeScope_Subtree, condition);
            IUIAutomationScrollPattern? bestPattern = null;
            double bestArea = 0;
            double bestViewportHeight = 0;
            var minimumArea = (double)desktopBounds.Width * desktopBounds.Height / 4;

            for (var index = 0; index < elements.Length; index++)
            {
                var element = elements.GetElement(index);
                if (element.GetCurrentPattern(ScrollPatternId) is not IUIAutomationScrollPattern pattern)
                {
                    continue;
                }

                if (pattern.CurrentVerticallyScrollable == 0 || pattern.CurrentVerticalViewSize >= 99.9)
                {
                    continue;
                }

                var bounds = element.CurrentBoundingRectangle;
                var area = Math.Max(0, bounds.right - bounds.left) *
                    Math.Max(0, bounds.bottom - bounds.top);
                if (area >= minimumArea && area > bestArea)
                {
                    bestArea = area;
                    bestViewportHeight = Math.Max(0, bounds.bottom - bounds.top);
                    bestPattern = pattern;
                }
            }

            if (bestPattern is null)
            {
                details = "UI Automation exposed no vertically scrollable element";
                return false;
            }

            var before = bestPattern.CurrentVerticalScrollPercent;
            var viewSize = bestPattern.CurrentVerticalViewSize;
            var scrollRangeInViewports = Math.Max(0.01, (100d / viewSize) - 1d);
            // Keep a generous overlap so the stitcher can measure the real movement.
            // UI Automation percentages are only a control signal; they are not precise
            // enough to decide how many image rows should be appended.
            var percentDelta = Math.Max(0.15, 18d / scrollRangeInViewports);
            var requested = Math.Min(100d, before + percentDelta);
            bestPattern.SetScrollPercent(ScrollPatternNoScroll, requested);
            Thread.Sleep(120);
            var after = bestPattern.CurrentVerticalScrollPercent;
            reachedEnd = before >= 99.99 && after >= 99.99;
            expectedAdvance = (int)Math.Round(
                Math.Max(0, after - before) / 100d *
                bestViewportHeight *
                scrollRangeInViewports);
            details = $"UI Automation scrolled · vertical {before:F2}% → {after:F2}% " +
                $"(requested {requested:F2}%, view {viewSize:F2}%) · " +
                $"expected advance {expectedAdvance}px · end {reachedEnd} · area {bestArea:F0}";
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            details = $"UI Automation unavailable · {exception.GetType().Name}: {exception.Message}";
            return false;
        }
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

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachInput);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll", EntryPoint = "SendInput")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        internal MouseInput Mouse;

        [FieldOffset(0)]
        internal KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }
}
