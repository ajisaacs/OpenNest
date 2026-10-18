#nullable enable
using System;
using System.Collections.Generic;
using OpenNest.Engine.CirclePacking;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>Exact replays of pure cutout computations within one pre-pass, which asks the same
/// questions for every frame instance and frame angle. A clear frame and insert cost
/// <see cref="NestLayoutCheck.Clears"/> its worst case: every perimeter triangle pair, then every
/// hole triangle. Not thread-safe; one instance per call.</summary>
/// <remarks>Clearance keys hold both geometries by reference, their poses bit for bit, the
/// argument order and the spacing, so a replay is the answer the check would give. Geometry is
/// read once per snapshot; shared readers must clone before transforming, as they already do.</remarks>
internal sealed class CutoutMemo
{
    private readonly record struct Key(JobPartGeometry A, long AX, long AY, long ARotation,
        JobPartGeometry B, long BX, long BY, long BRotation, long Spacing);

    private readonly Dictionary<Key, bool> clearances = new();
    private readonly Dictionary<PartGeometrySnapshot, JobPartGeometry?> geometries =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JobPartGeometry, Item> items = new();

    internal JobPartGeometry? Read(PartGeometrySnapshot snapshot)
    {
        if (!geometries.TryGetValue(snapshot, out var geometry))
        {
            geometry = JobPartGeometry.TryRead(snapshot);
            geometries.Add(snapshot, geometry);
        }
        return geometry;
    }

    internal Item ItemOf(JobPartGeometry geometry)
    {
        if (!items.TryGetValue(geometry, out var item))
        {
            item = Item.Create(geometry);
            items.Add(geometry, item);
        }
        return item;
    }

    internal bool Clears(JobPartGeometry a, NestJobPlacement pa,
        JobPartGeometry b, NestJobPlacement pb, double spacing)
    {
        var key = new Key(a, Bits(pa.X), Bits(pa.Y), Bits(pa.Rotation),
            b, Bits(pb.X), Bits(pb.Y), Bits(pb.Rotation), Bits(spacing));
        if (!clearances.TryGetValue(key, out var clears))
        {
            clears = NestLayoutCheck.Clears(a, pa, b, pb, spacing);
            clearances.Add(key, clears);
        }
        return clears;
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
}
