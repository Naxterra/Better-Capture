namespace BetterCapture.App.Services;

internal enum CaptureHotkey
{
    PrintScreen,
    ControlPrintScreen,
    AltPrintScreen,
    ControlShiftS,
    F9,
    F10,
    F11,
    F12,
}

internal static class HotkeyPreferenceService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BetterCapture",
        "hotkey.txt");

    internal static IReadOnlyList<CaptureHotkey> Supported { get; } = Enum.GetValues<CaptureHotkey>();

    internal static CaptureHotkey Load()
    {
        try
        {
            if (File.Exists(SettingsPath) &&
                Enum.TryParse<CaptureHotkey>(File.ReadAllText(SettingsPath), out var hotkey))
            {
                return hotkey;
            }
        }
        catch
        {
        }

        return CaptureHotkey.PrintScreen;
    }

    internal static void Save(CaptureHotkey hotkey)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".partial";
        File.WriteAllText(temporaryPath, hotkey.ToString());
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    internal static (uint Modifiers, uint VirtualKey) GetRegistration(CaptureHotkey hotkey) => hotkey switch
    {
        CaptureHotkey.ControlPrintScreen => (0x0002, 0x2c),
        CaptureHotkey.AltPrintScreen => (0x0001, 0x2c),
        CaptureHotkey.ControlShiftS => (0x0002 | 0x0004, 0x53),
        CaptureHotkey.F9 => (0, 0x78),
        CaptureHotkey.F10 => (0, 0x79),
        CaptureHotkey.F11 => (0, 0x7a),
        CaptureHotkey.F12 => (0, 0x7b),
        _ => (0, 0x2c),
    };

    internal static string GetDisplayName(CaptureHotkey hotkey) => hotkey switch
    {
        CaptureHotkey.ControlPrintScreen => $"{Localizer.Get("ControlKeyName")}+{Localizer.Get("PrintScreenKeyName")}",
        CaptureHotkey.AltPrintScreen => $"Alt+{Localizer.Get("PrintScreenKeyName")}",
        CaptureHotkey.ControlShiftS => $"{Localizer.Get("ControlKeyName")}+{Localizer.Get("ShiftKeyName")}+S",
        CaptureHotkey.F9 => "F9",
        CaptureHotkey.F10 => "F10",
        CaptureHotkey.F11 => "F11",
        CaptureHotkey.F12 => "F12",
        _ => Localizer.Get("PrintScreenKeyName"),
    };
}
