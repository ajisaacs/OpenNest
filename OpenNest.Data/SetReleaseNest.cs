using OpenNest;

namespace OpenNest.Data;

/// <summary>One nest of a release and the material group it was made for.</summary>
public sealed class SetReleaseNest
{
    public Guid NestId { get; set; }

    public string MaterialName { get; set; } = "";

    public string MaterialGrade { get; set; } = "";

    /// <summary>Thickness in <see cref="Units"/>, stored rounded to four decimals.</summary>
    public double Thickness { get; set; }

    public Units Units { get; set; } = Units.Inches;

    public CutType CutType { get; set; } = CutType.Laser;

    /// <summary>
    /// The nest's current metadata, deleted or not, joined by the server when it reads a
    /// release, so status and deletion are always up to date. Ignored on post.
    /// </summary>
    public NestRecord? Nest { get; set; }
}
