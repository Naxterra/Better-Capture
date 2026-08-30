using BetterCapture.Core.Storage;

namespace BetterCapture.Core.Tests.Storage;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public async Task WriteAndReplace_LeaveNoPartialFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bettercapture-atomic-{Guid.NewGuid():N}");
        var destination = Path.Combine(directory, "capture.json");
        try
        {
            AtomicFileWriter.Write(destination, stream =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write("first");
                writer.Flush();
            });
            await AtomicFileWriter.WriteAllTextAsync(destination, "second");

            Assert.Equal("second", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
