using System;

namespace OpenNest.PostSettings
{
    /// <summary>
    /// Places a post-processor config property in a section of the desktop
    /// settings editor. A config type opts into the sectioned editor by marking
    /// at least one property; unmarked configs keep the generic PropertyGrid.
    /// Label and help text come from <c>DisplayName</c> and <c>Description</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class PostSettingAttribute : Attribute
    {
        public PostSettingAttribute(string section, int order = 0)
        {
            Section = section;
            Order = order;
        }

        public string Section { get; }

        public int Order { get; }

        /// <summary>Lower bound for numeric fields; NaN uses the kind's default.</summary>
        public double Minimum { get; set; } = double.NaN;

        /// <summary>Upper bound for numeric fields; NaN uses the kind's default.</summary>
        public double Maximum { get; set; } = double.NaN;

        /// <summary>Decimal places for decimal fields; negative uses the default.</summary>
        public int DecimalPlaces { get; set; } = -1;

        /// <summary>Column header for the key of a name/value table.</summary>
        public string KeyHeader { get; set; }

        /// <summary>Column header for the value of a name/value table.</summary>
        public string ValueHeader { get; set; }
    }

    /// <summary>Declares a settings section's order and description.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class PostSettingsSectionAttribute : Attribute
    {
        public PostSettingsSectionAttribute(string name, int order)
        {
            Name = name;
            Order = order;
        }

        public string Name { get; }

        public int Order { get; }

        public string Description { get; set; }
    }
}
