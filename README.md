<p align="center">
  <img src="src/BetterCapture.App/Assets/BetterCaptureLogo-1024.png" width="128" alt="BetterCapture logo">
</p>

# BetterCapture

BetterCapture is a local-first, x64 Windows 11 capture application designed around
HDR correctness, stability, and a deliberately small interface.

The first milestone has one acceptance test:

> Press Print Screen, click the detected window or drag a custom region, and
> receive both an HDR master and a correctly tone-mapped SDR image without
> disabling Windows HDR.

## Current scope

- Windows 11 24H2 or newer (build 26100+), x64 only
- WinUI 3 desktop shell
- `Windows.Graphics.Capture` with an FP16/scRGB D3D11 frame pool
- Smart window, maximized/full-screen, desktop, and custom-region selection
- Local OpenEXR HDR master plus tone-mapped PNG
- SDR PNG clipboard copy
- H.264 MP4 recording of any smart-selected window or region
- Hardware Media Foundation encoder support and a capture-excluded stop control
- Image editor with crop, resize, blur, cut/copy/paste, text, watermark,
  arrows, rectangles, ellipses, triangles, installed fonts, sizes, undo/redo,
  and 10–1600% zoom
- Location-first inline text boxes that remain editable after placement
- Conventional localized File/Edit/Image/Tools/View/Help menus with keyboard
  shortcuts, a fixed canvas toolbar, and a dedicated contextual properties sidebar
- Print Screen global hotkey
- User-selectable library root with `YYYY\MM` folders
- PNG metadata and JSON sidecars describing the source app and window
- Virtualized screenshot/video library with lazy Windows thumbnail loading,
  source descriptions, search, and direct image-editor/video-player opening
- English and German UI localization following the Windows display language
- No account, cloud service, telemetry, or network runtime dependency

SQLite tags/favorites, scrolling capture, and audio recording remain planned
modules. They intentionally do not share state with the capture engine. See
[`docs/architecture.md`](docs/architecture.md).

## Build

```powershell
dotnet restore .\BetterCapture.slnx
dotnet build .\BetterCapture.slnx -c Debug -p:Platform=x64
dotnet test .\tests\BetterCapture.Core.Tests\BetterCapture.Core.Tests.csproj -c Debug
```

Build the native x64 Windows installer after installing Inno Setup 7:

```powershell
.\installer\Build-Installer.ps1
```

To validate the FP16 path without launching the full UI, place the mouse on the
target display and run:

```powershell
dotnet run --project .\tools\BetterCapture.CaptureProbe\BetterCapture.CaptureProbe.csproj
```

The app is packaged and self-contained. The Windows setting **Use the Print
Screen key to open screen capture** may need to be turned off if Windows has
already reserved that key.
