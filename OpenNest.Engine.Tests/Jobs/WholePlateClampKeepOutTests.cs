using System.Collections.Generic;
using System.Threading;
using OpenNest.Clamps;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// The whole-plate nest of the fill-then-pack strategies (Lattice, both Remnants), as the
/// whole-job engines run it, keeps every part off the clamp keep-outs (zone grown by the clamp
/// clearance) in the steps after the fill:
/// <list type="bullet">
/// <item>A leftover even quantity goes in as a best-fit pair in a remnant that treats each
/// keep-out as occupied, so the pair can sit flush on one, then slides left and down. Held at
/// the part spacing, the keep-out read as already inside that spacing and the push carried the
/// pair through the clamp; it must stop the pair where they touch.</item>
/// <item>With no fill parts the leftovers were packed into the whole work area, keep-outs
/// included, since only a fill narrowed the pack to a remnant clear of them.</item>
/// <item>Placement keep-outs stay <see cref="NestTolerances.SpacingSlack"/> outside the
/// validator's even when the clamp clearance is under the slack, so a flush part is not read
/// as entering a zero-clearance zone through the validator's outline rounding.</item>
/// </list>
/// </summary>
public class WholePlateClampKeepOutTests
{
    private const double Edge = 0.25;
    private const double PartSpacing = 0.5;

    public static TheoryData<string> Policies => new() { "Fill", "Vertical Remnant", "Horizontal Remnant" };

    private static PlateFillPolicy Policy(string name) => name switch
    {
        "Vertical Remnant" => PlateFillPolicy.VerticalRemnant,
        "Horizontal Remnant" => PlateFillPolicy.HorizontalRemnant,
        _ => PlateFillPolicy.Fill,
    };

    private static Drawing RectDrawing(double lengthX, double widthY)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(lengthX, 0);
        program.LineTo(lengthX, widthY);
        program.LineTo(0, widthY);
        program.LineTo(0, 0);
        return new Drawing($"rect-{lengthX}-{widthY}", program);
    }

    /// <summary>
    /// 48 (X) by 24 (Y). Clamps every 6 along the front edge leave 4-wide gaps between their
    /// keep-outs (a 1.5-wide zone grown by the 0.25 clearance).
    /// </summary>
    private static (Plate Plate, List<Box> Zones) ClampedPlate()
    {
        var plate = new Plate(new Size(24, 48))
        {
            EdgeSpacing = new Spacing(Edge, Edge),
            PartSpacing = PartSpacing,
        };
        var zones = new List<Box>();
        for (var x = 2.0; x < 46; x += 6)
        {
            var zone = new Box(x, 0, 1.5, 0.625);
            zones.Add(zone);
            plate.ClampZones.Add(new ClampZone($"front-{zones.Count}", zone));
        }
        return (plate, zones);
    }

    private static void AssertNested(Plate plate, List<Box> zones, Drawing drawing, int quantity, string policy)
    {
        var items = new List<NestItem> { new() { Drawing = drawing, Quantity = quantity } };
        var parts = new PlateFiller(plate, Policy(policy)).Nest(items, null, CancellationToken.None);

        Assert.Equal(quantity, parts.Count);
        var clearance = plate.ClampClearance;
        foreach (var part in parts)
            foreach (var zone in zones)
            {
                var keepOut = zone.Offset(clearance);
                var box = part.BoundingBox;
                Assert.False(
                    box.Left < keepOut.Right - 1e-6 && keepOut.Left < box.Right - 1e-6
                        && box.Bottom < keepOut.Top - 1e-6 && keepOut.Bottom < box.Top - 1e-6,
                    $"part box [{box.Left:R}..{box.Right:R} x {box.Bottom:R}..{box.Top:R}] enters the "
                        + $"keep-out [{keepOut.Left:R}..{keepOut.Right:R} x {keepOut.Bottom:R}..{keepOut.Top:R}]");
            }

        var requirements = new Dictionary<Drawing, (string Name, int Quantity)>(ReferenceEqualityComparer.Instance)
        {
            [drawing] = (drawing.Name, quantity),
        };
        Assert.Empty(NestLayoutCheck.Validate(new List<(Plate, List<Part>)> { (plate, parts) }, requirements));
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void LeftoverPairStaysOnTheClampKeepOutsItLandsOn(string policy)
    {
        // Every pair of 5 x 3 rectangles is wider than the 4-wide gaps, so it can only land
        // on top of the keep-outs.
        var (plate, zones) = ClampedPlate();
        AssertNested(plate, zones, RectDrawing(5, 3), 2, policy);
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void PackWithoutFillPartsStaysOffTheClampKeepOuts(string policy)
    {
        // Three 4 x 4 squares are under a tenth of the plate, so nothing fills and all three
        // are packed; the packer's lower-left corner is across the first keep-out.
        var (plate, zones) = ClampedPlate();
        AssertNested(plate, zones, RectDrawing(4, 4), 3, policy);
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void ZeroClearanceZonesKeepTheValidatorsRoundingMargin(string policy)
    {
        // No bottom edge spacing and no rail: the clamp clearance is 0, so the validator's
        // keep-out is the bare zone. The rectangle's program lies below its origin, 3.00006
        // deep, and the validator's outline (on a 1e-4 grid at the part's origin) puts its
        // bottom edge 4e-5 lower than it is. Placed flush on a zone, it read as entering it.
        var (plate, zones) = ClampedPlate();
        plate.EdgeSpacing = new Spacing(0, 0);
        Assert.Equal(0, plate.ClampClearance);

        var program = new Program();
        program.MoveTo(-5, -3.00006);
        program.LineTo(0, -3.00006);
        program.LineTo(0, 0);
        program.LineTo(-5, 0);
        program.LineTo(-5, -3.00006);
        var drawing = new Drawing("below-origin", program);

        AssertNested(plate, zones, drawing, 3, policy);
    }

    [Fact]
    public void PlacementKeepOutsStayTheSlackOutsideTheValidators()
    {
        var (plate, _) = ClampedPlate();
        foreach (var bottomEdge in new[] { 0, NestTolerances.SpacingSlack / 2, 0.25 })
        {
            plate.EdgeSpacing = new Spacing(0, bottomEdge, 0, 0);
            Assert.Equal(bottomEdge, plate.ClampClearance);
            var placement = InteractiveClamps.ClearanceRectangles(plate);
            var validation = InteractiveClamps.ValidationKeepOuts(plate);
            for (var i = 0; i < placement.Count; i++)
            {
                Assert.True(placement[i].Top >= validation[i].Top + NestTolerances.SpacingSlack - 1e-12,
                    $"bottom edge {bottomEdge}: placement top {placement[i].Top:R}, validation top {validation[i].Top:R}");
                Assert.True(placement[i].Left <= validation[i].Left - NestTolerances.SpacingSlack + 1e-12);
            }
        }
    }
}
