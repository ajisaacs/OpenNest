using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Data;
using OpenNest.Engine;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;

namespace OpenNest.Desktop;

/// <summary>
/// The values Tools &gt; Options saves with its Save button, under the WinForms user-setting
/// names both desktop apps share in user-settings.json. A missing or unreadable value is its
/// WinForms default. The update feed URL and the nest server URL have their own buttons
/// (<see cref="UpdateFeedOption"/>, <see cref="NestServerUrlOption"/>), so Save leaves them alone.
/// </summary>
public sealed record DesktopOptions
{
    public const string CreateNewNestOnOpenName = "CreateNewNestOnOpen";
    public const string AutoSizePlateFactorName = "AutoSizePlateFactor";
    public const string CuttingPlanEffortName = "CuttingPlanEffort";
    public const string ClonePlacementOrderName = "ClonePlacementOrder";
    public const string DisabledStrategiesName = "DisabledStrategies";
    public const string UpdateIncludeDevBuildsName = "UpdateIncludeDevBuilds";

    /// <summary>The auto-size factor's range and step, as the WinForms box allows.</summary>
    public const double AutoSizePlateFactorMinimum = 0;
    public const double AutoSizePlateFactorMaximum = 100;
    public const double AutoSizePlateFactorIncrement = 0.125;
    public const int AutoSizePlateFactorDecimals = 4;

    /// <summary>Cut planning effort's step; its range is Plan Cutting's budget multiplier range.</summary>
    public const double CuttingPlanEffortIncrement = 0.1;

    public static double CuttingPlanEffortMinimum => CuttingPlanBatch.MinimumBudgetMultiplier;
    public static double CuttingPlanEffortMaximum => CuttingPlanBatch.MaximumBudgetMultiplier;

    /// <summary>The WinForms setting defaults.</summary>
    public static DesktopOptions Defaults { get; } = new();

    /// <summary>Create a new nest when the app starts.</summary>
    public bool CreateNewNestOnOpen { get; init; } = true;

    /// <summary>The amount Resize Plate rounds the plate size up to.</summary>
    public double AutoSizePlateFactor { get; init; } = 0.5;

    /// <summary>Plan Cutting's search budget multiplier (1 is normal).</summary>
    public double CuttingPlanEffort { get; init; } = CuttingPlanBatch.DefaultBudgetMultiplier;

    /// <summary>The colour scheme's name, as <see cref="ColorSchemeRegistry.SettingName"/> stores it.</summary>
    public string ColorScheme { get; init; } = ColorSchemeRegistry.DefaultSchemeName;

    /// <summary>Which way Shift+click and Ctrl+click clones push the copy.</summary>
    public SettleOrder ClonePlacementOrder { get; init; } = SettleOrder.ClosestToCorner;

    /// <summary>Fill strategies left out of the pipeline, stored comma-separated by name.</summary>
    public IReadOnlyCollection<string> DisabledStrategies { get; init; } = Array.Empty<string>();

    /// <summary>Check the rolling development-build release as well as stable releases.</summary>
    public bool UpdateIncludeDevBuilds { get; init; }

    /// <summary>The saved values; one missing or unreadable is its WinForms default.</summary>
    public static DesktopOptions Read(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new DesktopOptions
        {
            CreateNewNestOnOpen = UserSettingText.ReadBool(settings, CreateNewNestOnOpenName, Defaults.CreateNewNestOnOpen),
            AutoSizePlateFactor = UserSettingText.ReadDouble(settings, AutoSizePlateFactorName, Defaults.AutoSizePlateFactor),
            CuttingPlanEffort = UserSettingText.ReadDouble(settings, CuttingPlanEffortName, Defaults.CuttingPlanEffort),
            ColorScheme = ColorSchemeRegistry.SavedSchemeName(settings),
            ClonePlacementOrder = ClonePlacement.Parse(
                UserSettingText.ReadString(settings, ClonePlacementOrderName, null)),
            DisabledStrategies = FillStrategyOptions.ParseDisabled(
                UserSettingText.ReadString(settings, DisabledStrategiesName, "")),
            UpdateIncludeDevBuilds = UserSettingText.ReadBool(settings, UpdateIncludeDevBuildsName, Defaults.UpdateIncludeDevBuilds),
        };
    }

