using BetterCapture.Core.Capture;
using BetterCapture.Core.Geometry;
using BetterCapture.Core.Storage;
using BetterCapture.Core.Video;
using BetterCapture.Graphics.Color;
using BetterCapture.Graphics.Encoding;
using BetterCapture.Graphics.Images;
using BetterCapture.Video;
using System.Text.Json;
using Windows.System;

namespace BetterCapture.App.Services;

internal sealed class CaptureWorkflow
{
    private readonly IDisplayCaptureService _captureService;
    private readonly AppSettingsService _settingsService;
    private readonly WindowsGraphicsVideoRecorder _videoRecorder = new();

    internal CaptureWorkflow(
        IDisplayCaptureService captureService,
        AppSettingsService settingsService)
    {
        _captureService = captureService;
        _settingsService = settingsService;
    }

    internal string OutputRoot => _settingsService.LibraryRoot;

    internal async Task<PreparedCapture> PrepareAsync(DisplayTarget target, CaptureOptions options)
    {
        // DisplayInformation must be created on the UI thread that owns a
        // DispatcherQueue, so query it before the first await.
        var reportedSdrWhite = DisplayColorService.TryGetSdrWhiteLevelNits(target);
        var frame = await _captureService.CaptureAsync(target, options);
        var sdrWhiteLevel = DisplayColorService.ResolveSdrWhiteLevel(
            reportedSdrWhite,
            frame.Metadata.AdvancedColorEnabled);
        var toneMapSettings = new ToneMapSettings { SdrWhiteLevelNits = sdrWhiteLevel };
        var preview = await Task.Run(() => ScRgbToneMapper.ToBgra8(frame, settings: toneMapSettings));
        return new PreparedCapture(
            target,
            frame,
            preview,
            ScRgbToneMapper.Analyze(frame),
            sdrWhiteLevel);
    }

    internal async Task<CaptureSaveResult> SaveAsync(
        PreparedCapture prepared,
        SmartCaptureSelection selection,
        bool saveHdrMaster)
    {
        var selectedFrame = prepared.Frame.Crop(selection.Region);
        var analysis = ScRgbToneMapper.Analyze(selectedFrame);
        var paths = FindAvailablePaths(selectedFrame.Metadata.CapturedAt);
        Directory.CreateDirectory(paths.Directory);
        var capturedDesktopBounds = new PixelRect(
            selection.Region.X + prepared.Target.DesktopBounds.X,
            selection.Region.Y + prepared.Target.DesktopBounds.Y,
            selection.Region.Width,
            selection.Region.Height);
        var embeddedMetadata = new Dictionary<string, string>
        {
            ["Description"] = selection.Source.Description,
            ["SourceApplication"] = selection.Source.ApplicationName,
            ["SourceWindow"] = selection.Source.WindowTitle,
            ["CaptureKind"] = selection.Source.Kind.ToString(),
            ["Software"] = "BetterCapture",
        };

        var pngTask = Task.Run(() =>
        {
            var image = ScRgbToneMapper.ToBgra8(
                selectedFrame,
                settings: new ToneMapSettings { SdrWhiteLevelNits = prepared.SdrWhiteLevelNits });
            PngWriter.Write(paths.SdrPngPath, image, embeddedMetadata);
        });
        var exrTask = saveHdrMaster
            ? Task.Run(() => OpenExrWriter.Write(paths.HdrExrPath, selectedFrame))
            : Task.CompletedTask;
        var metadataTask = WriteMetadataAsync(
            paths,
            selectedFrame,
            selection.Source,
            capturedDesktopBounds,
            analysis,
            prepared.SdrWhiteLevelNits,
            saveHdrMaster);
        await Task.WhenAll(pngTask, exrTask, metadataTask);

        return new CaptureSaveResult(
            paths.SdrPngPath,
            paths.HdrExrPath,
            selectedFrame.Width,
            selectedFrame.Height,
            analysis,
            selectedFrame.Metadata,
            prepared.SdrWhiteLevelNits,
            selection.Source,
            paths.MetadataPath);
    }

    internal async Task OpenOutputFolderAsync()
    {
        Directory.CreateDirectory(OutputRoot);
        var folder = await global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(OutputRoot);
        await Launcher.LaunchFolderAsync(folder);
    }

