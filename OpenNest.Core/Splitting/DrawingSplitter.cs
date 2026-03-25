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

        var profile = BuildProfile(drawing);
        DecomposeCircles(profile);

        var perimeter = profile.Perimeter;
        var bounds = perimeter.BoundingBox;

        var sortedLines = splitLines
            .Where(l => IsLineInsideBounds(l, bounds))
            .OrderBy(l => l.Position)
            .ToList();

        if (sortedLines.Count == 0)
            return new List<Drawing> { drawing };

        var regions = BuildClipRegions(sortedLines, bounds);
        var feature = GetFeature(parameters.Type);

        var results = new List<Drawing>();
        var pieceIndex = 1;

        foreach (var region in regions)
        {
            var pieceEntities = ClipPerimeterToRegion(perimeter, region, sortedLines, feature, parameters);
            if (pieceEntities.Count == 0)
                continue;

            var cutoutEntities = CollectCutouts(profile.Cutouts, region, sortedLines);

            var allEntities = new List<Entity>();
            allEntities.AddRange(pieceEntities);
            allEntities.AddRange(cutoutEntities);

            var piece = BuildPieceDrawing(drawing, allEntities, pieceIndex, region);
            results.Add(piece);
            pieceIndex++;
        }

        return results;
    }

    private static ShapeProfile BuildProfile(Drawing drawing)
    {
        var entities = ConvertProgram.ToGeometry(drawing.Program)
            .Where(e => e.Layer != SpecialLayers.Rapid)
            .ToList();
        return new ShapeProfile(entities);
    }

    private static List<Entity> CollectCutouts(List<Shape> cutouts, Box region, List<SplitLine> splitLines)
    {
        var entities = new List<Entity>();
        foreach (var cutout in cutouts)
        {
            if (IsCutoutInRegion(cutout, region))
                entities.AddRange(cutout.Entities);
            else if (DoesCutoutCrossSplitLine(cutout, splitLines))
            {
                var clipped = ClipCutoutToRegion(cutout, region, splitLines);
                if (clipped.Count > 0)
                    entities.AddRange(clipped);
            }
        }
        return entities;
    }

    private static Drawing BuildPieceDrawing(Drawing source, List<Entity> entities, int pieceIndex, Box region)
    {
        var pieceBounds = entities.Select(e => e.BoundingBox).ToList().GetBoundingBox();
        var offsetX = -pieceBounds.X;
        var offsetY = -pieceBounds.Y;

        foreach (var e in entities)
            e.Offset(offsetX, offsetY);

        var pgm = ConvertGeometry.ToProgram(entities);
        var piece = new Drawing($"{source.Name}-{pieceIndex}", pgm);
        piece.Color = source.Color;
        piece.Priority = source.Priority;
        piece.Material = source.Material;
        piece.Constraints = source.Constraints;
        piece.Customer = source.Customer;
        piece.Source = source.Source;
        piece.Quantity.Required = source.Quantity.Required;

        if (source.Bends != null && source.Bends.Count > 0)
        {
            piece.Bends = new List<Bending.Bend>();
            foreach (var bend in source.Bends)
            {
                var clipped = ClipLineToBox(bend.StartPoint, bend.EndPoint, region);
                if (clipped == null)
                    continue;

                piece.Bends.Add(new Bending.Bend
                {
                    StartPoint = new Vector(clipped.Value.Start.X + offsetX, clipped.Value.Start.Y + offsetY),
                    EndPoint = new Vector(clipped.Value.End.X + offsetX, clipped.Value.End.Y + offsetY),
                    Direction = bend.Direction,
                    Angle = bend.Angle,
                    Radius = bend.Radius,
                    NoteText = bend.NoteText,
                });
            }
        }

        return piece;
    }

    /// <summary>
    /// Clips a line segment to an axis-aligned box using Liang-Barsky algorithm.
    /// Returns the clipped start/end or null if the line is entirely outside.
    /// </summary>
    private static (Vector Start, Vector End)? ClipLineToBox(Vector start, Vector end, Box box)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        double t0 = 0, t1 = 1;

        double[] p = { -dx, dx, -dy, dy };
        double[] q = { start.X - box.Left, box.Right - start.X, start.Y - box.Bottom, box.Top - start.Y };

        for (var i = 0; i < 4; i++)
        {
            if (System.Math.Abs(p[i]) < Math.Tolerance.Epsilon)
            {
                if (q[i] < -Math.Tolerance.Epsilon)
                    return null;
            }
            else
            {
                var t = q[i] / p[i];
                if (p[i] < 0)
                    t0 = System.Math.Max(t0, t);
                else
                    t1 = System.Math.Min(t1, t);

                if (t0 > t1)
                    return null;
            }
        }

        var clippedStart = new Vector(start.X + t0 * dx, start.Y + t0 * dy);
        var clippedEnd = new Vector(start.X + t1 * dx, start.Y + t1 * dy);
        return (clippedStart, clippedEnd);
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
    /// Clip perimeter to a region by walking entities, splitting at split line crossings,
    /// and stitching in feature edges. No polygon clipping library needed.
    /// </summary>
    private static List<Entity> ClipPerimeterToRegion(Shape perimeter, Box region,
        List<SplitLine> splitLines, ISplitFeature feature, SplitParameters parameters)
    {
        var boundarySplitLines = GetBoundarySplitLines(region, splitLines);
        var entities = new List<Entity>();
        var splitPoints = new List<(Vector Point, SplitLine Line, bool IsExit)>();

        foreach (var entity in perimeter.Entities)
        {
            ProcessEntity(entity, region, boundarySplitLines, entities, splitPoints);
        }

        if (entities.Count == 0)
            return new List<Entity>();

        InsertFeatureEdges(entities, splitPoints, region, boundarySplitLines, feature, parameters);
        EnsurePerimeterWinding(entities);
        return entities;
    }

    private static void ProcessEntity(Entity entity, Box region,
        List<SplitLine> boundarySplitLines, List<Entity> entities,
        List<(Vector Point, SplitLine Line, bool IsExit)> splitPoints)
    {
        // Find the first boundary split line this entity crosses
        SplitLine crossedLine = null;
        Vector? intersectionPt = null;

        foreach (var sl in boundarySplitLines)
        {
            if (SplitLineIntersect.CrossesSplitLine(entity, sl))
            {
                var pt = SplitLineIntersect.FindIntersection(entity, sl);
                if (pt != null)
                {
                    crossedLine = sl;
                    intersectionPt = pt;
                    break;
                }
            }
        }

        if (crossedLine != null)
        {
            // Entity crosses a split line — split it and keep the half inside the region
            var regionSide = RegionSideOf(region, crossedLine);
            var startPt = GetStartPoint(entity);
            var startSide = SplitLineIntersect.SideOf(startPt, crossedLine);
            var startInRegion = startSide == regionSide || startSide == 0;

            SplitEntityAtPoint(entity, intersectionPt.Value, startInRegion, crossedLine, entities, splitPoints);
        }
        else
        {
            // Entity doesn't cross any boundary split line — check if it's inside the region
            var mid = MidPoint(entity);
            if (region.Contains(mid))
                entities.Add(entity);
        }
    }

    private static void SplitEntityAtPoint(Entity entity, Vector point, bool startInRegion,
        SplitLine crossedLine, List<Entity> entities,
        List<(Vector Point, SplitLine Line, bool IsExit)> splitPoints)
    {
        if (entity is Line line)
        {
            var (first, second) = line.SplitAt(point);
            if (startInRegion)
            {
                if (first != null) entities.Add(first);
                splitPoints.Add((point, crossedLine, true));
            }
            else
            {
                splitPoints.Add((point, crossedLine, false));
                if (second != null) entities.Add(second);
            }
        }
        else if (entity is Arc arc)
        {
            var (first, second) = arc.SplitAt(point);
            if (startInRegion)
            {
                if (first != null) entities.Add(first);
                splitPoints.Add((point, crossedLine, true));
            }
            else
            {
                splitPoints.Add((point, crossedLine, false));
                if (second != null) entities.Add(second);
            }
        }
    }

    /// <summary>
    /// Returns split lines whose position matches a boundary edge of the region.
    /// </summary>
    private static List<SplitLine> GetBoundarySplitLines(Box region, List<SplitLine> splitLines)
    {
        var result = new List<SplitLine>();
        foreach (var sl in splitLines)
        {
            if (sl.Axis == CutOffAxis.Vertical)
            {
                if (System.Math.Abs(sl.Position - region.Left) < OpenNest.Math.Tolerance.Epsilon
                    || System.Math.Abs(sl.Position - region.Right) < OpenNest.Math.Tolerance.Epsilon)
                    result.Add(sl);
            }
            else
            {
                if (System.Math.Abs(sl.Position - region.Bottom) < OpenNest.Math.Tolerance.Epsilon
                    || System.Math.Abs(sl.Position - region.Top) < OpenNest.Math.Tolerance.Epsilon)
                    result.Add(sl);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns -1 or +1 indicating which side of the split line the region center is on.
    /// </summary>
    private static int RegionSideOf(Box region, SplitLine sl)
    {
        return SplitLineIntersect.SideOf(region.Center, sl);
    }

    /// <summary>
    /// Returns the midpoint of an entity. For lines: average of endpoints.
    /// For arcs: point at the mid-angle.
    /// </summary>
    private static Vector MidPoint(Entity entity)
    {
        if (entity is Line line)
            return line.MidPoint;

        if (entity is Arc arc)
        {
            var midAngle = (arc.StartAngle + arc.EndAngle) / 2;
            return new Vector(
                arc.Center.X + arc.Radius * System.Math.Cos(midAngle),
                arc.Center.Y + arc.Radius * System.Math.Sin(midAngle));
        }

        return new Vector(0, 0);
    }

    /// <summary>
    /// Groups split points by split line, pairs exits with entries, and generates feature edges.
    /// </summary>
    private static void InsertFeatureEdges(List<Entity> entities,
        List<(Vector Point, SplitLine Line, bool IsExit)> splitPoints,
        Box region, List<SplitLine> boundarySplitLines,
        ISplitFeature feature, SplitParameters parameters)
    {
        // Group split points by their split line
        var groups = new Dictionary<SplitLine, List<(Vector Point, bool IsExit)>>();
        foreach (var sp in splitPoints)
        {
            if (!groups.ContainsKey(sp.Line))
                groups[sp.Line] = new List<(Vector, bool)>();
            groups[sp.Line].Add((sp.Point, sp.IsExit));
        }

        foreach (var kvp in groups)
        {
            var sl = kvp.Key;
            var points = kvp.Value;

            // Pair each exit with the next entry
            var exits = points.Where(p => p.IsExit).Select(p => p.Point).ToList();
            var entries = points.Where(p => !p.IsExit).Select(p => p.Point).ToList();

            if (exits.Count == 0 || entries.Count == 0)
                continue;

            // For each exit, find the matching entry to form the feature edge span
            // Sort exits and entries by their position along the split line
            var isVertical = sl.Axis == CutOffAxis.Vertical;
            exits = exits.OrderBy(p => isVertical ? p.Y : p.X).ToList();
            entries = entries.OrderBy(p => isVertical ? p.Y : p.X).ToList();

            // Pair them up: each exit with the next entry (or vice versa)
            var pairCount = System.Math.Min(exits.Count, entries.Count);
            for (var i = 0; i < pairCount; i++)
            {
                var exitPt = exits[i];
                var entryPt = entries[i];

                var extentStart = isVertical
                    ? System.Math.Min(exitPt.Y, entryPt.Y)
                    : System.Math.Min(exitPt.X, entryPt.X);
                var extentEnd = isVertical
                    ? System.Math.Max(exitPt.Y, entryPt.Y)
                    : System.Math.Max(exitPt.X, entryPt.X);

                var featureResult = feature.GenerateFeatures(sl, extentStart, extentEnd, parameters);

                var isNegativeSide = RegionSideOf(region, sl) < 0;
                var featureEdge = isNegativeSide ? featureResult.NegativeSideEdge : featureResult.PositiveSideEdge;

                if (featureEdge.Count > 0)
                    featureEdge = AlignFeatureDirection(featureEdge, exitPt, entryPt, sl.Axis);

                entities.AddRange(featureEdge);
            }
        }
    }

    private static List<Entity> AlignFeatureDirection(List<Entity> featureEdge, Vector start, Vector end, CutOffAxis axis)
    {
        var featureStart = GetStartPoint(featureEdge[0]);
        var featureEnd = GetEndPoint(featureEdge[^1]);
        var isVertical = axis == CutOffAxis.Vertical;

        var edgeGoesForward = isVertical ? start.Y < end.Y : start.X < end.X;
        var featureGoesForward = isVertical ? featureStart.Y < featureEnd.Y : featureStart.X < featureEnd.X;

        if (edgeGoesForward != featureGoesForward)
        {
            featureEdge = new List<Entity>(featureEdge);
            featureEdge.Reverse();
            foreach (var e in featureEdge)
                e.Reverse();
        }

        return featureEdge;
    }

    private static void EnsurePerimeterWinding(List<Entity> entities)
    {
        var shape = new Shape();
        shape.Entities.AddRange(entities);
        var poly = shape.ToPolygon();
        if (poly != null && poly.RotationDirection() != RotationType.CW)
            shape.Reverse();

        entities.Clear();
        entities.AddRange(shape.Entities);
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

    /// <summary>
    /// Clip a cutout shape to a region by walking entities, splitting at split line
    /// intersections, keeping portions inside the region, and closing gaps with
    /// straight lines. No polygon clipping library needed.
    /// </summary>
    private static List<Entity> ClipCutoutToRegion(Shape cutout, Box region, List<SplitLine> splitLines)
    {
        var boundarySplitLines = GetBoundarySplitLines(region, splitLines);
        var entities = new List<Entity>();
        var splitPoints = new List<(Vector Point, SplitLine Line, bool IsExit)>();

        foreach (var entity in cutout.Entities)
        {
            ProcessEntity(entity, region, boundarySplitLines, entities, splitPoints);
        }

        if (entities.Count == 0)
            return new List<Entity>();

        // Close gaps with straight lines (connect exit→entry pairs)
        var groups = new Dictionary<SplitLine, List<(Vector Point, bool IsExit)>>();
        foreach (var sp in splitPoints)
        {
            if (!groups.ContainsKey(sp.Line))
                groups[sp.Line] = new List<(Vector, bool)>();
            groups[sp.Line].Add((sp.Point, sp.IsExit));
        }

        foreach (var kvp in groups)
        {
            var sl = kvp.Key;
            var points = kvp.Value;
            var isVertical = sl.Axis == CutOffAxis.Vertical;

            var exits = points.Where(p => p.IsExit).Select(p => p.Point)
                .OrderBy(p => isVertical ? p.Y : p.X).ToList();
            var entries = points.Where(p => !p.IsExit).Select(p => p.Point)
                .OrderBy(p => isVertical ? p.Y : p.X).ToList();

            var pairCount = System.Math.Min(exits.Count, entries.Count);
            for (var i = 0; i < pairCount; i++)
                entities.Add(new Line(exits[i], entries[i]));
        }

        // Ensure CCW winding for cutouts
        var shape = new Shape();
        shape.Entities.AddRange(entities);
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
            Arc a => a.StartPoint(),
            _ => new Vector(0, 0)
        };
    }

    private static Vector GetEndPoint(Entity entity)
    {
        return entity switch
        {
            Line l => l.EndPoint,
            Arc a => a.EndPoint(),
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
