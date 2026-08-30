using BetterCapture.Core.Updates;

namespace BetterCapture.Core.Tests.Updates;

public sealed class GitHubReleaseParserTests
{
    [Fact]
    public void ParseLatest_SelectsVersionedX64InstallerAndDigest()
    {
        const string digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var json = $$"""
        {
          "tag_name": "v1.2.3",
          "html_url": "https://github.com/Naxterra/Better-Capture/releases/tag/v1.2.3",
          "body": "Release notes",
          "assets": [
            {
              "name": "BetterCapture-v1.2.3-Windows-x64-Setup.exe",
              "browser_download_url": "https://example.test/BetterCapture-v1.2.3-Windows-x64-Setup.exe",
              "digest": "sha256:{{digest}}",
              "size": 12345
            }
          ]
        }
        """;

        var update = GitHubReleaseParser.ParseLatest(json, new Version(1, 2, 2));

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 2, 3), update.Version);
        Assert.Equal(digest.ToUpperInvariant(), update.Sha256);
        Assert.Equal(12345, update.Size);
    }

    [Fact]
    public void ParseLatest_ReturnsNullWhenInstalledVersionIsCurrent()
    {
        const string json = """
        {
          "tag_name": "v1.2.3",
          "assets": []
        }
        """;

        Assert.Null(GitHubReleaseParser.ParseLatest(json, new Version(1, 2, 3)));
    }
}
