using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest;

/// <summary>
/// Splits a Drawing into multiple pieces along split lines with optional feature geometry.
/// </summary>
public static class DrawingSplitter
{
    public static List<Drawing> Split(Drawing drawing, List<SplitLine> splitLines, SplitParameters parameters)
    {
        if (splitLines.Count == 0)
            return new List<Drawing> { drawing };

        // 1. Convert program to geometry -> ShapeProfile separates perimeter from cutouts
        //    Filter out rapid-layer entities so ShapeBuilder doesn't chain cutouts to the perimeter
        var entities = ConvertProgram.ToGeometry(drawing.Program)
            .Where(e => e.Layer != SpecialLayers.Rapid)
            .ToList();
        var profile = new ShapeProfile(entities);

        // Decompose circles to arcs so all entities support SplitAt()
        DecomposeCircles(profile);

        var perimeter = profile.Perimeter;
        var bounds = perimeter.BoundingBox;

        // 2. Sort split lines by position, discard any outside the part
        var sortedLines = splitLines
            .Where(l => IsLineInsideBounds(l, bounds))
            .OrderBy(l => l.Position)
            .ToList();

        if (sortedLines.Count == 0)
            return new List<Drawing> { drawing };

        // 3. Build clip regions (grid cells between split lines)
        var regions = BuildClipRegions(sortedLines, bounds);

        // 4. Get the split feature strategy
        var feature = GetFeature(parameters.Type);

        // 5. For each region, clip the perimeter and build a new drawing
        var results = new List<Drawing>();
        var pieceIndex = 1;

        foreach (var region in regions)
        {
            var pieceEntities = ClipPerimeterToRegion(perimeter, region, sortedLines, feature, parameters);

            if (pieceEntities.Count == 0)
                continue;

            // Assign cutouts fully inside this region
            var cutoutEntities = new List<Entity>();
            foreach (var cutout in profile.Cutouts)
            {
                if (IsCutoutInRegion(cutout, region))
                    cutoutEntities.AddRange(cutout.Entities);
                else if (DoesCutoutCrossSplitLine(cutout, sortedLines))
                {
                    // Cutout crosses a split line -- clip it to this region too
                    var clippedCutout = ClipCutoutToRegion(cutout, region);
                    if (clippedCutout.Count > 0)
                        cutoutEntities.AddRange(clippedCutout);
                }
            }

            // Normalize origin: translate so bounding box starts at (0,0)
            var allEntities = new List<Entity>();
            allEntities.AddRange(pieceEntities);
            allEntities.AddRange(cutoutEntities);

            var pieceBounds = allEntities.Select(e => e.BoundingBox).ToList().GetBoundingBox();
            var offsetX = -pieceBounds.X;
            var offsetY = -pieceBounds.Y;

            foreach (var e in allEntities)
                e.Offset(offsetX, offsetY);

            // Build program (ConvertGeometry.ToProgram internally identifies perimeter vs cutouts)
            var pgm = ConvertGeometry.ToProgram(allEntities);

            // Create drawing with copied properties
            var piece = new Drawing($"{drawing.Name}-{pieceIndex}", pgm);
            piece.Color = drawing.Color;
            piece.Priority = drawing.Priority;
            piece.Material = drawing.Material;
            piece.Constraints = drawing.Constraints;
            piece.Customer = drawing.Customer;
            piece.Source = drawing.Source;

            piece.Quantity.Required = drawing.Quantity.Required;

            results.Add(piece);
            pieceIndex++;
        }

        return results;
    }

    private static void DecomposeCircles(ShapeProfile profile)
    {
        DecomposeCirclesInShape(profile.Perimeter);
        foreach (var cutout in profile.Cutouts)
            DecomposeCirclesInShape(cutout);
    }

    private static void DecomposeCirclesInShape(Shape shape)
    {
        for (var i = shape.Entities.Count - 1; i >= 0; i--)
        {
            if (shape.Entities[i] is Circle circle)
            {
                var arc1 = new Arc(circle.Center, circle.Radius, 0, System.Math.PI);
                var arc2 = new Arc(circle.Center, circle.Radius, System.Math.PI, System.Math.PI * 2);
                shape.Entities.RemoveAt(i);
                shape.Entities.Insert(i, arc2);
                shape.Entities.Insert(i, arc1);
            }
        }
    }

    private static bool IsLineInsideBounds(SplitLine line, Box bounds)
    {
        return line.Axis == CutOffAxis.Vertical
            ? line.Position > bounds.Left + OpenNest.Math.Tolerance.Epsilon
              && line.Position < bounds.Right - OpenNest.Math.Tolerance.Epsilon
            : line.Position > bounds.Bottom + OpenNest.Math.Tolerance.Epsilon
              && line.Position < bounds.Top - OpenNest.Math.Tolerance.Epsilon;
    }

