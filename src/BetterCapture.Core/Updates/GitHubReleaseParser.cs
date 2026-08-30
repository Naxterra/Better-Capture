using System.Text.Json;

namespace BetterCapture.Core.Updates;

public static class GitHubReleaseParser
{
    public static ReleaseUpdate? ParseLatest(string json, Version currentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(currentVersion);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = RequiredString(root, "tag_name");
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= currentVersion)
        {
            return null;
        }

        var expectedAssetName = $"BetterCapture-v{version.ToString(3)}-Windows-x64-Setup.exe";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The release does not contain downloadable assets.");
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = RequiredString(asset, "name");
            if (!string.Equals(name, expectedAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var digest = RequiredString(asset, "digest");
            const string prefix = "sha256:";
            if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                digest.Length != prefix.Length + 64)
            {
                throw new InvalidDataException("The installer asset has no valid SHA-256 digest.");
            }

            var hash = digest[prefix.Length..].ToUpperInvariant();
            if (hash.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("The installer SHA-256 digest is invalid.");
            }

            return new ReleaseUpdate(
                version,
                tag,
                name,
                new Uri(RequiredString(asset, "browser_download_url"), UriKind.Absolute),
                hash,
                asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                root.TryGetProperty("html_url", out var page) ? page.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty);
        }

        throw new InvalidDataException($"The release does not contain {expectedAssetName}.");
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"The release property '{propertyName}' is missing.");
        }

        return property.GetString()!;
    }
}

public sealed record ReleaseUpdate(
    Version Version,
    string Tag,
    string AssetName,
    Uri DownloadUri,
    string Sha256,
    long Size,
    string ReleasePage,
    string Notes);
