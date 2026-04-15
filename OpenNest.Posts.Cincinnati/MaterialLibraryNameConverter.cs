using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace OpenNest.Posts.Cincinnati
{
    public sealed class MaterialLibraryNameConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            var config = context?.Instance as CincinnatiPostConfig;
            var names = new List<string> { "" };

            if (config?.MaterialLibraries != null)
            {
                names.AddRange(config.MaterialLibraries
                    .Select(e => e.Library)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            }

            return new StandardValuesCollection(names);
        }
    }
}
