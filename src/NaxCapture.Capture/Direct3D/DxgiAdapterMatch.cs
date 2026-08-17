using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace NaxCapture.Capture.Direct3D;

internal sealed class DxgiAdapterMatch : IDisposable
{
    private DxgiAdapterMatch(
        IDXGIAdapter1 adapter,
        string adapterName,
        bool advancedColorEnabled,
        float? maximumLuminanceNits)
    {
        Adapter = adapter;
        AdapterName = adapterName;
        AdvancedColorEnabled = advancedColorEnabled;
        MaximumLuminanceNits = maximumLuminanceNits;
    }

    internal IDXGIAdapter1 Adapter { get; }

    internal string AdapterName { get; }

    internal bool AdvancedColorEnabled { get; }

    internal float? MaximumLuminanceNits { get; }

    internal static DxgiAdapterMatch Find(nint monitorHandle)
    {
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();

        for (uint adapterIndex = 0;
             factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success;
             adapterIndex++)
        {
            if (adapter is null)
            {
                continue;
            }

            var matched = false;
            try
            {
                for (uint outputIndex = 0;
                     adapter.EnumOutputs(outputIndex, out IDXGIOutput? output).Success;
                     outputIndex++)
                {
                    if (output is null)
                    {
                        continue;
                    }

                    using (output)
                    {
                        var description = output.Description;
                        if (description.Monitor != monitorHandle)
                        {
                            continue;
                        }

                        matched = true;
                        var advancedColor = false;
                        float? maximumLuminance = null;

                        using var output6 = output.QueryInterfaceOrNull<IDXGIOutput6>();
                        if (output6 is not null)
                        {
                            var description1 = output6.Description1;
                            advancedColor = description1.ColorSpace is
                                ColorSpaceType.RgbFullG2084NoneP2020 or
                                ColorSpaceType.RgbStudioG2084NoneP2020;
                            maximumLuminance = description1.MaxLuminance > 0
                                ? description1.MaxLuminance
                                : null;
                        }

                        return new DxgiAdapterMatch(
                            adapter,
                            adapter.Description1.Description.TrimEnd('\0', ' '),
                            advancedColor,
                            maximumLuminance);
                    }
                }
            }
            finally
            {
                if (!matched)
                {
                    adapter.Dispose();
                }
            }
        }

        throw new InvalidOperationException("No DXGI adapter owns the selected monitor.");
    }

    public void Dispose() => Adapter.Dispose();
}
