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

        // Polygonize cutouts once. Used for trimming feature edges (so cut lines
        // don't travel through a cutout interior) and for hole/containment tests
        // in the final component-assembly pass.
        var cutoutPolygons = profile.Cutouts
            .Select(c => c.ToPolygon())
            .Where(p => p != null)
            .ToList();

        var results = new List<Drawing>();
        var pieceIndex = 1;

        foreach (var region in regions)
        {
            var pieceEntities = ClipPerimeterToRegion(perimeter, region, sortedLines, feature, parameters, cutoutPolygons);
            if (pieceEntities.Count == 0)
                continue;

            var cutoutEntities = CollectCutouts(profile.Cutouts, region, sortedLines);

            var allEntities = new List<Entity>();
            allEntities.AddRange(pieceEntities);
            allEntities.AddRange(cutoutEntities);

            // A single region may yield multiple physically-disjoint pieces when an
            // interior cutout spans across it. Group the region's entities into
            // connected closed loops, nest holes by containment, and emit one
            // Drawing per outer loop (with its contained holes).
            foreach (var pieceOfRegion in AssemblePieces(allEntities))
            {
                var piece = BuildPieceDrawing(drawing, pieceOfRegion, pieceIndex, region);
                results.Add(piece);
                pieceIndex++;
            }
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
        List<SplitLine> splitLines, ISplitFeature feature, SplitParameters parameters,
        List<Polygon> cutoutPolygons)
    {
        var boundarySplitLines = GetBoundarySplitLines(region, splitLines);
        var entities = new List<Entity>();

        foreach (var entity in perimeter.Entities)
            ProcessEntity(entity, region, entities);

        if (entities.Count == 0)
            return new List<Entity>();

        InsertFeatureEdges(entities, region, boundarySplitLines, feature, parameters, cutoutPolygons);
        // Winding is handled later in AssemblePieces, once connected components
        // are known. At this stage the piece may still be multiple disjoint loops.
        return entities;
    }

    private static void ProcessEntity(Entity entity, Box region, List<Entity> entities)
    {
        if (entity is Line line)
        {
            var clipped = ClipLineToBox(line.StartPoint, line.EndPoint, region);
            if (clipped == null) return;
            if (clipped.Value.Start.DistanceTo(clipped.Value.End) < Math.Tolerance.Epsilon) return;
            entities.Add(new Line(clipped.Value.Start, clipped.Value.End));
            return;
        }

        if (entity is Arc arc)
        {
            foreach (var sub in ClipArcToRegion(arc, region))
                entities.Add(sub);
            return;
        }
    }

    /// <summary>
    /// Clips an arc against the four edges of a region box. Returns the sub-arcs
    /// whose midpoints lie inside the region. Uses line-arc intersection to find
    /// split points, then iteratively bisects the arc at each crossing.
    /// </summary>
    private static List<Arc> ClipArcToRegion(Arc arc, Box region)
    {
        var edges = new[]
        {
            new Line(new Vector(region.Left, region.Bottom), new Vector(region.Right, region.Bottom)),
            new Line(new Vector(region.Right, region.Bottom), new Vector(region.Right, region.Top)),
            new Line(new Vector(region.Right, region.Top), new Vector(region.Left, region.Top)),
            new Line(new Vector(region.Left, region.Top), new Vector(region.Left, region.Bottom))
        };

        var arcs = new List<Arc> { arc };

        foreach (var edge in edges)
        {
            var next = new List<Arc>();
            foreach (var a in arcs)
            {
                if (!Intersect.Intersects(a, edge, out var pts) || pts.Count == 0)
                {
                    next.Add(a);
                    continue;
                }

                // Split the arc at each intersection that actually lies on one of
                // the working sub-arcs. Prior splits may make some original hits
                // moot for the sub-arc that now holds them.
                var working = new List<Arc> { a };
                foreach (var pt in pts)
                {
                    var replaced = new List<Arc>();
                    foreach (var w in working)
                    {
                        var onArc = OpenNest.Math.Angle.IsBetweenRad(
                            w.Center.AngleTo(pt), w.StartAngle, w.EndAngle, w.IsReversed);
                        if (!onArc)
                        {
                            replaced.Add(w);
                            continue;
                        }

                        var (first, second) = w.SplitAt(pt);
                        if (first != null && first.SweepAngle() > Math.Tolerance.Epsilon) replaced.Add(first);
                        if (second != null && second.SweepAngle() > Math.Tolerance.Epsilon) replaced.Add(second);
                    }
                    working = replaced;
                }
                next.AddRange(working);
            }
            arcs = next;
        }

        var result = new List<Arc>();
        foreach (var a in arcs)
        {
            if (region.Contains(a.MidPoint()))
                result.Add(a);
        }
        return result;
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
    /// For each boundary split line of the region, generates a feature edge that
    /// spans the full region boundary along that split line and trims it against
    /// interior cutouts. This produces one (or zero) feature edge per contiguous
    /// material interval on the boundary, handling corner regions (one perimeter
    /// crossing), spanning cutouts (two holes puncturing the line), and
    /// normal mid-part splits uniformly.
    /// </summary>
    private static void InsertFeatureEdges(List<Entity> entities,
        Box region, List<SplitLine> boundarySplitLines,
        ISplitFeature feature, SplitParameters parameters,
        List<Polygon> cutoutPolygons)
    {
        foreach (var sl in boundarySplitLines)
        {
            var isVertical = sl.Axis == CutOffAxis.Vertical;
            var extentStart = isVertical ? region.Bottom : region.Left;
            var extentEnd = isVertical ? region.Top : region.Right;

            if (extentEnd - extentStart < Math.Tolerance.Epsilon)
                continue;

            var featureResult = feature.GenerateFeatures(sl, extentStart, extentEnd, parameters);
            var isNegativeSide = RegionSideOf(region, sl) < 0;
            var featureEdge = isNegativeSide ? featureResult.NegativeSideEdge : featureResult.PositiveSideEdge;

            // Trim any line segments that cross a cutout — cut lines must never
            // travel through a hole.
            featureEdge = TrimFeatureEdgeAgainstCutouts(featureEdge, cutoutPolygons);

            entities.AddRange(featureEdge);
        }
    }

    /// <summary>
    /// Subtracts any portions of line entities in <paramref name="featureEdge"/> that
    /// lie inside any of the supplied cutout polygons. Non-line entities (arcs) are
    /// passed through unchanged; a tighter fix for arcs in feature edges (weld-gap
    /// tabs, spike-groove) can be added later if a test demands it.
    /// </summary>
    private static List<Entity> TrimFeatureEdgeAgainstCutouts(List<Entity> featureEdge, List<Polygon> cutoutPolygons)
    {
        if (cutoutPolygons.Count == 0 || featureEdge.Count == 0)
            return featureEdge;

        var result = new List<Entity>();
        foreach (var entity in featureEdge)
        {
            if (entity is Line line)
                result.AddRange(SubtractCutoutsFromLine(line, cutoutPolygons));
            else
                result.Add(entity);
        }
        return result;
    }

    /// <summary>
    /// Returns the sub-segments of <paramref name="line"/> that lie outside every
    /// cutout polygon. Handles the common axis-aligned feature-edge case exactly.
    /// </summary>
    private static List<Line> SubtractCutoutsFromLine(Line line, List<Polygon> cutoutPolygons)
    {
        // Collect parameter values t in [0,1] where the line crosses any cutout edge.
        var ts = new List<double> { 0.0, 1.0 };
        foreach (var poly in cutoutPolygons)
        {
            var polyLines = poly.ToLines();
            foreach (var edge in polyLines)
            {
                if (TryIntersectSegments(line.StartPoint, line.EndPoint, edge.StartPoint, edge.EndPoint, out var t))
                {
                    if (t > Math.Tolerance.Epsilon && t < 1.0 - Math.Tolerance.Epsilon)
                        ts.Add(t);
                }
            }
        }

        ts.Sort();

        var segments = new List<Line>();
        for (var i = 0; i < ts.Count - 1; i++)
        {
            var t0 = ts[i];
            var t1 = ts[i + 1];
            if (t1 - t0 < Math.Tolerance.Epsilon) continue;

            var tMid = (t0 + t1) * 0.5;
            var mid = new Vector(
                line.StartPoint.X + (line.EndPoint.X - line.StartPoint.X) * tMid,
                line.StartPoint.Y + (line.EndPoint.Y - line.StartPoint.Y) * tMid);

            var insideCutout = false;
            foreach (var poly in cutoutPolygons)
            {
                if (poly.ContainsPoint(mid))
                {
                    insideCutout = true;
                    break;
                }
            }
            if (insideCutout) continue;

            var p0 = new Vector(
                line.StartPoint.X + (line.EndPoint.X - line.StartPoint.X) * t0,
                line.StartPoint.Y + (line.EndPoint.Y - line.StartPoint.Y) * t0);
            var p1 = new Vector(
                line.StartPoint.X + (line.EndPoint.X - line.StartPoint.X) * t1,
                line.StartPoint.Y + (line.EndPoint.Y - line.StartPoint.Y) * t1);

            segments.Add(new Line(p0, p1));
        }

        return segments;
    }

    /// <summary>
    /// Segment-segment intersection. On hit, returns the parameter t along segment AB
    /// (0 = a0, 1 = a1) via <paramref name="tOnA"/>.
    /// </summary>
    private static bool TryIntersectSegments(Vector a0, Vector a1, Vector b0, Vector b1, out double tOnA)
    {
        tOnA = 0;
        var rx = a1.X - a0.X;
        var ry = a1.Y - a0.Y;
        var sx = b1.X - b0.X;
        var sy = b1.Y - b0.Y;

        var denom = rx * sy - ry * sx;
        if (System.Math.Abs(denom) < Math.Tolerance.Epsilon)
            return false;

        var dx = b0.X - a0.X;
        var dy = b0.Y - a0.Y;
        var t = (dx * sy - dy * sx) / denom;
        var u = (dx * ry - dy * rx) / denom;

        if (t < -Math.Tolerance.Epsilon || t > 1 + Math.Tolerance.Epsilon) return false;
        if (u < -Math.Tolerance.Epsilon || u > 1 + Math.Tolerance.Epsilon) return false;

        tOnA = t;
        return true;
    }

    private static bool IsCutoutInRegion(Shape cutout, Box region)
    {
        if (cutout.Entities.Count == 0) return false;
        var bb = cutout.BoundingBox;
        // Fully contained iff the cutout's bounding box fits inside the region.
        return bb.Left >= region.Left - Math.Tolerance.Epsilon
            && bb.Right <= region.Right + Math.Tolerance.Epsilon
            && bb.Bottom >= region.Bottom - Math.Tolerance.Epsilon
            && bb.Top <= region.Top + Math.Tolerance.Epsilon;
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
    /// Clip a cutout shape to a region by walking entities and splitting at split-line
    /// crossings. Only returns the cutout-edge fragments that lie inside the region —
    /// it deliberately does NOT emit synthetic closing lines at the region boundary.
    ///
    /// Rationale: a closing line on the region boundary would overlap the split-line
    /// feature edge and reintroduce a cut through the cutout interior. The feature
    /// edge (trimmed against cutouts in <see cref="InsertFeatureEdges"/>) and these
    /// cutout fragments are stitched together later by <see cref="AssemblePieces"/>
    /// using endpoint connectivity, which produces the correct closed loops — one
    /// loop per physically-connected strip of material.
    /// </summary>
    private static List<Entity> ClipCutoutToRegion(Shape cutout, Box region, List<SplitLine> splitLines)
    {
        var entities = new List<Entity>();
        foreach (var entity in cutout.Entities)
            ProcessEntity(entity, region, entities);
        return entities;
    }

    /// <summary>
    /// Groups a region's entities into closed components and nests holes inside
    /// outer loops by point-in-polygon containment. Returns one entity list per
    /// output <see cref="Drawing"/> — outer loop first, then its contained holes.
    /// Each outer loop is normalized to CW winding and each hole to CCW.
    /// </summary>
    private static List<List<Entity>> AssemblePieces(List<Entity> entities)
    {
        var pieces = new List<List<Entity>>();
        if (entities.Count == 0) return pieces;

        var shapes = ShapeBuilder.GetShapes(entities);
        if (shapes.Count == 0) return pieces;

        // Polygonize every shape once so we can run containment tests.
        var polygons = new List<Polygon>(shapes.Count);
        foreach (var s in shapes)
            polygons.Add(s.ToPolygon());

        // Classify each shape as outer or hole using nesting by containment.
        // Shape A is contained in shape B iff A's bounding box is strictly inside
        // B's bounding box AND a representative vertex of A lies inside B's polygon.
        // The bbox pre-check avoids the ambiguity of bbox-center tests when two
        // shapes share a center (e.g., an outer half and a centered cutout).
        var isHole = new bool[shapes.Count];
        for (var i = 0; i < shapes.Count; i++)
        {
            var bbA = shapes[i].BoundingBox;
            var repA = FirstVertexOf(shapes[i]);

            for (var j = 0; j < shapes.Count; j++)
            {
                if (i == j) continue;
                if (polygons[j] == null) continue;
                if (polygons[j].Vertices.Count < 3) continue;

                var bbB = shapes[j].BoundingBox;
                if (!BoxContainsBox(bbB, bbA)) continue;
                if (!polygons[j].ContainsPoint(repA)) continue;

                isHole[i] = true;
                break;
            }
        }

        // For each outer, attach the holes that fall inside it.
        for (var i = 0; i < shapes.Count; i++)
        {
            if (isHole[i]) continue;

            var outer = shapes[i];
            var outerPoly = polygons[i];

            // Enforce perimeter winding = CW.
            if (outerPoly != null && outerPoly.Vertices.Count >= 3
                && outerPoly.RotationDirection() != RotationType.CW)
                outer.Reverse();

            var piece = new List<Entity>();
            piece.AddRange(outer.Entities);

            for (var j = 0; j < shapes.Count; j++)
            {
                if (!isHole[j]) continue;
                if (polygons[i] == null || polygons[i].Vertices.Count < 3) continue;

                var bbJ = shapes[j].BoundingBox;
                if (!BoxContainsBox(shapes[i].BoundingBox, bbJ)) continue;

                var rep = FirstVertexOf(shapes[j]);
                if (!polygons[i].ContainsPoint(rep)) continue;

                var hole = shapes[j];
                var holePoly = polygons[j];
                if (holePoly != null && holePoly.Vertices.Count >= 3
                    && holePoly.RotationDirection() != RotationType.CCW)
                    hole.Reverse();

                piece.AddRange(hole.Entities);
            }

            pieces.Add(piece);
        }

        return pieces;
    }

    /// <summary>
    /// Returns the first vertex of a shape (start point of its first entity). Used as
    /// a representative for containment testing: if bbox pre-check says the whole
    /// shape is inside another, testing one vertex is sufficient to confirm.
    /// </summary>
    private static Vector FirstVertexOf(Shape shape)
    {
        if (shape.Entities.Count == 0)
            return new Vector(0, 0);
        return GetStartPoint(shape.Entities[0]);
    }

    /// <summary>
    /// True iff box <paramref name="inner"/> is entirely inside box
    /// <paramref name="outer"/> (tolerant comparison).
    /// </summary>
    private static bool BoxContainsBox(Box outer, Box inner)
    {
        var eps = Math.Tolerance.Epsilon;
        return inner.Left >= outer.Left - eps
            && inner.Right <= outer.Right + eps
            && inner.Bottom >= outer.Bottom - eps
            && inner.Top <= outer.Top + eps;
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