    /// <summary>
    /// Stores the values Save writes, in the WinForms text form; other settings are kept, and
    /// the cut planning effort is stored as entered (Plan Cutting clamps it when it reads it).
    /// </summary>
    public void WriteTo(IDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings[CreateNewNestOnOpenName] = UserSettingText.Format(CreateNewNestOnOpen);
        settings[UpdateIncludeDevBuildsName] = UserSettingText.Format(UpdateIncludeDevBuilds);
        settings[AutoSizePlateFactorName] = UserSettingText.Format(AutoSizePlateFactor);
        settings[CuttingPlanEffortName] = UserSettingText.Format(CuttingPlanEffort);
        settings[ColorSchemeRegistry.SettingName] = string.IsNullOrEmpty(ColorScheme)
            ? ColorSchemeRegistry.DefaultSchemeName
            : ColorScheme;
        settings[ClonePlacementOrderName] = ClonePlacementOrder.ToString();
        settings[DisabledStrategiesName] = FillStrategyOptions.FormatDisabled(DisabledStrategies);
    }

    /// <summary>The colour schemes to choose from: the built-ins, then those in Schemes.</summary>
    public static IReadOnlyList<string> ColorSchemeNames =>
        ColorSchemeRegistry.AllSchemes.Select(s => s.Name).ToList();

    /// <summary>
    /// The index of the saved scheme in <paramref name="names"/> (exact name), or the first
    /// scheme when it is no longer there, as the WinForms combo selects it.
    /// </summary>
    public int ColorSchemeIndex(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == ColorScheme)
                return i;
        }
        return 0;
    }

    /// <summary>The auto-size factor as the Options box shows it: within its range.</summary>
    public double AutoSizePlateFactorForEditing =>
        double.IsFinite(AutoSizePlateFactor)
            ? System.Math.Clamp(AutoSizePlateFactor, AutoSizePlateFactorMinimum, AutoSizePlateFactorMaximum)
            : Defaults.AutoSizePlateFactor;

    /// <summary>The cut planning effort as the Options box shows it: the value Plan Cutting uses.</summary>
    public double CuttingPlanEffortForEditing => CuttingPlanBatch.ClampBudgetMultiplier(CuttingPlanEffort);

    /// <summary>
    /// Applies what takes effect without a restart: the fill strategy enablement and the colour
    /// scheme (whose <see cref="ColorSchemeRegistry.Applied"/> lets each front end recolour and
    /// repaint). Run after Save and, from the saved settings, at startup.
    /// </summary>
    public void Apply()
    {
        FillStrategyOptions.Apply(DisabledStrategies);
        ColorSchemeRegistry.Apply(ColorSchemeRegistry.Get(ColorScheme));
    }
}

/// <summary>The Options choices for Shift+click and Ctrl+click clone placement, in combo order.</summary>
public static class ClonePlacement
{
    public static IReadOnlyList<(SettleOrder Order, string Text)> Choices { get; } = new[]
    {
        (SettleOrder.ClosestToCorner, "Closest to origin"),
        (SettleOrder.HorizontalFirst, "Push horizontally first"),
        (SettleOrder.VerticalFirst, "Push vertically first"),
    };

    /// <summary>The stored enum name's order; an unknown name is the closest-to-origin search.</summary>
    public static SettleOrder Parse(string stored) =>
        Enum.TryParse<SettleOrder>(stored, out var order) && Enum.IsDefined(order)
            ? order
            : SettleOrder.ClosestToCorner;

    /// <summary>The combo index of <paramref name="order"/>, the first for one not listed.</summary>
    public static int IndexOf(SettleOrder order)
    {
        for (var i = 0; i < Choices.Count; i++)
        {
            if (Choices[i].Order == order)
                return i;
        }
        return 0;
    }

    /// <summary>The order at a combo index; no selection is the first choice.</summary>
    public static SettleOrder At(int index) => Choices[System.Math.Clamp(index, 0, Choices.Count - 1)].Order;
}

/// <summary>A fill strategy as the Options grid lists it.</summary>
public sealed record FillStrategyRow(string Name, NestPhase Phase, int Order);

/// <summary>
/// The Options fill strategy grid and the DisabledStrategies setting: names separated by commas,
/// matched without regard to case, applied to <see cref="FillStrategyRegistry"/>.
/// </summary>
public static class FillStrategyOptions
{
    /// <summary>Every built-in strategy, in pipeline order.</summary>
    public static IReadOnlyList<FillStrategyRow> Rows =>
        FillStrategyRegistry.AllStrategies.Select(s => new FillStrategyRow(s.Name, s.Phase, s.Order)).ToList();

