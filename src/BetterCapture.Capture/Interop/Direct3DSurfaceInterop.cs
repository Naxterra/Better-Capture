using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace BetterCapture.Capture.Interop;

internal static class Direct3DSurfaceInterop
{
    internal static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccessNative>();
        var pointer = access.GetInterface(typeof(ID3D11Texture2D).GUID);
        return new ID3D11Texture2D(pointer);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccessNative
    {
        nint GetInterface(in Guid interfaceId);
    }
}
