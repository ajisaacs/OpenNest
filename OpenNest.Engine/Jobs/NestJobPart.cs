using System;

namespace OpenNest.Engine.Jobs;

/// <summary>An immutable requirement, independent of drawing names, UI state, and drawing quantity counters.</summary>
public sealed class NestJobPart
{
    /// <summary>Creates an immutable part requirement.</summary>
    /// <param name="id">Unique requirement ID.</param>
    /// <param name="geometry">Snapshot of the part geometry.</param>
    /// <param name="quantity">Positive number requested.</param>
    /// <param name="priority">Placement precedence: lower numbers are placed first and win scarce
    /// stock. Zero is the default and highest ordinary priority; equal priorities are peers.</param>
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
    /// Placement precedence: a lower number is placed first and wins scarce stock.
    /// Zero is the default and highest ordinary priority. Parts with equal priority are peers.
    /// </summary>
    public int Priority { get; }
    public RotationPolicy Rotation { get; }
}
