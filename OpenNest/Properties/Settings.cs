using System.Configuration;

namespace OpenNest.Properties
{
    // Keeps user settings outside the Velopack install folder; see AppDataSettingsProvider.
    [SettingsProvider(typeof(AppDataSettingsProvider))]
    internal sealed partial class Settings
    {
    }
}
