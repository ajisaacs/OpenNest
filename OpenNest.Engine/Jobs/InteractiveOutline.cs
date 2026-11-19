using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// Interactive-path walls of an outlined sheet (<see cref="Plate.Outline"/>): the part of the
/// plate's work area that is not usable material (a missing corner, an obstacle, each grown by
/// the outline's edge spacing), as static obstacles for the directional pushes. The rectangular
/// work area stays the pushes' own box test; these walls add only what the outline takes away
/// from it, so a rectangular sheet has none and its pushes are unchanged.
/// <para>
/// A moving part is measured as the edge review measures it against an outline: the
/// circumscribed perimeter (<see cref="SpacingOutline"/>) of its clean drawing material, without
/// leads, so a push that stops at a wall rests exactly where that review still passes. A box
/// push stops <see cref="BoxMargin"/> short, so the perimeter of the part in that box is still
/// clear of the wall. A part that already lies in a wall, deeper than the review's slack, is not
/// held by it, like a part already overlapping another: it can be pushed back out.
/// </para>
/// </summary>
internal sealed class InteractiveOutline
{
    /// <summary>How far inside the work area the walls are clipped. The usable area and the work
    /// area meet along every straight run of the sheet's own box; on Clipper's grid they can
    /// differ by a unit there, and a sliver that thin would sit under every part resting on the
    /// plate edge. The box test already holds that edge.</summary>
    private static readonly double EdgeClip = 2 * System.Math.Pow(10, -ClipperBridge.Precision);

    /// <summary>How far a part's circumscribed perimeter can reach past its bounding box: the
    /// chord tolerance, and the grid the perimeter is built on. A box resting on a wall without
    /// it leaves the perimeter inside the wall, where the next geometry push would not be held.</summary>
    private static readonly double BoxMargin = SpacingOutline.ChordTolerance + EdgeClip;

    private static readonly InteractiveOutline None = new(new List<Wall>());

    private readonly List<Wall> walls;

    private InteractiveOutline(List<Wall> walls)
    {
        this.walls = walls;
    }

    /// <summary>True when the plate has no outline wall: every push is as it was.</summary>
    public bool IsEmpty => walls.Count == 0;

    /// <summary>
    /// The plate's walls in the parts' placement frame; none for a rectangular sheet. The
    /// usable area is the outline less its obstacles, eroded by the largest edge spacing, as
    /// the layout check and the Irregular engine take it.
    /// </summary>
    public static InteractiveOutline Of(Plate plate)
    {
        var outline = plate?.Outline;
        if (outline == null)
            return None;

        var sheet = plate.BoundingBox(includeParts: false);
        var edges = plate.EdgeSpacing;
        var inset = System.Math.Max(0, System.Math.Max(System.Math.Max(edges.Left, edges.Right),
            System.Math.Max(edges.Top, edges.Bottom)));
        var usable = outline.Usable(inset, new Vector(sheet.X, sheet.Y));

        var work = plate.WorkArea();
        var left = work.Left + EdgeClip;
        var bottom = work.Bottom + EdgeClip;
        var right = work.Right - EdgeClip;
        var top = work.Top - EdgeClip;
        if (!(right > left) || !(top > bottom))
            return None;

        var frame = new PathD
        {
            new PointD(left, bottom), new PointD(right, bottom), new PointD(right, top), new PointD(left, top),
        };
        var clipper = new ClipperD(ClipperBridge.Precision);
        clipper.AddSubject(new PathsD { frame });
        clipper.AddClip(usable);
        var tree = new PolyTreeD();
        clipper.Execute(ClipType.Difference, FillRule.NonZero, tree);

        var walls = new List<Wall>();
        AddWalls(tree, walls);
        return walls.Count == 0 ? None : new InteractiveOutline(walls);
    }

    /// <summary>
    /// The shorter of <paramref name="distance"/> and how far <paramref name="moving"/> travels
    /// along <paramref name="direction"/> before its circumscribed perimeter meets a wall.
    /// </summary>
    public double Travel(Part moving, Vector direction, double distance)
    {
        if (walls.Count == 0)
            return distance;
        var reach = moving.BoundingBox.Offset(BoxMargin);
        return Travel(reach, () => PerimeterLines(moving), direction, distance);
    }

    /// <summary>As <see cref="Travel(Part, Vector, double)"/> for a part's bounding box: the
    /// box pushes move a part as its box, which stops <see cref="BoxMargin"/> short of a wall.</summary>
    public double Travel(Box moving, Vector direction, double distance)
    {
        if (walls.Count == 0)
            return distance;
        var grown = moving.Offset(BoxMargin);
        return Travel(grown, () => BoxLines(grown), direction, distance);
    }

