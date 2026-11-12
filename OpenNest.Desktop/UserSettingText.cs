using System.Collections.Generic;
using System.Globalization;

namespace OpenNest.Desktop;

/// <summary>
/// Setting values in the text form the WinForms app stores in user-settings.json: what
/// System.Configuration's invariant type converters write and read ("True"/"False", a decimal
/// in "G" form, a double in round-trip "R" form). A value they cannot read stands for the
/// setting's default, as it does in WinForms.
/// </summary>
public static class UserSettingText
{
    /// <summary>The stored decimal under <paramref name="name"/>, or <paramref name="fallback"/>.</summary>
    public static decimal ReadDecimal(IReadOnlyDictionary<string, string> settings, string name, decimal fallback) =>
        settings.TryGetValue(name, out var text)
        && decimal.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>The stored double under <paramref name="name"/>, or <paramref name="fallback"/>.</summary>
    public static double ReadDouble(IReadOnlyDictionary<string, string> settings, string name, double fallback) =>
        settings.TryGetValue(name, out var text)
        && double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>The stored "True"/"False" under <paramref name="name"/>, or <paramref name="fallback"/>.</summary>
    public static bool ReadBool(IReadOnlyDictionary<string, string> settings, string name, bool fallback) =>
        settings.TryGetValue(name, out var text) && bool.TryParse(text?.Trim(), out var value)
            ? value
            : fallback;

    /// <summary>The stored text under <paramref name="name"/>, or <paramref name="fallback"/>.</summary>
    public static string ReadString(IReadOnlyDictionary<string, string> settings, string name, string fallback) =>
        settings.TryGetValue(name, out var text) && text is not null ? text : fallback;

    public static string Format(decimal value) => value.ToString("G", CultureInfo.InvariantCulture);

    public static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static string Format(bool value) => value ? "True" : "False";
}
