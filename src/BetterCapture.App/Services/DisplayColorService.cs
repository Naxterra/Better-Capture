using Microsoft.Graphics.Display;
using BetterCapture.Core.Capture;
using BetterCapture.Graphics.Color;

namespace BetterCapture.App.Services;

internal static class DisplayColorService
{
    internal static float? TryGetSdrWhiteLevelNits(DisplayTarget target)
    {
        try
        {
            var displayId = Microsoft.UI.Win32Interop.GetDisplayIdFromMonitor(target.MonitorHandle);
            using var displayInformation = DisplayInformation.CreateForDisplayId(displayId);
            var advancedColor = displayInformation.GetAdvancedColorInfo();
            var value = (float)advancedColor.SdrWhiteLevelInNits;
            return float.IsFinite(value) && value > 0 ? value : null;
        }
        catch
        {
            // Some driver/remote-display combinations do not expose Advanced
            // Color information. The workflow chooses an explicit safe fallback.
            return null;
        }
    }

    internal static float ResolveSdrWhiteLevel(float? reportedValue, bool advancedColorEnabled) =>
        reportedValue ?? (advancedColorEnabled ? 203f : ToneMapSettings.ScRgbReferenceWhiteNits);
}
