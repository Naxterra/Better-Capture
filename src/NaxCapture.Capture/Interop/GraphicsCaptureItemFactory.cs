using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace NaxCapture.Capture.Interop;

internal static class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    internal static GraphicsCaptureItem CreateForMonitor(nint monitorHandle)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForMonitor(monitorHandle, GraphicsCaptureItemGuid);

        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint windowHandle, in Guid interfaceId);

        nint CreateForMonitor(nint monitorHandle, in Guid interfaceId);
    }
}
