using System.Runtime.CompilerServices;
using OpenNest.Properties;

namespace OpenNest.WinForms.Tests;

internal static class IsolatedUserSettings
{
#pragma warning disable CA2255 // Must run before anything reads Settings.Default.
    [ModuleInitializer]
    internal static void Redirect() =>
        AppDataSettingsProvider.PathOverride = Path.Combine(
            Path.GetTempPath(), "OpenNest.WinForms.Tests", Guid.NewGuid().ToString("N"), "user-settings.json");
#pragma warning restore CA2255
}
