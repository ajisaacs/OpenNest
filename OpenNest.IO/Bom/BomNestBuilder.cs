using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.IO.Bom;

/// <summary>Plate size and spacing for one group's nest.</summary>
public sealed class BomGroupPlateSettings
{
    public double PlateWidth { get; set; }

    public double PlateLength { get; set; }

    public double PartSpacing { get; set; }

    public double EdgeLeft { get; set; }

    public double EdgeBottom { get; set; }

    public double EdgeRight { get; set; }

    public double EdgeTop { get; set; }
}

/// <summary>
/// The nest built for one group, or none when no drawing imported, plus a
/// message for every drawing that could not be imported.
/// </summary>
public sealed class BomNestBuildResult
{
    public BomNestBuildResult(Nest nest, IReadOnlyList<string> errors)
    {
        Nest = nest;
        Errors = errors;
    }

    /// <summary>The new nest, or null when none of the group's drawings imported.</summary>
    public Nest Nest { get; }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Builds the nest for one BOM import group.</summary>
public static class BomNestBuilder
{
    /// <summary>
    /// Creates a nest named "<paramref name="jobName"/> - thickness material".
    /// <paramref name="applySavedDefaults"/> runs first (units, quadrant,
    /// plate), then the group's plate size and spacing, material and
    /// thickness are set. Each drawing file is imported once, needing the
    /// total quantity of the rows that name it (a blank quantity counts as
    /// 1). The nest gets one plate when at least one drawing imported.
    /// </summary>
    public static BomNestBuildResult Build(
        BomImportGroup group,
        BomGroupPlateSettings plate,
        string jobName,
        Action<Nest> applySavedDefaults
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(plate);

        var material = group.Material;
        var thickness = group.Thickness;
        var errors = new List<string>();

        var nest = new Nest($"{jobName} - {thickness:0.###} {material}");
        nest.DateCreated = DateTime.Now;
        nest.DateLastModified = DateTime.Now;
        // Saved defaults first (units, quadrant, plate), as New does;
        // then the group's own plate size and spacing.
        applySavedDefaults?.Invoke(nest);
        nest.PlateDefaults.Size = new Size(plate.PlateWidth, plate.PlateLength);
        nest.Thickness = thickness;
        nest.Material = new Material(material);
        nest.PlateDefaults.PartSpacing = plate.PartSpacing;
        nest.PlateDefaults.EdgeSpacing = new Spacing(
            plate.EdgeLeft,
            plate.EdgeBottom,
            plate.EdgeRight,
            plate.EdgeTop
        );

        // Rows naming the same drawing file become one drawing that needs
        // their total: the nest's drawing set is keyed by drawing name.
        foreach (var rows in group.Parts.GroupBy(p => p.DxfPath, StringComparer.OrdinalIgnoreCase))
        {
            var part = rows.First();
            if (!File.Exists(part.DxfPath))
            {
                errors.Add($"{part.FileName}: DXF file not found");
                continue;
            }

            try
            {
                var drawing = CadImporter.ImportDrawing(
                    part.DxfPath,
                    new CadImportOptions { Quantity = rows.Sum(p => p.Qty ?? 1) }
                );
                drawing.Material = new Material(material);
                nest.Drawings.Add(drawing);
            }
            catch (Exception ex)
            {
                errors.Add($"{part.FileName}: {ex.Message}");
            }
        }

        if (nest.Drawings.Count == 0)
            return new BomNestBuildResult(null, errors);

        nest.CreatePlate();
        return new BomNestBuildResult(nest, errors);
    }
}
