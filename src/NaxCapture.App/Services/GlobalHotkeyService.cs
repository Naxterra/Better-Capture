using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace NaxCapture.App.Services;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4e41;
    private const uint ModifierNoRepeat = 0x4000;
    private const uint VirtualKeySnapshot = 0x2c;
    private const uint WindowMessageHotkey = 0x0312;
    private const nuint SubclassId = 0x4e415843;

    private readonly nint _windowHandle;
    private readonly SubclassProcedure _subclassProcedure;
    private bool _subclassInstalled;
    private bool _registered;

    internal GlobalHotkeyService(Window window)
    {
        _windowHandle = WindowNative.GetWindowHandle(window);
        _subclassProcedure = WindowSubclass;
        _subclassInstalled = SetWindowSubclass(_windowHandle, _subclassProcedure, SubclassId, 0);
    }

    internal event EventHandler? Pressed;

    internal bool TryRegisterPrintScreen(out int errorCode)
    {
        if (!_subclassInstalled)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        _registered = RegisterHotKey(_windowHandle, HotkeyId, ModifierNoRepeat, VirtualKeySnapshot);
        errorCode = _registered ? 0 : Marshal.GetLastWin32Error();
        return _registered;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
            _registered = false;
        }

        if (_subclassInstalled)
        {
            RemoveWindowSubclass(_windowHandle, _subclassProcedure, SubclassId);
            _subclassInstalled = false;
        }
    }

    private nint WindowSubclass(
        nint windowHandle,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == WindowMessageHotkey && (int)wordParameter == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return DefSubclassProc(windowHandle, message, wordParameter, longParameter);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint SubclassProcedure(
        nint windowHandle,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassId,
        nuint referenceData);

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
