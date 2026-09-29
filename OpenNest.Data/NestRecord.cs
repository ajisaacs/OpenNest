using OpenNest;

namespace OpenNest.Data;

/// <summary>
/// Server-side metadata for one saved nest. The .nest archive itself is stored
/// alongside; this record is what lists, filters and status tracking read.
/// Counts are captured by the client at save time.
/// </summary>
public sealed class NestRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Customer { get; set; } = "";

    public DateTime DateCreated { get; set; }

    public DateTime DateModified { get; set; }

    public string Material { get; set; } = "";

    public double Thickness { get; set; }

    /// <summary>quote | toBeCut | hasBeenCut (camelCase on the wire, case-insensitive readers).</summary>
    public NestStatus Status { get; set; } = NestStatus.Quote;

    public int PlateCount { get; set; }

    public int PartCount { get; set; }

    public string Comments { get; set; } = "";

    public string MadeBy { get; set; } = "";

    /// <summary>Size of the stored .nest archive in bytes.</summary>
    public long FileSize { get; set; }

    /// <summary>When the server last stored the nest contents or metadata.</summary>
    public DateTime SavedAt { get; set; }
}