    internal string? FindMostRecentImage()
    {
        try
        {
            if (!Directory.Exists(OutputRoot))
            {
                return null;
            }

            return Directory
                .EnumerateFiles(OutputRoot, "*.png", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal VideoRecordingSession StartVideoRecording(
        PreparedCapture prepared,
        SmartCaptureSelection selection,
        bool includeCursor)
    {
        var paths = FindAvailableVideoPaths(prepared.Frame.Metadata.CapturedAt);
        Directory.CreateDirectory(paths.Directory);
        var pixelCount = (long)selection.Region.Width * selection.Region.Height;
        var bitRate = checked((int)Math.Clamp(pixelCount * 8L, 4_000_000L, 40_000_000L));

        return _videoRecorder.Start(
            prepared.Target,
            selection,
            paths.VideoPath,
            paths.MetadataPath,
            new VideoRecordingOptions
            {
                FramesPerSecond = 30,
                BitRate = bitRate,
                IncludeCursor = includeCursor,
                SdrWhiteLevelNits = prepared.SdrWhiteLevelNits,
            });
    }

    internal async Task WriteVideoMetadataAsync(VideoRecordingResult result)
    {
        var document = new VideoMetadataDocument(
            SchemaVersion: 1,
            CapturedAt: DateTimeOffset.Now - result.Duration,
            Description: result.Source.Description,
            CaptureKind: result.Source.Kind.ToString(),
            SourceApplication: result.Source.ApplicationName,
            SourceWindowTitle: result.Source.WindowTitle,
            SourceWindowBounds: result.Source.DesktopBounds,
            CapturedDesktopBounds: result.CapturedDesktopBounds,
            Width: result.Width,
            Height: result.Height,
            Duration: result.Duration,
            FramesPerSecond: result.FramesPerSecond,
            BitRate: result.BitRate,
            SdrWhiteLevelNits: result.SdrWhiteLevelNits,
            EncodedFrameCount: result.EncodedFrameCount,
            DroppedFrameCount: result.DroppedFrameCount,
            VideoFile: Path.GetFileName(result.VideoPath));
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = result.MetadataPath + ".partial";
        await File.WriteAllTextAsync(temporaryPath, json);
        File.Move(temporaryPath, result.MetadataPath, overwrite: true);
    }

    private CapturePaths FindAvailablePaths(DateTimeOffset capturedAt)
    {
        for (var offset = 0; offset < 1000; offset++)
        {
            var paths = CapturePathPlanner.Create(OutputRoot, capturedAt.AddMilliseconds(offset));
            if (!File.Exists(paths.SdrPngPath) &&
                !File.Exists(paths.HdrExrPath) &&
                !File.Exists(paths.MetadataPath))
            {
                return paths;
            }
        }

        throw new IOException("Could not allocate a unique capture filename.");
    }

    private VideoCapturePaths FindAvailableVideoPaths(DateTimeOffset capturedAt)
    {
        for (var offset = 0; offset < 1000; offset++)
        {
            var paths = CapturePathPlanner.CreateVideo(OutputRoot, capturedAt.AddMilliseconds(offset));
            if (!File.Exists(paths.VideoPath) && !File.Exists(paths.MetadataPath))
            {
                return paths;
            }
        }

        throw new IOException("Could not allocate a unique video filename.");
    }

    private static async Task WriteMetadataAsync(
        CapturePaths paths,
        ScRgbFrame frame,
        CaptureSourceInfo source,
        PixelRect capturedDesktopBounds,
        HdrAnalysis analysis,
        float sdrWhiteLevelNits,
        bool hasHdrMaster)
    {
        var entry = new CaptureMetadataDocument(
            SchemaVersion: 1,
            CapturedAt: frame.Metadata.CapturedAt,
            Description: source.Description,
            CaptureKind: source.Kind.ToString(),
            SourceApplication: source.ApplicationName,
            SourceWindowTitle: source.WindowTitle,
            SourceWindowBounds: source.DesktopBounds,
            CapturedDesktopBounds: capturedDesktopBounds,
            Width: frame.Width,
            Height: frame.Height,
            HasExtendedRange: analysis.HasExtendedRange,
            MaximumContentLuminanceNits: analysis.MaximumLuminanceNits,
            SdrWhiteLevelNits: sdrWhiteLevelNits,
            PngFile: Path.GetFileName(paths.SdrPngPath),
            HdrFile: hasHdrMaster ? Path.GetFileName(paths.HdrExrPath) : null);
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = paths.MetadataPath + ".partial";
        await File.WriteAllTextAsync(temporaryPath, json);
        File.Move(temporaryPath, paths.MetadataPath, overwrite: true);
    }
}

internal sealed record CaptureMetadataDocument(
    int SchemaVersion,
    DateTimeOffset CapturedAt,
    string Description,
    string CaptureKind,
    string SourceApplication,
    string SourceWindowTitle,
    PixelRect SourceWindowBounds,
    PixelRect CapturedDesktopBounds,
    int Width,
    int Height,
    bool HasExtendedRange,
    float MaximumContentLuminanceNits,
    float SdrWhiteLevelNits,
    string PngFile,
    string? HdrFile);

internal sealed record VideoMetadataDocument(
    int SchemaVersion,
    DateTimeOffset CapturedAt,
    string Description,
    string CaptureKind,
    string SourceApplication,
    string SourceWindowTitle,
    PixelRect SourceWindowBounds,
    PixelRect CapturedDesktopBounds,
    int Width,
    int Height,
    TimeSpan Duration,
    int FramesPerSecond,
    int BitRate,
    float SdrWhiteLevelNits,
    long EncodedFrameCount,
    long DroppedFrameCount,
    string VideoFile);

internal sealed record PreparedCapture(
    DisplayTarget Target,
    ScRgbFrame Frame,
    Bgra8Image Preview,
    HdrAnalysis Analysis,
    float SdrWhiteLevelNits);

internal sealed record CaptureSaveResult(
    string PngPath,
    string ExrPath,
    int Width,
    int Height,
    HdrAnalysis Analysis,
    CaptureFrameMetadata Metadata,
    float SdrWhiteLevelNits,
    CaptureSourceInfo Source,
    string MetadataPath);
