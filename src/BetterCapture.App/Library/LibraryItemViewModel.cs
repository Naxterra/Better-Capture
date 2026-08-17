using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace BetterCapture.App.Library;

public sealed class LibraryItemViewModel : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private int _thumbnailState;

    internal LibraryItemViewModel(
        string path,
        LibraryMediaKind kind,
        DateTimeOffset capturedAt,
        string description,
        string sourceApplication,
        int width,
        int height,
        TimeSpan? duration,
        long fileSize,
        bool canEdit)
    {
        Path = path;
        Kind = kind;
        CapturedAt = capturedAt;
        Description = description;
        SourceApplication = sourceApplication;
        Width = width;
        Height = height;
        Duration = duration;
        FileSize = fileSize;
        CanEdit = canEdit;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }

    public LibraryMediaKind Kind { get; }

    public DateTimeOffset CapturedAt { get; }

    public string Description { get; }

    public string SourceApplication { get; }

    public int Width { get; }

    public int Height { get; }

    public TimeSpan? Duration { get; }

    public long FileSize { get; }

    public bool CanEdit { get; }

    public string CapturedAtDisplay => CapturedAt.ToLocalTime().ToString("g");

    public string Details
    {
        get
        {
            var dimensions = Width > 0 && Height > 0 ? $"{Width} × {Height}" : FormatFileSize(FileSize);
            return Kind == LibraryMediaKind.Video && Duration is not null
                ? $"{dimensions}  ·  {Duration.Value.ToString(@"mm\:ss")}"
                : dimensions;
        }
    }

    public string KindLabel => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    internal async Task EnsureThumbnailAsync()
    {
        if (Interlocked.CompareExchange(ref _thumbnailState, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path);
            using var thumbnail = await file.GetThumbnailAsync(
                Kind == LibraryMediaKind.Video ? ThumbnailMode.VideosView : ThumbnailMode.PicturesView,
                320,
                ThumbnailOptions.UseCurrentScale);
            if (thumbnail.Size == 0)
            {
                return;
            }

            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            Thumbnail = image;
            Interlocked.Exchange(ref _thumbnailState, 2);
        }
        catch
        {
            Interlocked.Exchange(ref _thumbnailState, 0);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatFileSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024d):0.##} MB"
        : $"{Math.Max(1, bytes / 1024d):0.#} KB";
}
