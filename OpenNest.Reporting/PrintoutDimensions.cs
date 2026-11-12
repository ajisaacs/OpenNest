using PdfSharp.Drawing;

namespace OpenNest.Reporting;

/// <summary>A dimension label: its box on the page and its text.</summary>
internal sealed record DimensionLabel(XRect Box, string Text);

/// <summary>Where a printout draws its part and the overall dimensions around it, in page points.</summary>
internal sealed record PrintoutDrawingLayout(Fit Fit, IReadOnlyList<(XPoint A, XPoint B)> Lines,
    IReadOnlyList<(XPoint Tip, XPoint From)> Arrows, IReadOnlyList<DimensionLabel> Labels);

/// <summary>
/// Lays out a part with its overall dimensions as the Shape Library draws them: the X extent
/// below the part and the Y extent to its right, each with arrows at both ends and a boxed label
/// centered on its line. The line sits at least <see cref="Gap"/> out from the part's bounds, and
/// far enough that its label box stays <see cref="LabelClearance"/> clear of them. Extension lines
/// start <see cref="ExtensionClearance"/> short of the part's outline (not its bounding box) and
/// run <see cref="ExtensionOverrun"/> past the dimension line. The part and its dimensions are
/// scaled and centered together in the area.
/// </summary>
internal static class PrintoutDimensions
{
    // Page points; the Shape Library's screen sizes in pixels.
    internal const double Gap = 18;
    internal const double LabelClearance = 14;
    internal const double ExtensionOverrun = 5;
    internal const double ExtensionClearance = 6;
    internal const double ArrowLength = 8;
    internal const double ArrowHalfWidth = 3;
    internal const double LabelPadding = 3;
    private const double ExtensionPrecision = 0.25;
    private const int MaxExtensionSteps = 64;

    /// <param name="measure">The size of a label's text in the label font.</param>
    internal static PrintoutDrawingLayout Layout(ReportGeometry geometry, XRect area, string lengthText,
        string widthText, Func<string, XSize> measure)
    {
        var bounds = geometry.Bounds;
        var hasLength = bounds.Length > 0;
        var hasWidth = bounds.Width > 0;
        var lengthLabel = hasLength ? LabelSize(lengthText, measure) : new XSize();
        var widthLabel = hasWidth ? LabelSize(widthText, measure) : new XSize();

        // How far each dimension reaches past the part, independent of scale: the line's gap,
        // then the overrun or half the label straddling the line, whichever is farther.
        var lengthGap = LineGap(lengthLabel.Height);
        var widthGap = LineGap(widthLabel.Width);
        var below = hasLength ? lengthGap + System.Math.Max(ExtensionOverrun, lengthLabel.Height / 2) : 0;
        var right = hasWidth ? widthGap + System.Math.Max(ExtensionOverrun, widthLabel.Width / 2) : 0;
        // A label wider than the part it measures sticks out past the part's ends.
        var lengthSpill = 0.0;
        var widthSpill = 0.0;

        var fit = default(Fit);
        for (var pass = 0; pass < 3; pass++)
        {
            var scale = Scale(bounds, area.Width - right - 2 * lengthSpill, area.Height - below - 2 * widthSpill);
            var groupWidth = bounds.Length * scale + right;
            var groupHeight = bounds.Width * scale + below;
            fit = new Fit(bounds, scale,
                area.X + (area.Width - groupWidth) / 2,
                area.Y + (area.Height - groupHeight) / 2);
            var nextLengthSpill = hasLength ? System.Math.Max(0, (lengthLabel.Width - bounds.Length * scale) / 2) : 0;
            var nextWidthSpill = hasWidth ? System.Math.Max(0, (widthLabel.Height - bounds.Width * scale) / 2) : 0;
            if (nextLengthSpill <= lengthSpill && nextWidthSpill <= widthSpill)
                break;
            lengthSpill = System.Math.Max(lengthSpill, nextLengthSpill);
            widthSpill = System.Math.Max(widthSpill, nextWidthSpill);
        }

        var lines = new List<(XPoint, XPoint)>();
        var arrows = new List<(XPoint, XPoint)>();
        var labels = new List<DimensionLabel>();
        var part = fit.Rect(bounds);
        if (hasLength)
            AddLinear(geometry, fit, part, new ReportPoint(bounds.Left, bounds.Bottom),
                new ReportPoint(bounds.Right, bounds.Bottom), new XVector(0, 1), lengthGap, lengthText, lengthLabel,
                lines, arrows, labels);
        if (hasWidth)
            AddLinear(geometry, fit, part, new ReportPoint(bounds.Right, bounds.Bottom),
                new ReportPoint(bounds.Right, bounds.Top), new XVector(1, 0), widthGap, widthText, widthLabel,
                lines, arrows, labels);
        return new PrintoutDrawingLayout(fit, lines, arrows, labels);
    }

    /// <summary>How far out a dimension line sits for a label <paramref name="across"/> points across it.</summary>
    private static double LineGap(double across) => System.Math.Max(Gap, across / 2 + LabelClearance);

    private static double Scale(ReportBounds bounds, double width, double height)
    {
        var scales = new List<double>(2);
        if (bounds.Length > 0)
            scales.Add(width / bounds.Length);
        if (bounds.Width > 0)
            scales.Add(height / bounds.Width);
        var scale = scales.Count == 0 ? 1 : scales.Min();
        if (!double.IsFinite(scale) || scale <= 0)
            throw new InvalidOperationException("The part and its dimensions cannot be fitted to the page.");
        return scale;
    }

