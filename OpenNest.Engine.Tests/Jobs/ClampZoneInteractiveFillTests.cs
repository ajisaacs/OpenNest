using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Slice 4 (C1): the interactive single-plate surface - PlateFillService strategies
/// (Fill/Strip/Vertical+Horizontal Remnant), PackArea and whole-plate Nest - never
/// proposes a part inside a clamp keep-out inflated by the plate's bottom-edge
/// spacing, while material outside the zones stays usable. Oracles are analytic:
/// strict positive-area overlap against independently inflated zone rectangles.
/// </summary>
public class ClampZoneInteractiveFillTests
{
    // Plate: 48 (X) by 24 (Y), uniform 0.25 edge spacing -> bottom clearance 0.25.
    // Two 1.5 x 0.625 clamps on the front edge at X 10 and X 20.
    private const double Edge = 0.25;
    private const double PartSpacing = 0.5;

    private static readonly Box ZoneA = new(10, 0, 1.5, 0.625);
    private static readonly Box ZoneB = new(20, 0, 1.5, 0.625);

    private static Plate MakePlate(params ClampZone[] zones)
    {
        var plate = new Plate(new Size(24, 48));
        plate.EdgeSpacing = new Spacing(Edge, Edge);
        plate.PartSpacing = PartSpacing;
        foreach (var zone in zones)
            plate.ClampZones.Add(zone);
        return plate;
    }

    private static Program RectProgram(double lengthX, double widthY)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(lengthX, 0);
        program.LineTo(lengthX, widthY);
        program.LineTo(0, widthY);
        program.LineTo(0, 0);
        return program;
    }

    private static Drawing RectDrawing(double lengthX, double widthY) =>
        new($"rect-{lengthX}-{widthY}", RectProgram(lengthX, widthY));

    private static Box Inflated(Box zone, double clearance) =>
        new(zone.X - clearance, zone.Y - clearance, zone.Length + 2 * clearance, zone.Width + 2 * clearance);

    /// <summary>Strict positive-area overlap: touching edges (the admitted closure) are clear.</summary>
    private static bool Overlaps(Box a, Box b) =>
        a.Left < b.Right - 1e-6 && b.Left < a.Right - 1e-6 && a.Bottom < b.Top - 1e-6 && b.Bottom < a.Top - 1e-6;

    private static void AssertClearOfClamps(IEnumerable<Part> parts, params Box[] zones)
    {
        foreach (var part in parts)
            foreach (var zone in zones)
            {
                var keepOut = Inflated(zone, Edge);
                Assert.False(
                    Overlaps(part.BoundingBox, keepOut),
                    $"'{part.BaseDrawing.Name}' at ({part.Location.X:R},{part.Location.Y:R}) "
                        + $"box [{part.BoundingBox.Left:R}..{part.BoundingBox.Right:R} x "
                        + $"{part.BoundingBox.Bottom:R}..{part.BoundingBox.Top:R}] enters the clamp keep-out"
                );
            }
    }

    public static TheoryData<string> Strategies => new() { "Fill", "Strip", "Vertical Remnant", "Horizontal Remnant" };

    [Theory]
    [MemberData(nameof(Strategies))]
    public void FillItemNeverProposesPartInsideClampKeepOut(string strategy)
    {
        var occupant = new Part(RectDrawing(6, 6), new Vector(38, 14)); // occupied-plate fixture
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        plate.Parts.Add(occupant);

        var item = new NestItem { Drawing = RectDrawing(4, 4), Quantity = 3 };
        var parts = PlateFillService.FillItem(
            strategy, plate, item, plate.WorkArea(), null, CancellationToken.None);

        Assert.Equal(3, parts.Count);
        AssertClearOfClamps(parts, ZoneA, ZoneB);
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public void FillUsesMaterialBetweenAndBehindTheClamps(string strategy)
    {
        // The band covers both clamps only where y < 3: parts must land in the strip
        // above the clamps and in the slot between them - not merely fewer parts.
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        var band = new Box(9, 0.25, 13, 2.75);

        var item = new NestItem { Drawing = RectDrawing(3, 1.8), Quantity = 2 };
        var parts = PlateFillService.FillItem(strategy, plate, item, band, null, CancellationToken.None);

        Assert.Equal(2, parts.Count);
        AssertClearOfClamps(parts, ZoneA, ZoneB);
    }

    [Fact]
    public void PackAreaNeverPacksInsideClampKeepOut()
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        var items = new List<NestItem> { new() { Drawing = RectDrawing(4, 4), Quantity = 8 } };

        var parts = PlateFillService.PackArea(
            "Fill", plate, plate.WorkArea(), items, null, CancellationToken.None);

        Assert.True(parts.Count >= 5, $"expected the unclamped material to pack parts, got {parts.Count}");
        AssertClearOfClamps(parts, ZoneA, ZoneB);
    }

    [Fact]
    public void ProgressNeverPublishesUnverifiedClampPlacements()
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA));
        var updates = new List<NestProgress>();
        var progress = new InlineProgress(update => updates.Add(update));
        var item = new NestItem { Drawing = RectDrawing(4, 4), Quantity = 4 };
        var parts = PlateFillService.FillItem("Fill", plate, item, plate.WorkArea(),
            progress, CancellationToken.None);
        Assert.NotEmpty(parts);
        Assert.NotEmpty(updates);
        Assert.All(updates, update => AssertClearOfClamps(update.BestParts, ZoneA));
    }

    private sealed class InlineProgress(System.Action<NestProgress> report) : System.IProgress<NestProgress>
    {
        public void Report(NestProgress value) => report(value);
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public void WholePlateNestAvoidsClampsOnAnOccupiedPlate(string strategy)
    {
        var occupant = new Part(RectDrawing(6, 6), new Vector(38, 14));
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        plate.Parts.Add(occupant);

        var items = new List<NestItem> { new() { Drawing = RectDrawing(4, 4), Quantity = 10 } };
        var parts = PlateFillService.Nest(strategy, plate, items, null, CancellationToken.None);

        Assert.Equal(10, parts.Count);
        AssertClearOfClamps(parts, ZoneA, ZoneB);
    }
}