    private double Travel(Box reach, Func<List<Entity>> movingLines, Vector direction, double distance)
    {
        var opposite = -direction;
        List<Entity> lines = null;
        PathsD region = null;

        foreach (var wall in walls)
        {
            // A wall wholly behind the part can only fall further behind.
            if (SpatialQuery.DirectionalGap(reach, wall.Bounds, opposite) >= -OpenNest.Math.Tolerance.Epsilon)
                continue;
            if (SpatialQuery.DirectionalGap(reach, wall.Bounds, direction) >= distance)
                continue;
            if (!SpatialQuery.PerpendicularOverlap(reach, wall.Bounds, direction))
                continue;

            lines ??= movingLines();
            if (lines.Count == 0)
                return distance;

            // Already in this wall: the slide kernel answers for boundaries that start apart.
            region ??= new PathsD { new PathD(lines.Select(line => ((Line)line).StartPoint).Select(p => new PointD(p.X, p.Y))) };
            if (wall.Core.Count > 0 && Clipper.Intersect(region, wall.Core, FillRule.NonZero, ClipperBridge.Precision)
                    .Sum(path => System.Math.Abs(Clipper.Area(path))) > 0)
                continue;

            var d = SpatialQuery.DirectionalDistance(lines, wall.Entities, direction);
            if (d < distance)
                distance = d;
        }

        return distance;
    }

    /// <summary>One wall per outer loop with the holes directly inside it, the shape of a part
    /// with cutouts; material inside a hole (an obstacle on an island) is a wall of its own.</summary>
    private static void AddWalls(PolyPathD node, List<Wall> walls)
    {
        for (var i = 0; i < node.Count; i++)
        {
            var outer = node[i];
            var entities = new List<Entity>();
            var paths = new PathsD { outer.Polygon };
            AddLoop(outer.Polygon, entities);

            for (var k = 0; k < outer.Count; k++)
            {
                var hole = outer[k];
                paths.Add(hole.Polygon);
                AddLoop(hole.Polygon, entities);
                AddWalls(hole, walls);
            }

            if (entities.Count < 3)
                continue;
            var core = Clipper.InflatePaths(paths, -SpacingOutline.SpacingSlack, JoinType.Miter, EndType.Polygon,
                2.0, ClipperBridge.Precision);
            core.RemoveAll(path => path.Count < 3);
            walls.Add(new Wall(entities, Bounds(outer.Polygon), core));
        }
    }

    private static void AddLoop(PathD path, List<Entity> entities)
    {
        if (path == null || path.Count < 3)
            return;
        for (var i = 0; i < path.Count; i++)
        {
            var a = path[i];
            var b = path[(i + 1) % path.Count];
            entities.Add(new Line(new Vector(a.x, a.y), new Vector(b.x, b.y)));
        }
    }

    private static Box Bounds(PathD path)
    {
        var box = Clipper.GetBounds(path); // RectD top is the minimum Y.
        return new Box(box.left, box.top, box.Width, box.Height);
    }

    /// <summary>The part's material perimeter as the edge review takes it: the drawing's clean
    /// material at the part's pose (no leads, seam overcut trimmed), arcs circumscribed at the
    /// spacing outline's chord tolerance. Empty without a closed perimeter.</summary>
    private static List<Entity> PerimeterLines(Part part)
    {
        var lines = new List<Entity>();
        ShapeProfile profile;
        try
        {
            profile = Diagnostics.PartSpacingReview.Profile(part, new Dictionary<(CNC.Program, long), ShapeProfile>());
        }
        catch (ArgumentException)
        {
            return lines;
        }

        var outline = profile == null ? null : SpacingOutline.Build(profile, part.Location, 0);
        if (outline?.Perimeter != null)
            AddLoop(ClipperBridge.ToPath(outline.Perimeter, positive: true), lines);
        return lines;
    }

    private static List<Entity> BoxLines(Box box)
    {
        var lines = new List<Entity>(4);
        AddLoop(new PathD
        {
            new PointD(box.Left, box.Bottom), new PointD(box.Right, box.Bottom),
            new PointD(box.Right, box.Top), new PointD(box.Left, box.Top),
        }, lines);
        return lines;
    }

    /// <summary><see cref="Core"/> is the wall less the edge review's slack on every side: a
    /// part resting on the wall never reaches it, a part lying in the wall does.</summary>
    private sealed record Wall(List<Entity> Entities, Box Bounds, PathsD Core);
}
