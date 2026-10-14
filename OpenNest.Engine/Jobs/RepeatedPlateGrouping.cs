using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.Engine.Jobs;

/// <summary>Alternate representation of checked physical sheets; never changes raw output.
/// Equality includes ordered requirement IDs and scalar bits, not instance indices.</summary>
internal static class RepeatedPlateGrouping
{
    internal sealed record Group(NestJobPlateResult Sheet, int Quantity);

    internal static IReadOnlyList<Group> Create(IReadOnlyList<NestJobPlateResult> sheets,
        bool consolidate, CancellationToken token = default)
    {
        var groups = new List<Group>();
        var firstByLayout = new Dictionary<NestJobPlateResult, int>(LayoutComparer.Instance);
        foreach (var sheet in sheets)
        {
            token.ThrowIfCancellationRequested();
            if (consolidate && sheet.Placements.Count > 0
                && firstByLayout.TryGetValue(sheet, out var first))
                groups[first] = groups[first] with { Quantity = checked(groups[first].Quantity + 1) };
            else
            {
                if (consolidate && sheet.Placements.Count > 0)
                    firstByLayout.Add(sheet, groups.Count);
                groups.Add(new Group(sheet, 1));
            }
        }
        return groups.AsReadOnly();
    }

    internal static bool SameStock(NestPlateStock a, NestPlateStock b) =>
        string.Equals(a.Id, b.Id, StringComparison.Ordinal)
        && Bits(a.Size.Width) == Bits(b.Size.Width) && Bits(a.Size.Length) == Bits(b.Size.Length)
        && a.Quadrant == b.Quadrant && Bits(a.PartSpacing) == Bits(b.PartSpacing)
        && Bits(a.EdgeSpacing.Left) == Bits(b.EdgeSpacing.Left)
        && Bits(a.EdgeSpacing.Right) == Bits(b.EdgeSpacing.Right)
        && Bits(a.EdgeSpacing.Top) == Bits(b.EdgeSpacing.Top)
        && Bits(a.EdgeSpacing.Bottom) == Bits(b.EdgeSpacing.Bottom)
        && a.Quantity == b.Quantity
        && a.Cost.HasValue == b.Cost.HasValue
        && (!a.Cost.HasValue || Bits(a.Cost.Value) == Bits(b.Cost.Value))
        && SameZones(a.ClampZones, b.ClampZones);

    // C8: a layout solved with clamps is never reused for a plate without them - and a
    // different clamp set is different stock. Ordered; ids and exact bits participate.
    private static bool SameZones(
        System.Collections.Generic.IReadOnlyList<ClampZone> x,
        System.Collections.Generic.IReadOnlyList<ClampZone> y
    )
    {
        if (x.Count != y.Count)
            return false;
        for (var i = 0; i < x.Count; i++)
        {
            if (
                !string.Equals(x[i].Id, y[i].Id, StringComparison.Ordinal)
                || Bits(x[i].Bounds.X) != Bits(y[i].Bounds.X)
                || Bits(x[i].Bounds.Y) != Bits(y[i].Bounds.Y)
                || Bits(x[i].Bounds.Length) != Bits(y[i].Bounds.Length)
                || Bits(x[i].Bounds.Width) != Bits(y[i].Bounds.Width)
            )
                return false;
        }
        return true;
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private sealed class LayoutComparer : IEqualityComparer<NestJobPlateResult>
    {
        internal static readonly LayoutComparer Instance = new();

        public bool Equals(NestJobPlateResult a, NestJobPlateResult b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || !SameStock(a.Stock, b.Stock)
                || a.Placements.Count != b.Placements.Count)
                return false;
            for (var i = 0; i < a.Placements.Count; i++)
            {
                var x = a.Placements[i];
                var y = b.Placements[i];
                if (!string.Equals(x.PartId, y.PartId, StringComparison.Ordinal)
                    || Bits(x.X) != Bits(y.X) || Bits(x.Y) != Bits(y.Y)
                    || Bits(x.Rotation) != Bits(y.Rotation))
                    return false;
            }
            return true;
        }

        public int GetHashCode(NestJobPlateResult sheet)
        {
            // Hash only accelerates lookup; Equals checks every stock field and ordered pose.
            var hash = new HashCode();
            hash.Add(sheet.StockId, StringComparer.Ordinal);
            foreach (var pose in sheet.Placements)
            {
                hash.Add(pose.PartId, StringComparer.Ordinal);
                hash.Add(Bits(pose.X));
                hash.Add(Bits(pose.Y));
                hash.Add(Bits(pose.Rotation));
            }
            return hash.ToHashCode();
        }
    }
}
