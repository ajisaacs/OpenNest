using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
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
    public void DesktopGroupFillThenSettleRemainsClearOfPlateOccupant()
    {
        var plate = new Plate(new Size(20, 20))
        {
            EdgeSpacing = new Spacing(0.25, 0.25),
            PartSpacing = 0.1,
        };
        plate.ClampZones.Add(new ClampZone("remote", new Box(15, 0, 1.5, 0.625)));
        var occupant = new Part(RectDrawing(4, 19.5), new Vector(0.25, 0.25));
        plate.Parts.Add(occupant);
        var group = new List<Part> { new(RectDrawing(3, 3)) };
        var result = PlateFillService.FillGroup("Fill", plate, group,
            plate.WorkArea(), null, CancellationToken.None);
        Assert.True(result.Count > 1);
        Assert.DoesNotContain(result, part => Overlaps(part.BoundingBox, occupant.BoundingBox));
        Compactor.Settle(result, plate, plate.WorkArea());
        Assert.DoesNotContain(result, part => Overlaps(part.BoundingBox, occupant.BoundingBox));
        AssertClearOfClamps(result, new Box(15, 0, 1.5, 0.625));
    }

    [Fact]
    public void FillGroupRetainsCompleteCopiesAheadOfAPartialTrailingCopy()
    {
        var drawing = RectDrawing(2, 2);
        var group = new List<Part>
        {
            new(drawing, new Vector(0, 0)), new(drawing, new Vector(3, 0)),
        };
        var plate = new Plate(new Size(20, 20)) { PartSpacing = 0 };
        var area = new Box(0, 0, 8, 2);
        var original = PlateFillService.FillGroup("Fill", plate, group, area,
            null, CancellationToken.None);
        Assert.True(original.Count > group.Count && original.Count % group.Count != 0,
            $"expected a full copy and partial tail, got {original.Count}");
        plate.ClampZones.Add(new ClampZone("remote", new Box(18, 0, 1, 0.5)));
        var clamped = PlateFillService.FillGroup("Fill", plate, group, area,
            null, CancellationToken.None);
        Assert.Equal(group.Count, clamped.Count);
    }

    [Fact]
    public void FillGroupRejectsTheWholeCopyWhenOnlyOneMemberTouchesAClamp()
    {
        var plate = new Plate(new Size(20, 20)) { PartSpacing = 0 };
        plate.ClampZones.Add(new ClampZone("front", new Box(5, 0, 1, 0.5)));
        var drawing = RectDrawing(2, 2);
        var group = new List<Part>
        {
            new(drawing, new Vector(2, 0)), new(drawing, new Vector(5, 0)),
        };
        var parts = PlateFillService.FillGroup("Fill", plate, group, new Box(2, 0, 5, 2),
            null, CancellationToken.None);
        Assert.Equal(0, parts.Count % group.Count);
        Assert.Empty(parts);
    }

    [Fact]
    public void FillGroupKeepsAnInsertInItsFrameHoleWhenAClampIsElsewhere()
    {
        var program = RectProgram(4, 4);
        program.MoveTo(1, 1);
        program.LineTo(1, 3);
        program.LineTo(3, 3);
        program.LineTo(3, 1);
        program.LineTo(1, 1);
        var frame = new Drawing("frame", program);
        var insert = RectDrawing(1, 1);
        var plate = new Plate(new Size(20, 20)) { PartSpacing = 0.25 };
        plate.ClampZones.Add(new ClampZone("remote", new Box(18, 0, 1, 0.5)));
        var group = new List<Part>
        {
            new(frame, new Vector(1, 3)), new(insert, new Vector(2.5, 4.5)),
        };
        var parts = PlateFillService.FillGroup("Fill", plate, group, new Box(1, 3, 4, 4),
            null, CancellationToken.None);
        Assert.Contains(parts, part => ReferenceEquals(part.BaseDrawing, frame));
        Assert.Contains(parts, part => ReferenceEquals(part.BaseDrawing, insert));
        Assert.Equal(0, parts.Count % 2);
    }

    [Fact]
    public void PackAreaKeepsAllowedOverhangOnlyAtThePlateBoundary()
    {
        var plate = new Plate(new Size(12, 12))
        {
            EdgeSpacing = new Spacing(1, 1),
            PartSpacing = 0,
        };
        plate.ClampZones.Add(new ClampZone("front", new Box(5, 0, 1, 0.5)));
        var drawing = RectDrawing(10 + OpenNest.Engine.NestingEngines.Rectangles.SheetPacker.OverhangAllowance * 0.8, 4);
        var items = new List<NestItem> { new() { Drawing = drawing, Quantity = 2 } };
        var parts = PlateFillService.PackArea("Fill", plate, plate.WorkArea(), items,
            null, CancellationToken.None);
        Assert.Equal(2, parts.Count);
        Assert.All(parts, part => Assert.True(part.BoundingBox.Right > plate.WorkArea().Right));
        AssertClearOfClamps(parts, new Box(5, 0, 1, 0.5));
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

    [Fact]
    public void ClampedFillIsTheUnclampedFillLessThePartsThatHitAClamp()
    {
        var item = new NestItem { Drawing = RectDrawing(4, 4) };
        var unclamped = PlateFillService.FillItem("Fill", MakePlate(), item, MakePlate().WorkArea(),
            null, CancellationToken.None);
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));

        var clamped = PlateFillService.FillItem("Fill", plate, item, plate.WorkArea(),
            null, CancellationToken.None);

        var hits = unclamped.Count(part =>
            Overlaps(part.BoundingBox, Inflated(ZoneA, Edge)) || Overlaps(part.BoundingBox, Inflated(ZoneB, Edge)));
        Assert.True(hits > 0, "fixture should put parts on the clamps");
        Assert.Equal(unclamped.Count - hits, clamped.Count);
        Assert.All(clamped, part => Assert.Contains(unclamped, other => other.Location == part.Location));
        AssertClearOfClamps(clamped, ZoneA, ZoneB);
    }

    [Fact]
    public void ClampedFillShowsPreviewsThatCanBeAcceptedEarly()
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA), new ClampZone("front-2", ZoneB));
        var item = new NestItem { Drawing = RectDrawing(4, 4) };
        using var cts = new CancellationTokenSource();
        NestProgress first = null;

        var parts = PlateFillService.FillItem("Fill", plate, item, plate.WorkArea(),
            new InlineProgress(update =>
            {
                first ??= update;
                cts.Cancel();
            }),
            cts.Token);

        Assert.NotNull(first);
        Assert.True(first.IsOverallBest);
        AssertClearOfClamps(first.BestParts, ZoneA, ZoneB);
        Assert.Equal(first.BestParts.Count, parts.Count);
        AssertClearOfClamps(parts, ZoneA, ZoneB);
    }

    private sealed class InlineProgress(System.Action<NestProgress> report) : System.IProgress<NestProgress>
    {
        public void Report(NestProgress value) => report(value);
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public void WholePlateNestWithNoRequestedPartsDoesNotFillBecauseOfClamps(string strategy)
    {
        var plate = MakePlate(new ClampZone("front-1", ZoneA));
        var item = new NestItem { Drawing = RectDrawing(3, 3), Quantity = 0 };
        var parts = PlateFillService.Nest(strategy, plate, new List<NestItem> { item },
            null, CancellationToken.None);
        if (strategy != "Strip")
            Assert.Empty(parts);
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
