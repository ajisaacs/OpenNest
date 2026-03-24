using System;
using System.Drawing;
using System.IO;
using System.Linq;
using OpenNest;
using OpenNest.Converters;
using OpenNest.Geometry;
using Size = OpenNest.Geometry.Size;
using OpenNest.IO;

var partColors = new Color[]
{
    Color.FromArgb(205, 92, 92),    // Indian Red
    Color.FromArgb(148, 103, 189),  // Medium Purple
    Color.FromArgb(75, 180, 175),   // Teal
    Color.FromArgb(210, 190, 75),   // Goldenrod
    Color.FromArgb(190, 85, 175),   // Orchid
    Color.FromArgb(185, 115, 85),   // Sienna
    Color.FromArgb(120, 100, 190),  // Slate Blue
    Color.FromArgb(200, 100, 140),  // Rose
    Color.FromArgb(80, 175, 155),   // Sea Green
    Color.FromArgb(195, 160, 85),   // Dark Khaki
    Color.FromArgb(175, 95, 160),   // Plum
    Color.FromArgb(215, 130, 130),  // Light Coral
};

var templateDir = @"C:\Users\AJ\Desktop\Projects\OpenNest\docs\Templates";
var outputDir = @"C:\Users\AJ\Desktop\Projects\OpenNest\docs\Templates\Nests";
Directory.CreateDirectory(outputDir);

// BOM: (fileName, qty, thickness, material)
var bom = new (string File, int Qty, double Thickness, string Material)[]
{
    ("PT01", 2, 0.250, "304SS"),
    ("PT02", 5, 0.625, "304SS"),
    ("PT03", 4, 0.250, "304SS"),
    ("PT04", 4, 0.250, "304SS"),
    ("PT05", 1, 0.250, "304SS"),
    ("PT06", 21, 0.375, "304SS"),
    ("PT07", 2, 0.250, "304SS"),
    ("PT08", 22, 0.250, "304SS"),
    ("PT11", 2, 0.1875, "304SS"),
    ("PT12", 2, 0.1875, "304SS"),
    ("PT13", 6, 0.1875, "304SS"),
    ("PT15", 6, 0.1875, "304SS"),
    ("PT16", 6, 0.1875, "304SS"),
    ("PT18", 3, 0.250, "304SS"),
    ("PT19", 3, 0.1875, "304SS"),
    ("PT20", 2, 0.1196, "304SS"),
    ("PT21", 6, 0.1196, "304SS"),
    ("PT22", 2, 0.1196, "304SS"),
    ("PT23", 1, 0.0598, "304SS"),
    ("PT24", 1, 0.0598, "304SS"),
    ("PT26", 4, 0.250, "304SS"),
    ("PT27", 2, 0.250, "304SS"),
    ("PT28", 4, 0.250, "304SS"),
    ("PT29", 6, 0.250, "304SS"),
    ("PT33", 2, 0.250, "304SS"),
    ("PT34", 4, 0.250, "304SS"),
    ("PT35", 3, 0.1875, "304SS"),
    ("PT36", 4, 0.1875, "304SS"),
    ("PT37", 4, 0.1875, "304SS"),
    ("PT38", 4, 0.1875, "304SS"),
    ("PT39", 2, 0.0598, "304SS"),
    ("PT40", 4, 0.0598, "304SS"),
    ("PT41", 1, 0.1875, "304SS"),
    ("PT43", 1, 0.0598, "304SS"),
    ("PT44", 1, 0.0598, "304SS"),
    ("PT45", 1, 0.250, "304SS"),
    ("PT46", 2, 0.250, "304SS"),
    ("PT47", 4, 0.250, "304SS"),
    ("PT48", 1, 0.250, "304SS"),
    ("PT49", 1, 0.750, "PCS"),
    ("PT50", 2, 0.375, "PCS"),
    ("PT51", 1, 0.250, "304SS"),
    ("PT52", 1, 0.1875, "304SS"),
    ("PT53", 1, 0.1875, "304SS"),
    ("PT54", 2, 0.250, "304SS"),
    ("PT55", 1, 0.1196, "304SS"),
    ("PT56", 1, 0.1196, "304SS"),
    ("PT57", 1, 0.0598, "304SS"),
    ("PT58", 1, 0.0598, "304SS"),
};

// Group by material + thickness
var groups = bom.GroupBy(b => (b.Material, b.Thickness)).OrderBy(g => g.Key.Material).ThenBy(g => g.Key.Thickness);

