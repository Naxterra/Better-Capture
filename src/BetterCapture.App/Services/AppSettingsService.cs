using System.Text.Json;

namespace BetterCapture.App.Services;

internal sealed class AppSettingsService
{
    private const string LegacySettingsDirectoryName = "Nax" + "Capture";

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

        var libraryRoot = LoadLibraryRoot(_settingsPath);
        if (libraryRoot is null)
        {
            var legacySettingsPath = Path.Combine(
                localData,
                LegacySettingsDirectoryName,
                "settings.json");
            libraryRoot = LoadLibraryRoot(legacySettingsPath);
            if (libraryRoot is not null)
            {
                TryPersistSettings(libraryRoot);
            }
        }

        LibraryRoot = libraryRoot ?? CreateDefaultLibraryRoot();
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

        return Path.Combine(pictures, "BetterCapture");
    }

    private void TryPersistSettings(string libraryRoot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = JsonSerializer.Serialize(new StoredSettings(libraryRoot), JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Migration is best-effort; the imported path remains active for this session.
        }
    }

    private sealed record StoredSettings(string LibraryRoot);
}
