using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.Tests.Jobs;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// A reported nest: the README gusset (an 8" right triangle with clipped corners) at 0.3"
/// spacing on a 60 x 120 sheet. Fill tiled a pair whose second gusset sits beside the first,
/// 0.26" higher: in the next column over, each gusset passes 0.04" above the one beside it.
/// Their bounding boxes do not overlap across the slide, so the copy distance never measured
/// that pair and the columns interlocked closer than the spacing.
/// </summary>
public class PatternCopySpacingTests
{
    private const double PartSpacing = 0.3;

    private static Drawing Gusset()
    {
        var program = new Program();
        program.MoveTo(3.7, 2.6);
        program.ArcTo(new Vector(3.7, 2.6), new Vector(2.6, 2.6), RotationType.CCW);
        program.MoveTo(0, 0);
        foreach (var (x, y) in new[] { (8.0, 0.0), (8.0, 1.0), (1.0, 8.0), (0.0, 8.0), (0.0, 0.0) })
            program.LineTo(x, y);
        return new Drawing("Gusset", program);
    }

    private static Part PlacedAt(Drawing drawing, double degrees, double left, double bottom)
    {
        var part = Part.CreateAtOrigin(drawing, Angle.ToRadians(degrees));
        part.Offset(new Vector(left, bottom) - part.BoundingBox.Location);
        part.UpdateBounds();
        return part;
    }

    private static List<string> SpacingViolations(Drawing drawing, List<Part> parts, Size size) =>
        NestLayoutCheck.Validate(
            new() { (new Plate(size) { PartSpacing = PartSpacing }, parts) },
            new Dictionary<Drawing, (string, int)> { [drawing] = ("Gusset", parts.Count) });

    [Theory]
    [InlineData(NestDirection.Vertical)]
    [InlineData(NestDirection.Horizontal)]
    public void TilingKeepsSpacingFromAPartBesideTheNextCopysPath(NestDirection direction)
    {
        var drawing = Gusset();
        var vertical = direction == NestDirection.Vertical;
        var pattern = new Pattern();
        pattern.Parts.Add(PlacedAt(drawing, vertical ? 90 : 270, 0, 0));
        pattern.Parts.Add(vertical
            ? PlacedAt(drawing, 270, 8.3001, 0.2594)
            : PlacedAt(drawing, 90, 0.2594, 8.3001));
        pattern.UpdateBounds();

        var parts = new FillLinear(new Box(0, 0, 60, 60), PartSpacing + 1e-4).Fill(pattern, direction);

        Assert.True(parts.Count > 4, $"Tiled {parts.Count} parts");
        Assert.Empty(SpacingViolations(drawing, parts, new Size(60, 60)));
    }

    /// <summary>
    /// A column pair of D-shapes nested arc against arc, their bounding boxes 0.01 apart: the
    /// next column's flat top passed 0.01 under the upper part's flat bottom.
    /// </summary>
    [Fact]
    public void TilingKeepsSpacingFromAPartJustAboveTheNextCopysPath()
    {
        var drawing = new Drawing("arc", TestDrawingFactory.ArcPart());
        var pattern = new Pattern();
        pattern.Parts.Add(PlacedAt(drawing, 0, 0, 0));
        pattern.Parts.Add(PlacedAt(drawing, 180, 2.7693, 5.01));
        pattern.UpdateBounds();
        var plate = new Plate(new Size(40, 40)) { PartSpacing = 0.5 };
        var requirements = new Dictionary<Drawing, (string, int)> { [drawing] = ("arc", 100) };
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, pattern.Parts) }, requirements));

        var parts = new FillLinear(new Box(0, 0, 40, 40), 0.5).Fill(pattern, NestDirection.Horizontal);

        Assert.True(parts.Count > 4, $"Tiled {parts.Count} parts");
        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) }, requirements));
    }

    /// <summary>The band Auto Nest asks for: 24 gussets in the sheet's 119.4 x 59 work area.</summary>
    [Fact]
    public void QuantityFillOfGussetsInALongBoxKeepsThePartSpacing()
    {
        var jobPart = DrawingJobMapper.FromDrawing("p", Gusset(), 24);
        var drawing = DrawingJobMapper.CreateDrawing(jobPart);
        var geometry = JobPartGeometry.TryRead(jobPart.Geometry)!;
        List<Part> parts;
        try
        {
            parts = PrivatePlateFill.Run(drawing, jobPart.Rotation, PartSpacing + 1e-4, 119.4, 59, 24,
                CancellationToken.None);
        }
        finally
        {
            BestFitCache.Invalidate(drawing);
        }

        Assert.Equal(24, parts.Count);
        var poses = parts
            .Select(p => new NestJobPlacement("p", 0, p.Location.X, p.Location.Y, Angle.NormalizeRad(p.Rotation)))
            .ToArray();
        var close = new List<string>();
        for (var i = 0; i < poses.Length; i++)
        {
            for (var k = i + 1; k < poses.Length; k++)
            {
                if (!NestLayoutCheck.Clears(geometry, poses[i], geometry, poses[k], PartSpacing))
                    close.Add($"{i} at {poses[i].X:F3},{poses[i].Y:F3} and {k} at {poses[k].X:F3},{poses[k].Y:F3}");
            }
        }

        Assert.Empty(close);
    }

    /// <summary>Fill Plate on that sheet, whole and with the quantity set.</summary>
    [Theory]
    [InlineData(0, 154)]
    [InlineData(24, 24)]
    public void FillPlateOfGussetsKeepsThePartSpacing(int quantity, int expected)
    {
        var plate = new Plate(new Size(60, 120))
        {
            PartSpacing = PartSpacing,
            EdgeSpacing = new Spacing(0.3, 0.75, 0.3, 0.25),
        };
        var drawing = Gusset();

        var parts = PlateFillService.FillItem("Fill", plate,
            new NestItem { Drawing = drawing, Quantity = quantity }, plate.WorkArea(), null,
            CancellationToken.None);

        Assert.Empty(NestLayoutCheck.Validate(new() { (plate, parts) },
            new Dictionary<Drawing, (string, int)> { [drawing] = ("Gusset", parts.Count) }));
        Assert.True(parts.Count >= expected, $"Placed {parts.Count} gussets");
    }
}
