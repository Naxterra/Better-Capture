using Microsoft.Windows.ApplicationModel.Resources;

namespace NaxCapture.App.Services;

internal static class Localizer
{
    private static readonly Lazy<ResourceLoader> Loader = new(() => new ResourceLoader());

    internal static string Get(string key)
    {
        try
        {
            var value = Loader.Value.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
            return key;
        }
    }

    internal static string Format(string key, params object[] arguments) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), arguments);
}