    private static List<Box> BuildClipRegions(List<SplitLine> sortedLines, Box bounds)
    {
        var verticals = sortedLines.Where(l => l.Axis == CutOffAxis.Vertical).OrderBy(l => l.Position).ToList();
        var horizontals = sortedLines.Where(l => l.Axis == CutOffAxis.Horizontal).OrderBy(l => l.Position).ToList();

        var xEdges = new List<double> { bounds.Left };
        xEdges.AddRange(verticals.Select(v => v.Position));
        xEdges.Add(bounds.Right);

        var yEdges = new List<double> { bounds.Bottom };
        yEdges.AddRange(horizontals.Select(h => h.Position));
        yEdges.Add(bounds.Top);

        var regions = new List<Box>();
        for (var yi = 0; yi < yEdges.Count - 1; yi++)
            for (var xi = 0; xi < xEdges.Count - 1; xi++)
                regions.Add(new Box(xEdges[xi], yEdges[yi], xEdges[xi + 1] - xEdges[xi], yEdges[yi + 1] - yEdges[yi]));

        return regions;
    }

    /// <summary>
    /// Clip perimeter to a region using Clipper2, then recover original arcs and stitch in feature edges.
    /// </summary>
    private static List<Entity> ClipPerimeterToRegion(Shape perimeter, Box region,
        List<SplitLine> splitLines, ISplitFeature feature, SplitParameters parameters)
    {
        var perimPoly = perimeter.ToPolygonWithTolerance(0.01);

        var regionPoly = new Polygon();
        regionPoly.Vertices.Add(new Vector(region.Left, region.Bottom));
        regionPoly.Vertices.Add(new Vector(region.Right, region.Bottom));
        regionPoly.Vertices.Add(new Vector(region.Right, region.Top));
        regionPoly.Vertices.Add(new Vector(region.Left, region.Top));
        regionPoly.Close();

        // Reuse existing Clipper2 helpers from NoFitPolygon
        var subj = new Clipper2Lib.PathsD { NoFitPolygon.ToClipperPath(perimPoly) };
        var clip = new Clipper2Lib.PathsD { NoFitPolygon.ToClipperPath(regionPoly) };
        var result = Clipper2Lib.Clipper.Intersect(subj, clip, Clipper2Lib.FillRule.NonZero);

        if (result.Count == 0)
            return new List<Entity>();

        var clippedPoly = NoFitPolygon.FromClipperPath(result[0]);
        var clippedEntities = new List<Entity>();

        var verts = clippedPoly.Vertices;
        for (var i = 0; i < verts.Count - 1; i++)
        {
            var start = verts[i];
            var end = verts[i + 1];

            // Check if this edge lies on a split line -- replace with feature geometry
            var splitLine = FindSplitLineForEdge(start, end, splitLines);
            if (splitLine != null)
            {
                var extentStart = splitLine.Axis == CutOffAxis.Vertical
                    ? System.Math.Min(start.Y, end.Y)
                    : System.Math.Min(start.X, end.X);
                var extentEnd = splitLine.Axis == CutOffAxis.Vertical
                    ? System.Math.Max(start.Y, end.Y)
                    : System.Math.Max(start.X, end.X);

                var featureResult = feature.GenerateFeatures(splitLine, extentStart, extentEnd, parameters);

                var regionCenter = splitLine.Axis == CutOffAxis.Vertical
                    ? (region.Left + region.Right) / 2
                    : (region.Bottom + region.Top) / 2;
                var isNegativeSide = regionCenter < splitLine.Position;
                var featureEdge = isNegativeSide ? featureResult.NegativeSideEdge : featureResult.PositiveSideEdge;

                // Ensure feature edge direction matches the polygon winding
                if (featureEdge.Count > 0)
                {
                    var featureStart = GetStartPoint(featureEdge[0]);
                    var featureEnd = GetEndPoint(featureEdge[^1]);
                    var edgeGoesForward = splitLine.Axis == CutOffAxis.Vertical
                        ? start.Y < end.Y : start.X < end.X;
                    var featureGoesForward = splitLine.Axis == CutOffAxis.Vertical
                        ? featureStart.Y < featureEnd.Y : featureStart.X < featureEnd.X;

                    if (edgeGoesForward != featureGoesForward)
                    {
                        featureEdge = new List<Entity>(featureEdge);
                        featureEdge.Reverse();
                        foreach (var e in featureEdge)
                            e.Reverse();
                    }
                }

                clippedEntities.AddRange(featureEdge);
            }
            else
            {
                // Try to recover original arc for this chord edge
                var originalArc = FindMatchingArc(start, end, perimeter);
                if (originalArc != null)
                    clippedEntities.Add(originalArc);
                else
                    clippedEntities.Add(new Line(start, end));
            }
        }

        // Ensure CW winding for perimeter (positive area = CCW in Polygon, so CW = negative)
        var shape = new Shape();
        shape.Entities.AddRange(clippedEntities);
        var poly = shape.ToPolygon();
        if (poly != null && poly.RotationDirection() != RotationType.CW)
            shape.Reverse();

        return shape.Entities;
    }

