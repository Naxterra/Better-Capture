using System.Text.Json;

namespace BetterCapture.App.Library;

internal static class LibraryScanner
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif", ".bmp", ".dng", ".exr", ".gif", ".heic", ".heif", ".jfif",
        ".jpg", ".jpeg", ".jxl", ".png", ".tif", ".tiff", ".webp",
    };

    private static readonly HashSet<string> EditableImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".jfif", ".jpg", ".jpeg", ".png", ".webp",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".webm", ".wmv",
    };

    internal static Task<IReadOnlyList<LibraryItemViewModel>> ScanAsync(
        string root,
        CancellationToken cancellationToken = default) => Task.Run(
        () => Scan(root, cancellationToken),
        cancellationToken);

    private static IReadOnlyList<LibraryItemViewModel> Scan(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var items = new List<LibraryItemViewModel>();
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
        };
        foreach (var path in Directory.EnumerateFiles(root, "*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = System.IO.Path.GetExtension(path);
            var kind = ImageExtensions.Contains(extension)
                ? LibraryMediaKind.Image
                : VideoExtensions.Contains(extension)
                    ? LibraryMediaKind.Video
                    : (LibraryMediaKind?)null;
            if (kind is null || System.IO.Path.GetFileNameWithoutExtension(path)
                    .EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var file = new FileInfo(path);
            var metadataPath = kind == LibraryMediaKind.Video
                ? System.IO.Path.ChangeExtension(path, ".video.json")
                : System.IO.Path.ChangeExtension(path, ".json");
            var metadata = ReadMetadata(metadataPath);
            var capturedAt = metadata.CapturedAt ?? new DateTimeOffset(file.LastWriteTime);
            var description = FirstNonEmpty(
                metadata.Description,
                metadata.SourceApplication,
                System.IO.Path.GetFileNameWithoutExtension(path));

            items.Add(new LibraryItemViewModel(
                path,
                kind.Value,
                capturedAt,
                description,
                metadata.SourceApplication ?? string.Empty,
                metadata.Width,
                metadata.Height,
                metadata.Duration,
                file.Length,
                EditableImageExtensions.Contains(extension)));
        }

        return items
            .OrderByDescending(item => item.CapturedAt)
            .ThenByDescending(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LibraryMetadata ReadMetadata(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new LibraryMetadata(
                ReadDate(root, "CapturedAt"),
                ReadString(root, "Description"),
                ReadString(root, "SourceApplication"),
                ReadInt32(root, "Width"),
                ReadInt32(root, "Height"),
                ReadTimeSpan(root, "Duration"));
        }
        catch
        {
            return default;
        }
    }

    private static DateTimeOffset? ReadDate(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), out var result)
            ? result
            : null;

    private static TimeSpan? ReadTimeSpan(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        TimeSpan.TryParse(value.GetString(), out var result)
            ? result
            : null;

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private readonly record struct LibraryMetadata(
        DateTimeOffset? CapturedAt,
        string? Description,
        string? SourceApplication,
        int Width,
        int Height,
        TimeSpan? Duration);
}
