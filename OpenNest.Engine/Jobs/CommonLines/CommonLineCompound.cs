#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.CNC;
using OpenNest.CommonLines;
using OpenNest.Diagnostics;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.CommonLines;

/// <summary>
/// A placed common-line group as one compound part: a fill places <see cref="Proxy"/>, whose
/// outline is every member's perimeter and the kerf along their shared spans, and
/// <see cref="Expand"/> turns each placed copy back into the members and their group.
/// </summary>
internal sealed class CommonLineCompound
{
    private readonly List<Part> members;
    private readonly Vector origin;
    private readonly double kerf;

    private CommonLineCompound(List<Part> members, Vector origin, double kerf, Drawing proxy)
    {
        this.members = members;
        this.origin = origin;
        this.kerf = kerf;
        Proxy = proxy;
    }

    /// <summary>The compound outline, solid (members' holes are not offered), as a drawing whose
    /// origin is the lower-left corner of the members' bounding box.</summary>
    public Drawing Proxy { get; }

    public int MemberCount => members.Count;

    /// <summary>
    /// The compound of <paramref name="parts"/> joined at <paramref name="kerf"/>, or null when
    /// they are not one intact common-line group or their outline is not a single profile. The
    /// parts are copied: later moves of the originals do not change the compound.
    /// </summary>
    public static CommonLineCompound? Build(IReadOnlyList<Part> parts, double kerf)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count < 2 || !double.IsFinite(kerf) || kerf <= 0)
            return null;
        try
        {
            var members = parts.Select(part => (Part)part.Clone()).ToList();
            foreach (var member in members)
                member.UpdateBounds();
            var check = CommonLineGeometry.Check(new CommonLineGroup(members, kerf));
            if (!check.IsIntact)
                return null;

            var bounds = ((IEnumerable<IBoundable>)members).GetBoundingBox();
            var origin = new Vector(bounds.X, bounds.Y);
            var pieces = new PathsD();
            var profiles = new Dictionary<(Program, long), ShapeProfile>();
            foreach (var member in members)
            {
                var profile = PartSpacingReview.Profile(member, profiles);
                if (profile?.Perimeter == null)
                    return null;
                pieces.Add(ClipperBridge.ToPath(
                    ClipperBridge.Flatten(profile.Perimeter, CommonLinePair.FlattenTolerance, circumscribe: true),
                    member.Location - origin));
            }
            foreach (var span in check.Spans)
                pieces.Add(Bridge(span.Start - origin, span.End - origin, kerf));

            // Every piece counter-clockwise, whatever the program's winding, so overlaps add up.
            foreach (var piece in pieces)
                if (!Clipper.IsPositive(piece))
                    piece.Reverse();
            var union = Clipper.Union(pieces, new PathsD(), FillRule.NonZero, 8);
            var outers = union.Where(path => Clipper.Area(path) > 0).ToList();
            if (outers.Count != 1)
                return null;

            var geometry = CommonLinePair.Snapshot(outers[0]);
            _ = JobPartGeometry.Read(geometry);
            var proxy = new Drawing("Common-line group", DrawingJobMapper.ToProgram(geometry));
            return new CommonLineCompound(members, origin, kerf, proxy);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or NotSupportedException or ArithmeticException)
        {
            return null;
        }
    }

    /// <summary>
    /// The members at a placed copy of <see cref="Proxy"/> and their common-line group. The
    /// proxy's frame is its drawing's own: turned about the program origin, then moved.
    /// </summary>
    public (List<Part> Parts, CommonLineGroup Group) Expand(Part placed)
    {
        var parts = new List<Part>(members.Count);
        foreach (var member in members)
        {
            var part = (Part)member.Clone();
            part.Offset(-origin.X, -origin.Y);
            part.Rotate(placed.Rotation);
            part.Offset(placed.Location);
            part.UpdateBounds();
            parts.Add(part);
        }
        return (parts, new CommonLineGroup(parts, kerf));
    }

    // The kerf strip along a shared span's centerline, reaching into both members' material so
    // the union joins them into one outline.
    private static PathD Bridge(Vector start, Vector end, double kerf)
    {
        var length = start.DistanceTo(end);
        var half = kerf / 2 + CommonLinePair.BridgeOverlap;
        var nx = -(end.Y - start.Y) / length * half;
        var ny = (end.X - start.X) / length * half;
        return new PathD
        {
            new(start.X - nx, start.Y - ny),
            new(end.X - nx, end.Y - ny),
            new(end.X + nx, end.Y + ny),
            new(start.X + nx, start.Y + ny),
        };
    }
}
