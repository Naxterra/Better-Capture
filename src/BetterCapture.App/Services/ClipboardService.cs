using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BetterCapture.App.Services;

internal static class ClipboardService
{
    internal static async Task CopyPngAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy,
        };
        package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
