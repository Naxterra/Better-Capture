using System.Net.Http.Headers;
using System.Security.Cryptography;
using BetterCapture.Core.Updates;

namespace BetterCapture.App.Services;

internal sealed class UpdateService
{
    private static readonly Uri LatestReleaseApi = new(
        "https://api.github.com/repos/Naxterra/Better-Capture/releases/latest");
    private static readonly HttpClient Client = CreateClient();

    internal async Task<ReleaseUpdate?> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return GitHubReleaseParser.ParseLatest(json, currentVersion);
    }

    internal async Task<string> DownloadAndVerifyAsync(
        ReleaseUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterCapture",
            "Updates");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, update.AssetName);
        var temporaryPath = $"{destination}.{Guid.NewGuid():N}.partial";
        try
        {
            using var response = await Client.GetAsync(
                update.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            var length = new FileInfo(temporaryPath).Length;
            if (update.Size > 0 && length != update.Size)
            {
                throw new InvalidDataException(
                    $"The downloaded installer size is {length}, expected {update.Size} bytes.");
            }

            await using var installer = File.OpenRead(temporaryPath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installer, cancellationToken));
            if (!string.Equals(actualHash, update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded installer failed SHA-256 verification.");
            }

            File.Move(temporaryPath, destination, overwrite: true);
            return destination;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
            }
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BetterCapture", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
