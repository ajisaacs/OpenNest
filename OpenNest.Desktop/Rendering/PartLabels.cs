using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Rendering;

/// <summary>
/// Where the plate view writes a part's number: the pole of inaccessibility of the drawing's
/// material, or the middle of a cut-off segment's own cut. A port of the WinForms
/// <c>LayoutPart</c> label rules.
/// </summary>
public static class PartLabels
{
    /// <summary>The search budget that keeps any shape from stalling a paint.</summary>
    public const int SearchCells = 20000;

    /// <summary>The drawing's label point in its program frame.</summary>
    public static Vector LabelPoint(Drawing drawing, int maxCells = SearchCells)
    {
        var entities = ConvertProgram.ToGeometry(drawing.Program);
        var material = entities.Where(e => SpecialLayers.IsMaterial(e.Layer)).ToList();
        var shapes = ShapeBuilder.GetShapes(material);

        if (shapes.Count == 0)
        {
            var bbox = drawing.Program.BoundingBox();
            return new Vector(bbox.Location.X + bbox.Length / 2, bbox.Location.Y + bbox.Width / 2);
        }

        var profile = ShapeProfile.FromShapes(shapes);
        var outer = profile.Perimeter.ToPolygonWithTolerance(0.1);

        List<Polygon> holes = null;
        if (profile.Cutouts.Count > 0)
        {
            holes = new List<Polygon>();
            foreach (var cutout in profile.Cutouts)
                holes.Add(cutout.ToPolygonWithTolerance(0.1));
        }

        return PolyLabel.Find(outer, holes, PolyLabel.ThicknessPrecision(outer, holes), maxCells);
    }

    /// <summary>
    /// A drawing label point (from <see cref="LabelPoint"/>) placed with <paramref name="part"/>. The
    /// point already carries the drawing program's own rotation, so only the part's rotation past
    /// it is applied.
    /// </summary>
    public static Vector Place(Part part, Vector drawingLabelPoint) =>
        drawingLabelPoint.Rotate(part.Rotation - part.BaseDrawing.Program.Rotation) + part.Location;

    /// <summary>
    /// The middle of a cut-off segment part's own cutting moves, in plate coordinates; null when
    /// the part is not an absolute cut-off segment. Every segment shares the cut-off drawing, so
    /// labelling from the drawing would stack all the numbers on one point.
    /// </summary>
    public static Vector? CutOffSegmentPoint(Part part)
    {
        var program = part.Program;
        if (!part.BaseDrawing.IsCutOff || program?.Mode != Mode.Absolute)
            return null;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        Vector? position = null;
        foreach (var code in program.Codes)
        {
            if (code is not Motion motion)
                continue;
            if (motion is not RapidMove && position is { } start)
            {
                foreach (var point in new[] { start, motion.EndPoint })
                {
                    minX = System.Math.Min(minX, point.X);
                    minY = System.Math.Min(minY, point.Y);
                    maxX = System.Math.Max(maxX, point.X);
                    maxY = System.Math.Max(maxY, point.Y);
                }
            }
            position = motion.EndPoint;
        }

        if (minX > maxX)
            return null;
        return new Vector((minX + maxX) / 2, (minY + maxY) / 2) + part.Location;
    }
}