    private static SplitLine FindSplitLineForEdge(Vector start, Vector end, List<SplitLine> splitLines)
    {
        foreach (var sl in splitLines)
        {
            if (sl.Axis == CutOffAxis.Vertical)
            {
                if (System.Math.Abs(start.X - sl.Position) < 0.1 && System.Math.Abs(end.X - sl.Position) < 0.1)
                    return sl;
            }
            else
            {
                if (System.Math.Abs(start.Y - sl.Position) < 0.1 && System.Math.Abs(end.Y - sl.Position) < 0.1)
                    return sl;
            }
        }
        return null;
    }

    /// <summary>
    /// Search original perimeter for an arc whose circle matches this polygon chord edge.
    /// Returns a new arc segment between the chord endpoints if found.
    /// </summary>
    private static Arc FindMatchingArc(Vector start, Vector end, Shape perimeter)
    {
        foreach (var entity in perimeter.Entities)
        {
            if (entity is Arc arc)
            {
                var distStart = start.DistanceTo(arc.Center) - arc.Radius;
                var distEnd = end.DistanceTo(arc.Center) - arc.Radius;

                if (System.Math.Abs(distStart) < 0.1 && System.Math.Abs(distEnd) < 0.1)
                {
                    var startAngle = OpenNest.Math.Angle.NormalizeRad(arc.Center.AngleTo(start));
                    var endAngle = OpenNest.Math.Angle.NormalizeRad(arc.Center.AngleTo(end));
                    return new Arc(arc.Center, arc.Radius, startAngle, endAngle, arc.IsReversed);
                }
            }
        }
        return null;
    }

    private static bool IsCutoutInRegion(Shape cutout, Box region)
    {
        if (cutout.Entities.Count == 0) return false;
        var pt = GetStartPoint(cutout.Entities[0]);
        return region.Contains(pt);
    }

    private static bool DoesCutoutCrossSplitLine(Shape cutout, List<SplitLine> splitLines)
    {
        var bb = cutout.BoundingBox;
        foreach (var sl in splitLines)
        {
            if (sl.Axis == CutOffAxis.Vertical && bb.Left < sl.Position && bb.Right > sl.Position)
                return true;
            if (sl.Axis == CutOffAxis.Horizontal && bb.Bottom < sl.Position && bb.Top > sl.Position)
                return true;
        }
        return false;
    }

    private static List<Entity> ClipCutoutToRegion(Shape cutout, Box region)
    {
        var cutoutPoly = cutout.ToPolygonWithTolerance(0.01);
        var regionPoly = new Polygon();
        regionPoly.Vertices.Add(new Vector(region.Left, region.Bottom));
        regionPoly.Vertices.Add(new Vector(region.Right, region.Bottom));
        regionPoly.Vertices.Add(new Vector(region.Right, region.Top));
        regionPoly.Vertices.Add(new Vector(region.Left, region.Top));
        regionPoly.Close();

        var subj = new Clipper2Lib.PathsD { NoFitPolygon.ToClipperPath(cutoutPoly) };
        var clip = new Clipper2Lib.PathsD { NoFitPolygon.ToClipperPath(regionPoly) };
        var result = Clipper2Lib.Clipper.Intersect(subj, clip, Clipper2Lib.FillRule.NonZero);

        if (result.Count == 0)
            return new List<Entity>();

        var clippedPoly = NoFitPolygon.FromClipperPath(result[0]);
        var lineEntities = new List<Entity>();
        var verts = clippedPoly.Vertices;
        for (var i = 0; i < verts.Count - 1; i++)
            lineEntities.Add(new Line(verts[i], verts[i + 1]));

        // Ensure CCW winding for cutouts
        var shape = new Shape();
        shape.Entities.AddRange(lineEntities);
        var poly = shape.ToPolygon();
        if (poly != null && poly.RotationDirection() != RotationType.CCW)
            shape.Reverse();

        return shape.Entities;
    }

    private static Vector GetStartPoint(Entity entity)
    {
        return entity switch
        {
            Line l => l.StartPoint,
            Arc a => a.StartPoint(), // Arc.StartPoint() is a method
            _ => new Vector(0, 0)
        };
    }

    private static Vector GetEndPoint(Entity entity)
    {
        return entity switch
        {
            Line l => l.EndPoint,
            Arc a => a.EndPoint(), // Arc.EndPoint() is a method
            _ => new Vector(0, 0)
        };
    }

    private static ISplitFeature GetFeature(SplitType type)
    {
        return type switch
        {
            SplitType.Straight => new StraightSplit(),
            SplitType.WeldGapTabs => new WeldGapTabSplit(),
            SplitType.SpikeGroove => new SpikeGrooveSplit(),
            _ => new StraightSplit()
        };
    }
}
