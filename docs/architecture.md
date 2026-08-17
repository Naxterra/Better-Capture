# Architecture

## Product constraints

BetterCapture is offline by construction. Capture files are ordinary files and
will never depend on the library database for recovery. All shipping binaries
are x64. Windows 11 build 26100 is the minimum supported OS.

## Capture pipeline

```text
Windows DWM (HDR or SDR desktop)
  -> Windows.Graphics.Capture
  -> D3D11 R16G16B16A16_FLOAT frame pool
  -> linear scRGB frame (1.0 = 80 nits)
     -> OpenEXR FP16 HDR master
     -> undo Windows SDR-white boost -> highlight mapping
        -> sRGB PNG / clipboard / UI preview
```

An SDR-only BGRA capture path is not used on an HDR desktop because it clips or
washes out highlights. Still captures remain GPU-resident until the single
readback required for encoding. Video will keep frames GPU-resident through the
hardware encoder.

On an HDR desktop, DWM raises ordinary SDR white from nominal 80 nits to the
user's configured SDR white level (commonly around 200 nits). Before producing
an SDR PNG, BetterCapture reads that monitor-specific value and multiplies linear
scRGB by `80 / configured SDR white nits`. Applying a filmic curve directly to
the DWM values makes midtones pale and is intentionally prohibited by tests.

## Projects

- `BetterCapture.App`: WinUI 3 shell, selection overlay, hotkeys, clipboard.
- `BetterCapture.Capture`: Windows Graphics Capture and D3D11 interop.
- `BetterCapture.Graphics`: color conversion, tone mapping, PNG and EXR encoding.
- `BetterCapture.Video`: fixed-rate H.264/MP4 recording through Media Foundation.
- `BetterCapture.Editor`: Skia-based raster operations and annotation model.
- `BetterCapture.Core`: capture contracts and platform-neutral frame geometry.
- `BetterCapture.Core.Tests`: deterministic geometry, color, and encoder tests.

Future `Scrolling`, indexed `Library`, and audio modules will depend on Core
contracts instead of reaching into the capture engine.

## Video pipeline

The smart selector defines a fixed monitor-relative recording rectangle. WGC
continues to acquire FP16 scRGB frames, D3D11 copies only the selected rectangle
into a staging texture, and the SDR-white-aware converter produces BGRA frames.
Media Foundation's sink writer converts those frames to H.264 in an MP4
container with hardware transforms enabled. A periodic encoder loop repeats the
latest desktop frame when WGC suppresses unchanged frames, preserving a stable
output frame rate and real-time duration.

The floating stop control uses `WDA_EXCLUDEFROMCAPTURE`, so it does not appear
inside recordings. Video audio is not yet captured.

## Editor model

The editor opens after a screenshot and is also reachable from the compact
controller. Raster edits (crop, resize, blur, cut, paste) operate on a mutable
BGRA document. Text, watermarks, arrows, and geometric shapes remain separate
annotations until save, so zooming and undo/redo do not repeatedly degrade the
image. Text annotations carry stable identities and measured bounds: the user
clicks or drags the destination first, edits in an inline XAML text box, and can
click the annotation later to edit it again. Installed system fonts are provided
by the Skia font manager.

## Library plan

Media is stored under a persistent user-selected root using `YYYY/MM` folders.
Each capture has an ordinary PNG, an optional EXR HDR master, and a JSON sidecar
containing its source application, window title, selection kind, desktop bounds,
resolution, and color metadata. PNG files also carry embedded UTF-8 source
metadata.

The current library scans ordinary media and sidecar files asynchronously. Its
grid uses an `ItemsWrapGrid` virtualization panel, and thumbnails are requested
only as containers become visible; Windows supplies and caches the bounded
picture/video thumbnails. Search operates on source app, window description,
and filename.

A later SQLite index will add tags, favorites, notes, and keyset-paginated
search without becoming the owner of any media.

Deleting `library.db` must never delete or invalidate captured media; a rescan
reconstructs the index.

## HDR output policy

- The internal interchange format is linear FP16 scRGB.
- HDR masters are OpenEXR files with BT.709/sRGB primaries and D65 white metadata.
- Compatibility images and clipboard data are 8-bit sRGB PNG after tone mapping.
- Display peak luminance is advisory metadata, not a hard clamp. HDR400 and
  HDR1000 displays use the same FP16 path.

Later HDR video uses scRGB -> Rec.2020/PQ -> P010 and a hardware HEVC Main10 or
AV1 10-bit encoder, including mastering/display metadata where available.
