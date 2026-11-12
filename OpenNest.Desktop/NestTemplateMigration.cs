using System;
using System.Collections.Generic;
using System.IO;
using OpenNest.BestFit;
using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Desktop;

/// <summary>What <see cref="NestTemplateMigration"/> did.</summary>
/// <param name="ClearSetting">Clear the NestTemplatePath setting: the template was converted or
/// there was nothing to convert, so it is never consulted again.</param>
/// <param name="Warning">Why a conversion failed, to show the user; null when it did not.</param>
public sealed record NestTemplateMigrationResult(bool ClearSetting, string Warning);

/// <summary>
/// One-time upgrade of the legacy .nstdot nest template (the NestTemplatePath setting) into the
/// nest defaults file. A template is converted only when its file exists and no defaults file
/// does yet; a failed conversion keeps the setting so the user can still find the file.
/// </summary>
public static class NestTemplateMigration
{
    public const string SettingName = "NestTemplatePath";

    public static NestTemplateMigrationResult Run(string templatePath, string defaultsPath, IBestFitStore bestFitStore)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
            return new NestTemplateMigrationResult(false, null);

        if (File.Exists(templatePath) && !File.Exists(defaultsPath))
        {
            try
            {
                var nest = new NestReader(templatePath) { BestFitStore = bestFitStore }.Read();
                NestDefaults.FromNest(nest).Save(defaultsPath);
            }
            catch (Exception ex)
            {
                return new NestTemplateMigrationResult(false,
                    $"The nest template could not be converted to the new defaults file:\n{templatePath}\n\n{ex.Message}\n\nIt will no longer be loaded automatically. Set defaults under Tools > Nest Defaults.");
            }
        }

        return new NestTemplateMigrationResult(true, null);
    }

    /// <summary>
    /// Runs the migration on the template named in <paramref name="settings"/> and clears the
    /// setting (to the empty text WinForms stores) when it is done with.
    /// </summary>
    public static NestTemplateMigrationResult Run(IDictionary<string, string> settings, string defaultsPath, IBestFitStore bestFitStore)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.TryGetValue(SettingName, out var templatePath);
        var result = Run(templatePath, defaultsPath, bestFitStore);
        if (result.ClearSetting)
            settings[SettingName] = "";
        return result;
    }
}
