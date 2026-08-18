using Microsoft.Windows.ApplicationModel.Resources;

namespace BetterCapture.App.Services;

internal static class Localizer
{
    private static readonly Lazy<ResourceManager> Manager = new(() => new ResourceManager());
    private static readonly Lazy<ResourceMap> Resources = new(() =>
        Manager.Value.MainResourceMap.TryGetSubtree("Resources") ?? Manager.Value.MainResourceMap);
    private static readonly Lazy<ResourceContext> Context = new(CreateContext);

    internal static System.Globalization.CultureInfo CurrentCulture => LanguagePreferenceService.Culture;

    internal static string Get(string key)
    {
        try
        {
            var candidate = Resources.Value.TryGetValue(key, Context.Value);
            var value = candidate?.ValueAsString;
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
            return key;
        }
    }

    internal static string Format(string key, params object[] arguments) =>
        string.Format(CurrentCulture, Get(key), arguments);

    private static ResourceContext CreateContext()
    {
        var context = Manager.Value.CreateResourceContext();
        context.QualifierValues[KnownResourceQualifierName.Language] = LanguagePreferenceService.CurrentLanguage;
        return context;
    }
}
