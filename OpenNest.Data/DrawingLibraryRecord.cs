using OpenNest;

namespace OpenNest.Data;

/// <summary>
/// Server-side metadata for one revision of a shared library drawing. The geometry
/// travels separately as a drawing package (a single-drawing, zero-plate .nest archive).
/// </summary>
public sealed class DrawingLibraryRecord
{
    /// <summary>Allocated by the client before the first upload; kept by every revision.</summary>
    public Guid Id { get; set; }

    /// <summary>Immutable revision number, starting at 1. Ignored on upload.</summary>
    public int Revision { get; set; }

    public string Name { get; set; } = "";

    public string Customer { get; set; } = "";

    /// <summary>Post material name, for example "Carbon Steel".</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>Alloy or grade within the material, for example "A36 HR".</summary>
    public string MaterialGrade { get; set; } = "";

    /// <summary>Thickness in <see cref="Units"/>, stored rounded to four decimals.</summary>
    public double Thickness { get; set; }

    public Units Units { get; set; } = Units.Inches;

    public CutType CutType { get; set; } = CutType.Laser;

    public string Notes { get; set; } = "";

    /// <summary>
    /// Nesting settings applied when the drawing is added to a nest. Not part of the
    /// package, so changing them adds a revision that reuses the same package.
    /// </summary>
    public DrawingNestingSettings Settings { get; set; } = new();

    /// <summary>Hidden from browsing by default; still readable by id.</summary>
    public bool Archived { get; set; }

    /// <summary>Lower-case hex SHA-256 of the package bytes. Server-computed.</summary>
    public string PackageSha256 { get; set; } = "";

    /// <summary>Package size in bytes. Server-computed.</summary>
    public long PackageSize { get; set; }

    /// <summary>When the server stored this revision. Server-computed.</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>Who saved this revision, as reported by the client.</summary>
    public string SavedBy { get; set; } = "";
}
