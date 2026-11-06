using System;

namespace OpenNest.Engine.Jobs;

/// <summary>An immutable requirement, independent of drawing names, UI state, and drawing quantity counters.</summary>
public sealed class NestJobPart
{
    /// <summary>Creates an immutable part requirement.</summary>
    /// <param name="id">Unique requirement ID.</param>
    /// <param name="geometry">Snapshot of the part geometry.</param>
    /// <param name="quantity">Positive number requested.</param>
    /// <param name="priority">Placement precedence: higher numbers are placed first and win scarce
    /// stock. Zero is the default; negative numbers place after it. Equal priorities are peers.</param>
    /// <param name="rotation">Allowed rotations, or null for automatic rotation.</param>
    public NestJobPart(
        string id,
        PartGeometrySnapshot geometry,
        int quantity,
        int priority = 0,
        RotationPolicy rotation = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(geometry);
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Id = id;
        Geometry = geometry;
        Quantity = quantity;
        Priority = priority;
        Rotation = rotation ?? RotationPolicy.Automatic;
    }

    public string Id { get; }
    public PartGeometrySnapshot Geometry { get; }

    /// <summary>Positive number requested; never decremented by placement code.</summary>
    public int Quantity { get; }
    /// <summary>
    /// Placement precedence: a higher number is placed first and wins scarce stock.
    /// Zero is the default; negative numbers place after it. Parts with equal priority are peers.
    /// </summary>
    public int Priority { get; }
    public RotationPolicy Rotation { get; }

    /// <summary>
    /// The part may share a common line with copies of itself when the job's
    /// <see cref="NestJobOptions.CommonLineUsage"/> allows it (<c>Drawing.AllowCommonLine</c>).
    /// </summary>
    public bool AllowCommonLine { get; init; }
}
