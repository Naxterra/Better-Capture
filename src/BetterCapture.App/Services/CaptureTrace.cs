namespace BetterCapture.App.Services;

internal static class CaptureTrace
{
    private const long MaximumLogBytes = 1_048_576;
    private static readonly object Sync = new();
    private static readonly string LogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BetterCapture",
        "capture-trace.log");

    internal static string Path => LogPath;

    internal static void StartSession()
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaximumLogBytes)
            {
                File.Delete(LogPath);
            }
        }
        catch
        {
        }

        Write($"session started · version {typeof(CaptureTrace).Assembly.GetName().Version}");
    }

    internal static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:O} · pid {Environment.ProcessId} · {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