    private static XSize LabelSize(string text, Func<string, XSize> measure)
    {
        var size = measure(text);
        return new XSize(size.Width + 2 * LabelPadding, size.Height + LabelPadding);
    }

    /// <param name="side">Page-space unit vector from the part toward the dimension line.</param>
    private static void AddLinear(ReportGeometry geometry, Fit fit, XRect part, ReportPoint start, ReportPoint end,
        XVector side, double gap, string text, XSize labelSize, List<(XPoint, XPoint)> lines,
        List<(XPoint, XPoint)> arrows, List<DimensionLabel> labels)
    {
        var a = fit.Point(start);
        var b = fit.Point(end);
        var extreme = new[] { part.TopLeft, part.TopRight, part.BottomLeft, part.BottomRight }.Max(c => Dot(c, side));
        var reachA = extreme - Dot(a, side) + gap;
        var reachB = extreme - Dot(b, side) + gap;
        var lineA = a + side * reachA;
        var lineB = b + side * reachB;
        var extent = System.Math.Abs(side.X) > System.Math.Abs(side.Y) ? part.Width : part.Height;
        lines.Add((a + side * ExtensionStart(geometry, fit, start, side, reachA, extent), lineA + side * ExtensionOverrun));
        lines.Add((b + side * ExtensionStart(geometry, fit, end, side, reachB, extent), lineB + side * ExtensionOverrun));
        lines.Add((lineA, lineB));
        arrows.Add((lineA, lineB));
        arrows.Add((lineB, lineA));
        var center = new XPoint((lineA.X + lineB.X) / 2, (lineA.Y + lineB.Y) / 2);
        labels.Add(new DimensionLabel(new XRect(center.X - labelSize.Width / 2, center.Y - labelSize.Height / 2,
            labelSize.Width, labelSize.Height), text));
    }

    /// <summary>
    /// How far from the measured point (in points along <paramref name="side"/>; negative past
    /// it, toward the part) its extension line starts: walking in from the dimension line,
    /// <paramref name="reach"/> points out, by the distance known to be free at each step until
    /// it comes <see cref="ExtensionClearance"/> from the outline. A measured point off the
    /// outline, such as a bounding-box corner beside a round part, is passed; the walk ends at
    /// the part's far side at the latest.
    /// </summary>
    private static double ExtensionStart(ReportGeometry geometry, Fit fit, ReportPoint point, XVector side,
        double reach, double extent)
    {
        // Page Y points down; the model's points up.
        var modelSide = new XVector(side.X, -side.Y);
        var limit = reach + extent;
        var travelled = 0.0;
        for (var i = 0; i < MaxExtensionSteps && travelled < limit; i++)
        {
            var along = (reach - travelled) / fit.Scale;
            var at = new ReportPoint(point.X + modelSide.X * along, point.Y + modelSide.Y * along);
            var free = Distance(geometry, at) * fit.Scale - ExtensionClearance;
            if (free <= ExtensionPrecision)
                break;
            travelled += free;
        }
        return reach - System.Math.Min(travelled, limit);
    }

    /// <summary>Distance from <paramref name="point"/> to the nearest line or arc of the part.</summary>
    internal static double Distance(ReportGeometry geometry, ReportPoint point)
    {
        var nearest = double.MaxValue;
        foreach (var contour in geometry.Contours)
        {
            foreach (var segment in contour.Segments)
                nearest = System.Math.Min(nearest, Distance(segment, point));
        }
        return nearest;
    }

    private static double Distance(ReportSegment segment, ReportPoint p)
    {
        if (segment.Center is not { } c)
            return SegmentDistance(segment.Start, segment.End, p);

        var dx = p.X - c.X;
        var dy = p.Y - c.Y;
        var fromCenter = System.Math.Sqrt(dx * dx + dy * dy);
        if (System.Math.Abs(segment.SweepAngle) >= 2 * System.Math.PI - 1e-9 || fromCenter == 0)
            return System.Math.Abs(fromCenter - segment.Radius);

        // Within the swept angles the nearest point is on the arc; otherwise it is an end.
        var turn = segment.SweepAngle > 0
            ? Normalize(System.Math.Atan2(dy, dx) - segment.StartAngle)
            : Normalize(segment.StartAngle - System.Math.Atan2(dy, dx));
        return turn <= System.Math.Abs(segment.SweepAngle)
            ? System.Math.Abs(fromCenter - segment.Radius)
            : System.Math.Min(PointDistance(segment.Start, p), PointDistance(segment.End, p));
    }

    private static double Normalize(double angle)
    {
        var turn = angle % (2 * System.Math.PI);
        return turn < 0 ? turn + 2 * System.Math.PI : turn;
    }

    private static double SegmentDistance(ReportPoint a, ReportPoint b, ReportPoint p)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lengthSquared = abx * abx + aby * aby;
        var t = lengthSquared == 0 ? 0 : System.Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lengthSquared, 0, 1);
        return PointDistance(new ReportPoint(a.X + abx * t, a.Y + aby * t), p);
    }

    private static double PointDistance(ReportPoint a, ReportPoint b) =>
        System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Dot(XPoint p, XVector d) => p.X * d.X + p.Y * d.Y;
}
