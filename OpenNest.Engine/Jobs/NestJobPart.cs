using System;

namespace OpenNest;

/// <summary>An immutable requirement, independent of drawing names, UI state, and drawing quantity counters.</summary>
public sealed class NestJobPart
{
    public NestJobPart(string id, PartGeometrySnapshot geometry, int quantity, int priority = 0,
        RotationPolicy rotation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(geometry);
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
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
    public int Priority { get; }
    public RotationPolicy Rotation { get; }
}
