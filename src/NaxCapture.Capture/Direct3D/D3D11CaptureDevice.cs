using System.Runtime.InteropServices;
using NaxCapture.Capture.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using static Vortice.Direct3D11.D3D11;

namespace NaxCapture.Capture.Direct3D;

internal sealed class D3D11CaptureDevice : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0,
    ];

    private readonly IDirect3DDevice _winRtDevice;
    private bool _disposed;

    private D3D11CaptureDevice(
        ID3D11Device device,
        ID3D11DeviceContext context,
        IDirect3DDevice winRtDevice,
        string adapterName,
        FeatureLevel featureLevel,
        bool advancedColorEnabled,
        float? maximumLuminanceNits)
    {
        Device = device;
        Context = context;
        _winRtDevice = winRtDevice;
        AdapterName = adapterName;
        FeatureLevel = featureLevel;
        AdvancedColorEnabled = advancedColorEnabled;
        MaximumLuminanceNits = maximumLuminanceNits;
    }

    internal ID3D11Device Device { get; }

    internal ID3D11DeviceContext Context { get; }

    internal IDirect3DDevice WinRtDevice => _winRtDevice;

    internal string AdapterName { get; }

    internal FeatureLevel FeatureLevel { get; }

    internal bool AdvancedColorEnabled { get; }

    internal float? MaximumLuminanceNits { get; }

    internal static D3D11CaptureDevice Create(nint monitorHandle)
    {
        using var adapterMatch = DxgiAdapterMatch.Find(monitorHandle);
        var flags = DeviceCreationFlags.BgraSupport;

        D3D11CreateDevice(
            adapterMatch.Adapter,
            DriverType.Unknown,
            flags,
            FeatureLevels,
            out var device,
            out var featureLevel,
            out var context).CheckError();

        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            var result = NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(
                dxgiDevice.NativePointer,
                out var inspectable);
            Marshal.ThrowExceptionForHR(result);

            try
            {
                var winRtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                return new D3D11CaptureDevice(
                    device,
                    context,
                    winRtDevice,
                    adapterMatch.AdapterName,
                    featureLevel,
                    adapterMatch.AdvancedColorEnabled,
                    adapterMatch.MaximumLuminanceNits);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
        }
        catch
        {
            context.Dispose();
            device.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        (_winRtDevice as IDisposable)?.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
