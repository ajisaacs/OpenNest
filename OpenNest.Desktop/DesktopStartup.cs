using OpenNest.BestFit;
using OpenNest.Data;

namespace OpenNest.Desktop;

/// <summary>What the desktop app's start-up steps found.</summary>
/// <param name="TemplateWarning">Why the legacy nest template could not be converted, to show
/// the user; null when there was nothing to report.</param>
/// <param name="ClearTemplateSetting">The template was converted or is gone: clear its setting
/// (<see cref="DesktopStartup.ClearTemplateSetting"/>) so it is never consulted again.</param>
/// <param name="CreateNewNest">Open a new nest, as Tools > Options > Create a new nest on
/// startup asks; the window adds none beside nests already open.</param>
public sealed record DesktopStartupResult(
    string TemplateWarning, bool ClearTemplateSetting, bool CreateNewNest);

/// <summary>
/// The desktop app's start-up steps, in the WinForms main window's order: first the one-time
/// conversion of the legacy nest template into the nest defaults file (a new nest reads that
/// file), then whether to open a new nest.
/// </summary>
public static class DesktopStartup
{
    /// <summary>
    /// Converts the legacy nest template and decides on the new nest. Reads the user settings
    /// but never writes them, so it can run off the UI thread while the window's own settings
    /// writers run on it; the caller clears the setting on the UI thread when asked to.
    /// </summary>
    /// <param name="settingsPath">The shared user settings file.</param>
    /// <param name="defaultsPath">The nest defaults file the template converts into.</param>
    /// <param name="bestFitStore">Best-fit cache the template's drawings read from.</param>
    public static DesktopStartupResult Run(
        string settingsPath, string defaultsPath, IBestFitStore bestFitStore)
    {
        var settings = UserSettingsFile.Read(settingsPath);
        settings.TryGetValue(NestTemplateMigration.SettingName, out var templatePath);
        var migration = NestTemplateMigration.Run(templatePath, defaultsPath, bestFitStore);
        return new DesktopStartupResult(migration.Warning, migration.ClearSetting,
            DesktopOptions.Read(settings).CreateNewNestOnOpen);
    }

    /// <summary>
    /// Clears the template setting to the empty text WinForms stores, keeping every other
    /// setting. Returns false when the file could not be written; the next start then finds
    /// the defaults file already there and clears it again.
    /// </summary>
    public static bool ClearTemplateSetting(string settingsPath) =>
        UserSettingsFile.Update(settingsPath, s => s[NestTemplateMigration.SettingName] = "");
}
