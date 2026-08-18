using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BetterCapture.App.Services;

internal static partial class LanguagePreferenceService
{
    private const uint MuiLanguageName = 0x8;
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BetterCapture",
        "language.txt");

    internal static string CurrentLanguage { get; private set; } = ResolveLanguage();

    internal static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(CurrentLanguage);

    [ModuleInitializer]
    internal static void InitializeModule() => ApplySavedLanguage();

    internal static void ApplySavedLanguage()
    {
        CurrentLanguage = ResolveLanguage();
        Culture = CultureInfo.GetCultureInfo(CurrentLanguage);
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;

        try
        {
            global::Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = CurrentLanguage;
        }
        catch
        {
        }

        var languageList = CurrentLanguage + "\0";
        _ = SetProcessPreferredUILanguages(MuiLanguageName, languageList, out _);
        _ = SetThreadPreferredUILanguages(MuiLanguageName, languageList, out _);
    }

    internal static void Save(string language)
    {
        var normalized = Normalize(language);
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".partial";
        File.WriteAllText(temporaryPath, normalized);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
        CurrentLanguage = normalized;
    }

    private static string ResolveLanguage()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return Normalize(File.ReadAllText(SettingsPath));
            }
        }
        catch
        {
        }

        return CultureInfo.CurrentUICulture.Name.StartsWith("de", StringComparison.OrdinalIgnoreCase)
            ? "de-DE"
            : "en-US";
    }

    private static string Normalize(string language) =>
        language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "de-DE" : "en-US";

    [LibraryImport("kernel32.dll", EntryPoint = "SetProcessPreferredUILanguages", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessPreferredUILanguages(
        uint flags,
        string languages,
        out uint numberOfLanguages);

    [LibraryImport("kernel32.dll", EntryPoint = "SetThreadPreferredUILanguages", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadPreferredUILanguages(
        uint flags,
        string languages,
        out uint numberOfLanguages);
}
