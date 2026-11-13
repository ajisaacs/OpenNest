using System;
using System.Collections.Generic;
using OpenNest.CutOffs;

namespace OpenNest.Desktop;

/// <summary>
/// Whether new cut-offs lock the head height and overcut the far sheet edge, kept between
/// sessions under the WinForms names CutOffLockHeadHeight ("True"/"False") and
/// CutOffHeadLockOvercutInches (a double, always inches) and converted to each nest's units.
/// </summary>
public sealed record SavedCutOffHeadLock(bool LockHeadHeight, double OvercutInches)
{
    public const string LockHeadHeightName = "CutOffLockHeadHeight";
    public const string OvercutInchesName = "CutOffHeadLockOvercutInches";

    public const double DefaultOvercutInches = 1.5;
    private const double MillimetersPerInch = 25.4;

    /// <summary>The WinForms setting defaults: unlocked, 1.5 in overcut.</summary>
    public static SavedCutOffHeadLock Unsaved { get; } = new(false, DefaultOvercutInches);

    /// <summary>The saved values; a missing or unreadable one is its WinForms default.</summary>
    public static SavedCutOffHeadLock Read(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SavedCutOffHeadLock(
            UserSettingText.ReadBool(settings, LockHeadHeightName, Unsaved.LockHeadHeight),
            UserSettingText.ReadDouble(settings, OvercutInchesName, Unsaved.OvercutInches));
    }

    /// <summary>Stores both values in the WinForms format.</summary>
    public void WriteTo(IDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings[LockHeadHeightName] = UserSettingText.Format(LockHeadHeight);
        settings[OvercutInchesName] = UserSettingText.Format(OvercutInches);
    }

    /// <summary>
    /// Sets the new cut-off lock and overcut of <paramref name="settings"/> for a nest in
    /// <paramref name="units"/>; a saved overcut that is not a positive number is 1.5 in.
    /// </summary>
    public void ApplyTo(CutOffSettings settings, Units units)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var inches = OvercutInches;
        if (!double.IsFinite(inches) || inches <= 0)
            inches = DefaultOvercutInches;
        settings.LockHeadHeight = LockHeadHeight;
        settings.HeadLockOvercut = units == Units.Millimeters ? inches * MillimetersPerInch : inches;
    }

    /// <summary>What to save for a cut-off choice made in a nest in <paramref name="units"/>.</summary>
    public static SavedCutOffHeadLock From(bool lockHeadHeight, double overcut, Units units) =>
        new(lockHeadHeight, units == Units.Millimeters ? overcut / MillimetersPerInch : overcut);
}