    /// <summary>The names in a stored value; blank entries are skipped.</summary>
    public static HashSet<string> ParseDisabled(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The stored form of <paramref name="names"/>.</summary>
    public static string FormatDisabled(IEnumerable<string> names) =>
        names == null ? "" : string.Join(",", names);

    /// <summary>Re-enables every strategy, then disables <paramref name="disabled"/>.</summary>
    public static void Apply(IEnumerable<string> disabled)
    {
        FillStrategyRegistry.Enable(FillStrategyRegistry.AllStrategies.Select(s => s.Name).ToArray());
        var names = disabled?.ToArray() ?? Array.Empty<string>();
        if (names.Length > 0)
            FillStrategyRegistry.Disable(names);
    }

    /// <summary>Applies the DisabledStrategies value stored in <paramref name="settings"/>.</summary>
    public static void ApplySaved(IReadOnlyDictionary<string, string> settings) =>
        Apply(ParseDisabled(UserSettingText.ReadString(settings, DesktopOptions.DisabledStrategiesName, "")));
}

public enum OptionNoticeKind
{
    Information,
    Warning,
    Error,
}

/// <summary>What to tell the user after an Options button, and how.</summary>
public sealed record OptionNotice(string Title, string Text, OptionNoticeKind Kind);

/// <summary>
/// Tools &gt; Options &gt; Set / Change Server: the nest server URL in storage.json, keeping the
/// storage mode (File / Database is chosen under File &gt; Storage Mode).
/// </summary>
public static class NestServerUrlOption
{
    public const string Title = "Server URL";

    /// <summary>
    /// Validates <paramref name="text"/> and passes the normalized URL to <paramref name="save"/>,
    /// which writes storage.json; returns what to tell the user and the URL saved (null when not).
    /// </summary>
    public static (OptionNotice Notice, string SavedUrl) Save(string text, Action<string> save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (!NestStorageSettings.TryNormalizeServerUrl(text, out var url))
            return (new OptionNotice(Title, "Enter a valid http or https server URL.", OptionNoticeKind.Warning), null);

        try
        {
            save(url);
            return (new OptionNotice(Title, "Server URL saved. Storage mode has not changed.",
                OptionNoticeKind.Information), url);
        }
        catch (Exception ex)
        {
            return (new OptionNotice(Title, $"Could not save the server URL: {ex.Message}", OptionNoticeKind.Error), null);
        }
    }

    /// <summary>Writes <paramref name="serverUrl"/> into the storage.json at <paramref name="path"/>, keeping its mode.</summary>
    public static NestStorageSettings SaveToFile(string path, string serverUrl)
    {
        var next = new NestStorageSettings
        {
            Mode = NestStorageSettings.Load(path).Mode,
            ServerUrl = serverUrl,
        };
        next.Save(path);
        return NestStorageSettings.Load(path);
    }
}

/// <summary>
/// Tools &gt; Options &gt; Save Feed URL: the UpdateFeedUrl user setting. Blank means the default
/// source: the nest server's mirror in Database mode (GitHub Releases if it has none), otherwise
/// GitHub Releases. A change takes effect after a restart.
/// </summary>
public static class UpdateFeedOption
{
    public const string SettingName = "UpdateFeedUrl";
    public const string Title = "Update Feed URL";

    /// <summary>The saved feed URL; blank (the default) when none.</summary>
    public static string Read(IReadOnlyDictionary<string, string> settings) =>
        UserSettingText.ReadString(settings, SettingName, "");

    /// <summary>
    /// Validates <paramref name="text"/> (blank clears it) and passes the normalized URL to
    /// <paramref name="save"/>; returns what to tell the user and the URL saved (null when not).
    /// </summary>
    public static (OptionNotice Notice, string SavedUrl) Save(string text, Action<string> save)
    {
        ArgumentNullException.ThrowIfNull(save);
        text = text?.Trim() ?? "";
        string url;
        string saved;
        if (text.Length == 0)
        {
            url = "";
            saved = "Update feed URL cleared. After a restart, updates come from "
                + "the nest server's mirror in Database mode (GitHub Releases if it has none), "
                + "otherwise from GitHub Releases.";
        }
        else if (NestStorageSettings.TryNormalizeServerUrl(text, out url))
        {
            saved = "Update feed URL saved. Restart OpenNest for the new feed to take effect.";
        }
        else
        {
            return (new OptionNotice(Title, "Enter a valid http or https feed URL, or clear the field to "
                + "use the default source: the nest server's mirror in Database mode, "
                + "otherwise GitHub Releases.", OptionNoticeKind.Warning), null);
        }

        try
        {
            save(url);
            return (new OptionNotice(Title, saved, OptionNoticeKind.Information), url);
        }
        catch (Exception ex)
        {
            return (new OptionNotice(Title, $"Could not save the update feed URL: {ex.Message}", OptionNoticeKind.Error), null);
        }
    }
}
