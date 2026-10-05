using System;

namespace OpenNest.IO.Bom;

/// <summary>Whether a BOM row can be imported, and if not, why.</summary>
public enum BomRowStatus
{
    /// <summary>Drawing found, material and thickness set: the row is imported.</summary>
    Ready,

    /// <summary>Drawing found but the material is blank.</summary>
    NeedsMaterial,

    /// <summary>Drawing found but the thickness is blank, zero or negative.</summary>
    NeedsThickness,

    /// <summary>No drawing file matches the row's file name.</summary>
    NoDrawing,

    /// <summary>The BOM row has no file name.</summary>
    NoFileName,
}

/// <summary>
/// One BOM line as the import dialog shows and edits it: the values read
/// from the BOM, the drawing file it resolved to and whether it can be
/// imported.
/// </summary>
public class BomPartRow
{
    public int? ItemNum { get; set; }

    public string FileName { get; set; }

    public int? Qty { get; set; }

    public string Description { get; set; }

    public string Material { get; set; }

    public double? Thickness { get; set; }

    /// <summary>The matched drawing file, or null when none was found.</summary>
    public string DxfPath { get; set; }

    public BomRowStatus Status
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FileName))
                return BomRowStatus.NoFileName;
            if (string.IsNullOrWhiteSpace(DxfPath))
                return BomRowStatus.NoDrawing;
            if (string.IsNullOrWhiteSpace(Material))
                return BomRowStatus.NeedsMaterial;
            if (Thickness is not double thickness || !double.IsFinite(thickness) || thickness <= 0)
                return BomRowStatus.NeedsThickness;
            return BomRowStatus.Ready;
        }
    }

    public string StatusText => Describe(Status);

    /// <summary>
    /// True when the row has a drawing, so the operator can complete its
    /// values; rows without one can never be imported.
    /// </summary>
    public bool IsEditable => Status is not (BomRowStatus.NoFileName or BomRowStatus.NoDrawing);

    public static string Describe(BomRowStatus status) =>
        status switch
        {
            BomRowStatus.Ready => "Ready",
            BomRowStatus.NeedsMaterial => "Needs material",
            BomRowStatus.NeedsThickness => "Needs thickness",
            BomRowStatus.NoDrawing => "No drawing found",
            BomRowStatus.NoFileName => "No file name",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
}
