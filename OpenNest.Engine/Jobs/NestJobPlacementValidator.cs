using System;
using System.Collections.Generic;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

/// <summary>Validates a trial against immutable job geometry before the runner commits accounting.</summary>
internal static class NestJobPlacementValidator
{
    private const double Epsilon = 0.0000001;
    // Flattening for placement overlap/spacing checks: the same 0.001 the benchmark's
    // NestValidator and Part.Intersects use. Arcs are inscribed, so a layout placed exactly at
    // the spacing passes; outward arcs may come up to this much closer than the spacing.
    private const double PlacementChordTolerance = NestTolerances.ValidationOutline;

    internal static void ValidateCandidate(
        PlateCandidate candidate,
        NestPlateStock stock,
        IReadOnlyDictionary<string, int> remaining,
        IReadOnlyDictionary<string, NestJobPart> parts
    )
    {
        if (candidate == null)
            throw new InvalidOperationException("The plate nester returned a null candidate.");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var placed = new List<ShapeTopology>();
        var sources = new Dictionary<string, ShapeTopology>(StringComparer.Ordinal);
        foreach (var placement in candidate.Placements)
        {
            if (
                placement.PartId == null
                || !remaining.TryGetValue(placement.PartId, out var available)
                || !parts.TryGetValue(placement.PartId, out var part)
            )
                throw new InvalidOperationException(
                    "Candidate references an unknown requirement ID."
                );
            if (
                !double.IsFinite(placement.X)
                || !double.IsFinite(placement.Y)
                || !double.IsFinite(placement.Rotation)
            )
                throw new InvalidOperationException("Candidate poses must be finite.");
            counts.TryGetValue(placement.PartId, out var count);
            if (count >= available)
                throw new InvalidOperationException("Candidate overproduces a requirement.");
            if (!part.Rotation.Allows(placement.Rotation))
                throw new InvalidOperationException(
                    "Candidate rotation is not allowed for the requirement."
                );

            if (!sources.TryGetValue(placement.PartId, out var source))
                sources[placement.PartId] = source = CreateShape(part.Geometry);
            var shape = Transform(source, placement);
            if (!FitsWorkArea(shape, stock))
                throw new InvalidOperationException(
                    "Candidate placement falls outside the usable stock area."
                );
            foreach (var other in placed)
            {
                // Analytic contour bounds give a conservative lower bound on clearance.
                // Do not polygonize or compare every hole edge for distant placements.
                if (BoundsDistance(shape.Perimeter.BoundingBox, other.Perimeter.BoundingBox)
                    >= stock.PartSpacing && !shape.Perimeter.BoundingBox.Intersects(other.Perimeter.BoundingBox))
                    continue;
                if (Overlaps(shape, other))
                    throw new InvalidOperationException("Candidate placements overlap.");
                if (stock.PartSpacing > 0
                    && Distance(shape, other) < stock.PartSpacing - NestTolerances.SpacingSlack)
                    throw new InvalidOperationException(
                        "Candidate placements violate required part spacing."
                    );
            }

            placed.Add(shape);
            counts[placement.PartId] = count + 1;
        }
    }

    private static ShapeTopology CreateShape(PartGeometrySnapshot geometry)
    {
        var shape = JobPartGeometry.Read(geometry);
        return new ShapeTopology(shape.Perimeter, shape.Profile.Cutouts);
    }

    private static ShapeTopology Transform(ShapeTopology source, NestJobPlacement placement)
    {
        var perimeter = TransformContour(source.Perimeter, placement);
        var cutouts = new List<Shape>(source.Cutouts.Count);
        foreach (var cutout in source.Cutouts)
            cutouts.Add(TransformContour(cutout, placement));
        return new ShapeTopology(perimeter, cutouts);
    }

    private static Shape TransformContour(Shape source, NestJobPlacement placement)
    {
        var contour = (Shape)source.Clone();
        contour.Rotate(placement.Rotation);
        contour.Offset(placement.X, placement.Y);
        return contour;
    }

    private static bool FitsWorkArea(ShapeTopology shape, NestPlateStock stock)
    {
        var workArea = stock.WorkArea;
        if (!FitsWorkArea(shape.Perimeter, workArea))
            return false;
        foreach (var cutout in shape.Cutouts)
            if (!FitsWorkArea(cutout, workArea))
                return false;
        return true;
    }

