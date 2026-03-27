using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenNest.IO.Bom
{
    public class BomAnalysis
    {
        public List<MaterialGroup> Groups { get; set; } = new List<MaterialGroup>();
        public List<BomItem> Skipped { get; set; } = new List<BomItem>();
        public List<BomItem> Unmatched { get; set; } = new List<BomItem>();
    }

    public class MaterialGroup
    {
        public string Material { get; set; }
        public double Thickness { get; set; }
        public List<MatchedPart> Parts { get; set; } = new List<MatchedPart>();
    }

    public class MatchedPart
    {
        public BomItem Item { get; set; }
        public string DxfPath { get; set; }
    }

    public static class BomAnalyzer
    {
        public static BomAnalysis Analyze(List<BomItem> items, string dxfFolder)
        {
            var result = new BomAnalysis();

            // Build a case-insensitive lookup of DXF files in the folder (if it exists)
            var folderExists = Directory.Exists(dxfFolder);
            var dxfFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (folderExists)
            {
                foreach (var file in Directory.GetFiles(dxfFolder, "*.dxf"))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                    dxfFiles[nameWithoutExt] = file;
                }
            }

            // Partition items into: skipped, unmatched, or matched (grouped)
            var matched = new List<MatchedPart>();

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.FileName) || !item.Thickness.HasValue)
                {
                    result.Skipped.Add(item);
                    continue;
                }

                var lookupName = item.FileName;

                // Strip .dxf extension if the BOM includes it
                if (lookupName.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
                    lookupName = Path.GetFileNameWithoutExtension(lookupName);

                if (!folderExists)
                {
                    // No folder to search — group items without a DXF path
                    matched.Add(new MatchedPart { Item = item, DxfPath = null });
                }
                else if (dxfFiles.TryGetValue(lookupName, out var dxfPath))
                {
                    matched.Add(new MatchedPart { Item = item, DxfPath = dxfPath });
                }
                else
                {
                    result.Unmatched.Add(item);
                }
            }

            // Group matched parts by material + thickness
            var groups = matched
                .GroupBy(p => new
                {
                    Material = (p.Item.Material ?? "").ToUpperInvariant(),
                    Thickness = p.Item.Thickness.Value
                })
                .Select(g => new MaterialGroup
                {
                    Material = g.First().Item.Material ?? "",
                    Thickness = g.Key.Thickness,
                    Parts = g.ToList()
                })
                .OrderBy(g => g.Material)
                .ThenBy(g => g.Thickness)
                .ToList();

            result.Groups = groups;
            return result;
        }
    }
}
