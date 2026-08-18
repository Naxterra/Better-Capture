using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace BetterCapture.App.Services;

internal sealed class TrayIconService : IDisposable
{
    private const uint IconId = 0x4243;
    private const uint CallbackMessage = 0x8000 + 0x42;
    private const uint WindowMessageSize = 0x0005;
    private const uint WindowMessageNull = 0x0000;
    private const uint WindowMessageLeftButtonUp = 0x0202;
    private const uint WindowMessageLeftButtonDoubleClick = 0x0203;
    private const uint WindowMessageRightButtonUp = 0x0205;
    private const nuint SizeMinimized = 1;
    private const nuint SubclassId = 0x42435459;
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconIcon = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint ImageIcon = 1;
    private const uint LoadResourceFromFile = 0x00000010;
    private const uint MenuString = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackRightButton = 0x0002;
    private const uint TrackReturnCommand = 0x0100;
    private const uint TrackNoNotify = 0x0080;
    private const uint OpenCommand = 1;
    private const uint CaptureCommand = 2;
    private const uint ExitCommand = 3;

    private readonly nint _windowHandle;
    private readonly SubclassProcedure _subclassProcedure;
    private readonly uint _taskbarCreatedMessage;
    private NotifyIconData _iconData;
    private nint _iconHandle;
    private bool _subclassInstalled;
    private bool _enabled;
    private bool _iconAdded;

    internal TrayIconService(Window window)
    {
        _windowHandle = WindowNative.GetWindowHandle(window);
        _subclassProcedure = WindowSubclass;
        _subclassInstalled = SetWindowSubclass(_windowHandle, _subclassProcedure, SubclassId, 0);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _iconHandle = LoadImage(
            0,
            Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
            ImageIcon,
            32,
            32,
            LoadResourceFromFile);
        _iconData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = IconId,
            Flags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip,
            CallbackMessage = CallbackMessage,
            IconHandle = _iconHandle,
            Tip = "BetterCapture",
            Info = string.Empty,
            InfoTitle = string.Empty,
        };
    }

    internal event EventHandler? OpenRequested;

    internal event EventHandler? CaptureRequested;

    internal event EventHandler? ExitRequested;

    internal event EventHandler? Minimized;

    internal void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            AddIcon();
        }
        else
        {
            RemoveIcon();
        }
    }

    public void Dispose()
    {
        RemoveIcon();
        if (_subclassInstalled)
        {
            RemoveWindowSubclass(_windowHandle, _subclassProcedure, SubclassId);
            _subclassInstalled = false;
        }

        if (_iconHandle != 0)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = 0;
        }
    }

    private void AddIcon()
    {
        if (_iconAdded || !_subclassInstalled || _iconHandle == 0)
        {
            return;
        }

        _iconData.IconHandle = _iconHandle;
        _iconAdded = ShellNotifyIcon(NotifyIconAdd, ref _iconData);
    }

    private void RemoveIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        ShellNotifyIcon(NotifyIconDelete, ref _iconData);
        _iconAdded = false;
    }

    private nint WindowSubclass(
        nint windowHandle,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == WindowMessageSize && wordParameter == SizeMinimized)
        {
            Minimized?.Invoke(this, EventArgs.Empty);
        }
        else if (message == CallbackMessage && _enabled)
        {
            var notification = (uint)(longParameter.ToInt64() & 0xffff);
            if (notification is WindowMessageLeftButtonUp or WindowMessageLeftButtonDoubleClick)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (notification == WindowMessageRightButtonUp)
            {
                ShowContextMenu();
            }
        }
        else if (message == _taskbarCreatedMessage && _enabled)
        {
            _iconAdded = false;
            AddIcon();
        }

        return DefSubclassProc(windowHandle, message, wordParameter, longParameter);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MenuString, OpenCommand, Localizer.Get("TrayOpen"));
            AppendMenu(menu, MenuString, CaptureCommand, Localizer.Get("TrayCapture"));
            AppendMenu(menu, MenuSeparator, 0, null);
            AppendMenu(menu, MenuString, ExitCommand, Localizer.Get("TrayExit"));
            GetCursorPosition(out var cursor);
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(
                menu,
                TrackRightButton | TrackReturnCommand | TrackNoNotify,
                cursor.X,
                cursor.Y,
                0,
                _windowHandle,
                0);
            PostMessage(_windowHandle, WindowMessageNull, 0, 0);
            if (command == OpenCommand)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (command == CaptureCommand)
            {
                CaptureRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (command == ExitCommand)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal uint Size;
        internal nint WindowHandle;
        internal uint Id;
        internal uint Flags;
        internal uint CallbackMessage;
        internal nint IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Tip;

        internal uint State;
        internal uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string Info;

        internal uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string InfoTitle;

        internal uint InfoFlags;
        internal Guid GuidItem;
        internal nint BalloonIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint SubclassProcedure(
        nint windowHandle,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassId,
        nuint referenceData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint windowHandle,
        SubclassProcedure procedure,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint windowHandle,
        SubclassProcedure procedure,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint windowHandle,
        uint message,
        nuint wordParameter,
        nint longParameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern nint LoadImage(
        nint instance,
        string name,
        uint type,
        int width,
        int height,
        uint loadFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, uint itemId, string? text);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        nint menu,
        uint flags,
        int x,
        int y,
        int reserved,
        nint windowHandle,
        nint rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPosition(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint windowHandle, uint message, nuint wordParameter, nint longParameter);
}