    private static bool FitsWorkArea(Shape contour, Box workArea)
    {
        var bounds = contour.BoundingBox;
        return bounds.Left >= workArea.Left - Epsilon
            && bounds.Right <= workArea.Right + Epsilon
            && bounds.Bottom >= workArea.Bottom - Epsilon
            && bounds.Top <= workArea.Top + Epsilon;
    }

    private static bool Overlaps(ShapeTopology left, ShapeTopology right)
    {
        var leftPoly = left.Contours[0].Polygon;
        var rightPoly = right.Contours[0].Polygon;
        if (!leftPoly.BoundingBox.Intersects(rightPoly.BoundingBox))
            return false;
        // True material overlap requires shared interior area, not boundary touching.
        // Edge/corner contact (zero clearance) is a valid placement when part spacing is zero.
        // Collision checks this by clipping triangulated polygons and rejecting zero-area
        // slivers, so it catches containment and small corner intersections that a witness
        // probe can miss, while contact stays legal; cutouts are subtracted from both sides.
        return Collision.HasOverlap(
            leftPoly,
            rightPoly,
            left.CutoutPolygons,
            right.CutoutPolygons
        );
    }

    private static double BoundsDistance(Box left, Box right)
    {
        var x = System.Math.Max(0, System.Math.Max(left.Left - right.Right, right.Left - left.Right));
        var y = System.Math.Max(0, System.Math.Max(left.Bottom - right.Top, right.Bottom - left.Top));
        return System.Math.Sqrt(x * x + y * y);
    }

    private static double Distance(ShapeTopology left, ShapeTopology right)
    {
        var result = double.PositiveInfinity;
        foreach (var leftContour in left.Contours)
        foreach (var rightContour in right.Contours)
            if (BoundsDistance(leftContour.Bounds, rightContour.Bounds) < result)
                result = System.Math.Min(
                    result,
                    BoundaryDistance(leftContour.Lines, rightContour.Lines)
                );
        return result;
    }

    private static double BoundaryDistance(List<Line> left, List<Line> right)
    {
        var result = double.PositiveInfinity;
        foreach (var leftLine in left)
        {
            foreach (var rightLine in right)
            {
                if (leftLine.Intersects(rightLine))
                    return 0;
                result = System.Math.Min(
                    result,
                    leftLine.ClosestPointTo(rightLine.StartPoint).DistanceTo(rightLine.StartPoint)
                );
                result = System.Math.Min(
                    result,
                    leftLine.ClosestPointTo(rightLine.EndPoint).DistanceTo(rightLine.EndPoint)
                );
                result = System.Math.Min(
                    result,
                    rightLine.ClosestPointTo(leftLine.StartPoint).DistanceTo(leftLine.StartPoint)
                );
                result = System.Math.Min(
                    result,
                    rightLine.ClosestPointTo(leftLine.EndPoint).DistanceTo(leftLine.EndPoint)
                );
            }
        }
        return result;
    }

    private sealed class ShapeTopology(Shape perimeter, List<Shape> cutouts)
    {
        private Contour[] contours;
        private List<Polygon> cutoutPolygons;

        internal Shape Perimeter { get; } = perimeter;
        internal List<Shape> Cutouts { get; } = cutouts;

        /// <summary>The perimeter first, then the cutouts, each flattened once on first use.</summary>
        internal Contour[] Contours
        {
            get
            {
                if (contours != null)
                    return contours;
                var result = new Contour[Cutouts.Count + 1];
                result[0] = new Contour(Perimeter);
                for (var i = 0; i < Cutouts.Count; i++)
                    result[i + 1] = new Contour(Cutouts[i]);
                return contours = result;
            }
        }

        internal List<Polygon> CutoutPolygons
        {
            get
            {
                if (cutoutPolygons != null)
                    return cutoutPolygons;
                var result = new List<Polygon>(Cutouts.Count);
                for (var i = 1; i < Contours.Length; i++)
                    result.Add(Contours[i].Polygon);
                return cutoutPolygons = result;
            }
        }
    }

    /// <summary>
    /// A contour flattened once, at <see cref="PlacementChordTolerance"/>, for the overlap and
    /// spacing checks against every other placement.
    /// </summary>
    private sealed class Contour
    {
        internal Contour(Shape shape)
        {
            Polygon = shape.ToPolygonWithTolerance(PlacementChordTolerance);
            Bounds = Polygon.BoundingBox;
            Lines = Polygon.ToLines();
        }

        internal Box Bounds { get; }
        internal Polygon Polygon { get; }
        internal List<Line> Lines { get; }
    }
}
