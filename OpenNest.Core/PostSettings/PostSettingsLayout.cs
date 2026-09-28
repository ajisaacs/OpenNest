using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace OpenNest.PostSettings
{
    public enum PostSettingKind
    {
        Text,
        Integer,
        Decimal,
        Boolean,
        Choice,
        StringMap,
    }

    public sealed class PostSettingsField
    {
        internal PostSettingsField(
            PropertyInfo property,
            PostSettingKind kind,
            PostSettingAttribute setting
        )
        {
            Property = property;
            Kind = kind;
            Label =
                property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                ?? property.Name;
            Description = property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";

            var (min, max) = DefaultRange(kind);
            Minimum = setting != null && !double.IsNaN(setting.Minimum) ? setting.Minimum : min;
            Maximum = setting != null && !double.IsNaN(setting.Maximum) ? setting.Maximum : max;
            DecimalPlaces =
                kind == PostSettingKind.Integer ? 0
                : setting != null && setting.DecimalPlaces >= 0 ? setting.DecimalPlaces
                : 3;
            KeyHeader = setting?.KeyHeader ?? "Name";
            ValueHeader = setting?.ValueHeader ?? "Value";
        }

        public PropertyInfo Property { get; }

        public string Name => Property.Name;

        public PostSettingKind Kind { get; }

        public string Label { get; }

        public string Description { get; }

        public double Minimum { get; }

        public double Maximum { get; }

        public int DecimalPlaces { get; }

        public string KeyHeader { get; }

        public string ValueHeader { get; }

        public string[] ChoiceNames =>
            Kind == PostSettingKind.Choice
                ? Enum.GetNames(Property.PropertyType)
                : Array.Empty<string>();

        public object GetValue(object config) => Property.GetValue(config);

        public void SetValue(object config, object value) => Property.SetValue(config, value);

        private static (double, double) DefaultRange(PostSettingKind kind) =>
            kind == PostSettingKind.Integer ? (int.MinValue, int.MaxValue) : (-1e9, 1e9);
    }

    public sealed class PostSettingsSection
    {
        internal PostSettingsSection(
            string name,
            string description,
            IReadOnlyList<PostSettingsField> fields
        )
        {
            Name = name;
            Description = description ?? "";
            Fields = fields;
        }

        public string Name { get; }

        public string Description { get; }

        public IReadOnlyList<PostSettingsField> Fields { get; }
    }

    /// <summary>
    /// Builds the sectioned settings layout for a post-processor config from its
    /// <see cref="PostSettingAttribute"/> metadata, without any UI dependency.
    /// </summary>
    public static class PostSettingsLayout
    {
        public const string OtherSection = "Other";

        /// <summary>
        /// Returns the ordered sections, or null when the config should use the
        /// generic PropertyGrid: it has no <see cref="PostSettingAttribute"/>,
        /// or one of its editable properties has a type the editor cannot show.
        /// Editable properties without the attribute go to the
        /// <see cref="OtherSection"/> so none become uneditable.
        /// </summary>
        public static IReadOnlyList<PostSettingsSection> TryBuild(Type configType)
        {
            if (configType == null)
                return null;

            var properties = configType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p =>
                    p.CanRead
                    && p.GetSetMethod() != null
                    && p.GetIndexParameters().Length == 0
                    && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false
                )
                .OrderBy(p => p.MetadataToken)
                .ToList();

            if (!properties.Any(p => p.GetCustomAttribute<PostSettingAttribute>() != null))
                return null;

            var entries = new List<(string Section, int Order, int Index, PostSettingsField Field)>();
            for (var i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                var kind = KindOf(property.PropertyType);
                if (kind == null)
                    return null;

                var setting = property.GetCustomAttribute<PostSettingAttribute>();
                var section = string.IsNullOrWhiteSpace(setting?.Section)
                    ? OtherSection
                    : setting.Section;
                entries.Add(
                    (
                        section,
                        setting?.Order ?? int.MaxValue,
                        i,
                        new PostSettingsField(property, kind.Value, setting)
                    )
                );
            }

            var declared = configType
                .GetCustomAttributes<PostSettingsSectionAttribute>()
                .GroupBy(a => a.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            return entries
                .GroupBy(e => e.Section, StringComparer.Ordinal)
                .Select(g => new
                {
                    Name = g.Key,
                    Declared = declared.TryGetValue(g.Key, out var d) ? d : null,
                    FirstIndex = g.Min(e => e.Index),
                    Fields = g.OrderBy(e => e.Order).ThenBy(e => e.Index).Select(e => e.Field).ToList(),
                })
                .OrderBy(s => s.Name == OtherSection && s.Declared == null ? 2
                    : s.Declared != null ? 0
                    : 1)
                .ThenBy(s => s.Declared?.Order ?? 0)
                .ThenBy(s => s.FirstIndex)
                .Select(s => new PostSettingsSection(s.Name, s.Declared?.Description, s.Fields))
                .ToList();
        }

        /// <summary>
        /// Builds a name/value map from edited table rows using the supplied key
        /// comparer. Keys and values are trimmed; fully blank rows are skipped.
        /// Throws <see cref="FormatException"/> naming the 1-based row for a
        /// missing or duplicate name.
        /// </summary>
        public static Dictionary<string, string> BuildMap(
            IEnumerable<KeyValuePair<string, string>> rows,
            IEqualityComparer<string> comparer
        )
        {
            var map = new Dictionary<string, string>(comparer ?? StringComparer.Ordinal);
            var row = 0;

            foreach (var entry in rows ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                row++;
                var key = entry.Key?.Trim() ?? "";
                var value = entry.Value?.Trim() ?? "";

                if (key.Length == 0 && value.Length == 0)
                    continue;
                if (key.Length == 0)
                    throw new FormatException($"Row {row}: a name is required.");
                if (map.ContainsKey(key))
                    throw new FormatException($"Row {row}: \"{key}\" is listed more than once.");

                map.Add(key, value);
            }

            return map;
        }

        private static PostSettingKind? KindOf(Type type)
        {
            if (type == typeof(string))
                return PostSettingKind.Text;
            if (type == typeof(int))
                return PostSettingKind.Integer;
            if (type == typeof(double))
                return PostSettingKind.Decimal;
            if (type == typeof(bool))
                return PostSettingKind.Boolean;
            if (type.IsEnum)
                return PostSettingKind.Choice;
            if (
                type == typeof(Dictionary<string, string>)
                || type == typeof(IDictionary<string, string>)
            )
                return PostSettingKind.StringMap;

            return null;
        }
    }
}
