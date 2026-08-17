using System.Text.Json;

namespace NaxCapture.App.Services;

internal sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    internal AppSettingsService()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDirectory = Path.Combine(localData, "BetterCapture");
        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
        var currentLibraryRoot = LoadLibraryRoot(_settingsPath);
        var legacySettingsPath = Path.Combine(localData, "NaxCapture", "settings.json");
        var legacyLibraryRoot = currentLibraryRoot is null
            ? LoadLibraryRoot(legacySettingsPath)
            : null;
        LibraryRoot = currentLibraryRoot ?? legacyLibraryRoot ?? CreateDefaultLibraryRoot();

        if (currentLibraryRoot is null && legacyLibraryRoot is not null)
        {
            TryPersistMigratedSettings(legacyLibraryRoot);
        }
    }

    internal string LibraryRoot { get; private set; }

    internal async Task SetLibraryRootAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);

        await _writeGate.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _settingsPath + ".partial";
            var json = JsonSerializer.Serialize(new StoredSettings(fullPath), JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
            LibraryRoot = fullPath;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static string? LoadLibraryRoot(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(settingsPath));
            return string.IsNullOrWhiteSpace(settings?.LibraryRoot)
                ? null
                : Path.GetFullPath(settings.LibraryRoot);
        }
        catch
        {
            return null;
        }
    }

    private static string CreateDefaultLibraryRoot()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        var betterCaptureRoot = Path.Combine(pictures, "BetterCapture");
        var legacyRoot = Path.Combine(pictures, "NaxCapture");
        return Directory.Exists(legacyRoot) && !Directory.Exists(betterCaptureRoot)
            ? legacyRoot
            : betterCaptureRoot;
    }

    private void TryPersistMigratedSettings(string libraryRoot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = JsonSerializer.Serialize(new StoredSettings(libraryRoot), JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Migration is best-effort. The legacy setting remains readable.
        }
    }

    private sealed record StoredSettings(string LibraryRoot);
}
