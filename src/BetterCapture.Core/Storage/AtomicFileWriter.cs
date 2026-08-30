using System.Text;

namespace BetterCapture.Core.Storage;

public static class AtomicFileWriter
{
    private const int MaximumAttempts = 4;

    public static void Write(string destinationPath, Action<FileStream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(write);
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        IOException? lastError = null;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            var temporaryPath = CreateTemporaryPath(destination);
            try
            {
                using (var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.SequentialScan))
                {
                    write(output);
                    output.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, destination, overwrite: true);
                return;
            }
            catch (IOException exception) when (attempt < MaximumAttempts)
            {
                lastError = exception;
                TryDelete(temporaryPath);
                Thread.Sleep(75 * attempt * attempt);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        throw lastError ?? new IOException($"Could not write '{destination}'.");
    }

    public static Task WriteAllTextAsync(
        string destinationPath,
        string content,
        CancellationToken cancellationToken = default) => Task.Run(
        () => Write(destinationPath, stream =>
        {
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024,
                leaveOpen: true);
            writer.Write(content);
            writer.Flush();
        }),
        cancellationToken);

    private static string CreateTemporaryPath(string destination) =>
        $"{destination}.{Environment.ProcessId}.{Guid.NewGuid():N}.partial";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