foreach (var group in groups)
{
    var material = group.Key.Material;
    var thickness = group.Key.Thickness;
    var thicknessLabel = thickness switch
    {
        0.0598 => "16GA",
        0.1196 => "11GA",
        0.1875 => "3-16",
        0.250 => "1-4",
        0.375 => "3-8",
        0.625 => "5-8",
        0.750 => "3-4",
        _ => thickness.ToString("F4")
    };

    var nestName = $"4526 A14 - {material} {thicknessLabel}";
    Console.WriteLine($"\n=== {nestName} ===");

    var nest = new Nest();
    nest.Name = nestName;
    nest.PlateDefaults.Thickness = thickness;
    nest.PlateDefaults.Material = new Material { Name = material };
    nest.PlateDefaults.PartSpacing = 0.125;
    nest.PlateDefaults.EdgeSpacing = new Spacing(0.25, 0.25, 0.25, 0.25);

    // Import DXFs for this group
    var importer = new DxfImporter();
    var colorIndex = 0;
    double maxMinDim = 0;
    double maxMaxDim = 0;

    foreach (var item in group)
    {
        var dxfPath = Path.Combine(templateDir, $"4526 A14 {item.File}.dxf");
        if (!File.Exists(dxfPath))
        {
            Console.WriteLine($"  WARNING: {dxfPath} not found, skipping");
            continue;
        }

        if (!importer.GetGeometry(dxfPath, out var geometry) || geometry.Count == 0)
        {
            Console.WriteLine($"  WARNING: no geometry in {item.File}, skipping");
            continue;
        }

        var pgm = ConvertGeometry.ToProgram(geometry);
        if (pgm == null)
        {
            Console.WriteLine($"  WARNING: failed to convert {item.File}, skipping");
            continue;
        }

        var drawing = new Drawing(item.File, pgm);
        drawing.Quantity.Required = item.Qty;
        drawing.Material = new Material { Name = material };
        drawing.Color = partColors[colorIndex % partColors.Length];
        colorIndex++;
        nest.Drawings.Add(drawing);

        var bbox = pgm.BoundingBox();
        var minDim = System.Math.Min(bbox.Width, bbox.Length);
        var maxDim = System.Math.Max(bbox.Width, bbox.Length);
        maxMinDim = System.Math.Max(maxMinDim, minDim);
        maxMaxDim = System.Math.Max(maxMaxDim, maxDim);

        Console.WriteLine($"  {item.File}: {bbox.Width:F2} x {bbox.Length:F2}, qty={item.Qty}");
    }

    // Choose plate size based on largest part dimensions
    // Size(width, length) — width is the short side, length is the long side
    // Standard sizes: 48x96, 48x120, 60x120, 60x144, 72x144, 96x120
    double plateW, plateL;
    if (maxMinDim <= 47.5 && maxMaxDim <= 95.5)
    {
        plateW = 48; plateL = 96;
    }
    else if (maxMinDim <= 47.5 && maxMaxDim <= 119.5)
    {
        plateW = 48; plateL = 120;
    }
    else if (maxMinDim <= 59.5 && maxMaxDim <= 119.5)
    {
        plateW = 60; plateL = 120;
    }
    else if (maxMinDim <= 59.5 && maxMaxDim <= 143.5)
    {
        plateW = 60; plateL = 144;
    }
    else if (maxMinDim <= 71.5 && maxMaxDim <= 143.5)
    {
        plateW = 72; plateL = 144;
    }
    else if (maxMinDim <= 95.5 && maxMaxDim <= 119.5)
    {
        plateW = 96; plateL = 120;
    }
    else
    {
        // Fallback: round up to nearest 12"
        plateW = System.Math.Ceiling((maxMinDim + 1) / 12.0) * 12;
        plateL = System.Math.Ceiling((maxMaxDim + 1) / 12.0) * 12;
    }

    // Create one empty plate via PlateDefaults so it inherits settings
    nest.PlateDefaults.Size = new Size(plateW, plateL);
    var plate = nest.CreatePlate();
    plate.Quantity = 1;

    Console.WriteLine($"  Plate size: {plateW} x {plateL} (W x L)");
    Console.WriteLine($"  Drawings: {nest.Drawings.Count}");

    var outputPath = Path.Combine(outputDir, $"{nestName}.nest");
    var writer = new NestWriter(nest);
    if (writer.Write(outputPath))
        Console.WriteLine($"  Saved: {outputPath}");
    else
        Console.WriteLine($"  ERROR: failed to save {outputPath}");
}

Console.WriteLine("\nDone!");
