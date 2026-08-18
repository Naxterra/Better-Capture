namespace BetterCapture.App.Services;

internal static class TrayPreferenceService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BetterCapture",
        "minimize-to-tray.txt");

    internal static bool Load()
    {
        try
        {
            if (File.Exists(SettingsPath) &&
                bool.TryParse(File.ReadAllText(SettingsPath), out var enabled))
            {
                return enabled;
            }
        }
        catch
        {
        }

        return true;
    }

    internal static void Save(bool enabled)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".partial";
        File.WriteAllText(temporaryPath, enabled.ToString());
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
