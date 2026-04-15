using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest
{
    public static class PostProcessorMaterials
    {
        private static readonly List<string> materials = new();

        public static IReadOnlyList<string> Names => materials;

        public static void AddFrom(IMaterialProvidingPostProcessor provider)
        {
            if (provider == null)
                return;

            foreach (var name in provider.GetMaterialNames())
            {
                if (!string.IsNullOrWhiteSpace(name)
                    && !materials.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    materials.Add(name);
                }
            }

            materials.Sort(StringComparer.OrdinalIgnoreCase);
        }
    }
}
