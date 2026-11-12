using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Data;

namespace OpenNest.Desktop;

/// <summary>
/// The cut-time settings last used in Calculate Cut Time or the simulator, under the names and
/// types the WinForms app stores: LastFeedrate, LastHoleFeedrate, LastRapidFeedrate and
/// LastPierceTime (seconds) as decimals, CutTimeSkipEtch as "True"/"False" and CutTimeMachine
/// as a machine id, "none" for full speed or empty until a machine is chosen.
/// </summary>
public sealed record SavedCutTimeSettings(
    decimal Feedrate,
    decimal HoleFeedrate,
    decimal RapidFeedrate,
    decimal PierceSeconds,
    bool SkipEtch,
    string Machine)
{
    public const string FeedrateName = "LastFeedrate";
    public const string HoleFeedrateName = "LastHoleFeedrate";
    public const string RapidFeedrateName = "LastRapidFeedrate";
    public const string PierceTimeName = "LastPierceTime";
    public const string SkipEtchName = "CutTimeSkipEtch";
    public const string MachineName = "CutTimeMachine";

    /// <summary>The saved machine choice that times every move at full speed.</summary>
    public const string FullSpeed = "none";

    /// <summary>The WinForms setting defaults, what a user who never saved has.</summary>
    public static SavedCutTimeSettings Unsaved { get; } = new(1, 0, 1, 0, true, "");

    /// <summary>
    /// Feed 1, rapid 1 and no pierce time are the never-saved defaults; estimating with them
    /// would crawl.
    /// </summary>
    public bool IsUnset => Feedrate == 1 && RapidFeedrate == 1 && PierceSeconds == 0;

    /// <summary>The saved values; a missing or unreadable one is its WinForms default.</summary>
    public static SavedCutTimeSettings Read(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var unsaved = Unsaved;
        return new SavedCutTimeSettings(
            UserSettingText.ReadDecimal(settings, FeedrateName, unsaved.Feedrate),
            UserSettingText.ReadDecimal(settings, HoleFeedrateName, unsaved.HoleFeedrate),
            UserSettingText.ReadDecimal(settings, RapidFeedrateName, unsaved.RapidFeedrate),
            UserSettingText.ReadDecimal(settings, PierceTimeName, unsaved.PierceSeconds),
            UserSettingText.ReadBool(settings, SkipEtchName, unsaved.SkipEtch),
            UserSettingText.ReadString(settings, MachineName, unsaved.Machine));
    }

    /// <summary>Stores every value in the WinForms format.</summary>
    public void WriteTo(IDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings[FeedrateName] = UserSettingText.Format(Feedrate);
        settings[HoleFeedrateName] = UserSettingText.Format(HoleFeedrate);
        settings[RapidFeedrateName] = UserSettingText.Format(RapidFeedrate);
        settings[PierceTimeName] = UserSettingText.Format(PierceSeconds);
        settings[SkipEtchName] = UserSettingText.Format(SkipEtch);
        settings[MachineName] = Machine ?? "";
    }

    /// <summary>
    /// The saved feed, hole feed, rapid, pierce and etch choice in <paramref name="units"/>, with
    /// <paramref name="machine"/>'s acceleration (null for full speed). Never-saved settings fall
    /// back to <see cref="CutParameters.Default"/>; no hole feed uses the feed.
    /// </summary>
    public CutParameters ToCutParameters(Units units, MachineConfig machine)
    {
        var unset = IsUnset;
        var defaults = CutParameters.Default;
        var feed = unset ? defaults.Feedrate : (double)Feedrate;

        return new CutParameters
        {
            Feedrate = feed,
            HoleFeedrate = HoleFeedrate > 0 ? (double)HoleFeedrate : feed,
            RapidTravelRate = unset ? defaults.RapidTravelRate : (double)RapidFeedrate,
            PierceTime = unset ? defaults.PierceTime : TimeSpan.FromSeconds((double)PierceSeconds),
            SkipScribe = SkipEtch,
            Units = units,
            Motion = machine?.GetMotionOrPlaceholder().ToProfile(machine.Units, units),
        };
    }

    /// <summary>These settings with the feeds, pierce and etch choice of <paramref name="parameters"/>.</summary>
    public SavedCutTimeSettings With(CutParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return this with
        {
            Feedrate = (decimal)parameters.Feedrate,
            HoleFeedrate = (decimal)(parameters.HoleFeedrate ?? 0),
            RapidFeedrate = (decimal)parameters.RapidTravelRate,
            PierceSeconds = (decimal)parameters.PierceTime.TotalSeconds,
            SkipEtch = parameters.SkipScribe,
        };
    }

    /// <summary>The machines in the order the cut-time machine list shows them.</summary>
    public static List<MachineConfig> SortedByName(IEnumerable<MachineConfig> machines) =>
        machines.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// The machine <paramref name="saved"/> names, the first by name until a choice is saved
    /// (or when the saved one is gone), or null for <see cref="FullSpeed"/>.
    /// </summary>
    public static MachineConfig ChooseMachine(IEnumerable<MachineConfig> machines, string saved)
    {
        ArgumentNullException.ThrowIfNull(machines);
        var sorted = SortedByName(machines);
        return saved == FullSpeed
            ? null
            : sorted.FirstOrDefault(m => m.Id.ToString() == saved) ?? sorted.FirstOrDefault();
    }

    /// <summary>The <see cref="Machine"/> value that saves <paramref name="machine"/> (null for full speed).</summary>
    public static string MachineChoice(MachineConfig machine) => machine?.Id.ToString() ?? FullSpeed;
}
