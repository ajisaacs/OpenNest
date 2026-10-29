using OpenNest.Geometry;

namespace OpenNest.Tests;

/// <summary>
/// The fixed machine clamp rail: X centers measured from the plate origin along the
/// origin-side edge, resolved onto every sheet size without re-scaling (D2/D3).
/// </summary>
public class ClampRailTests
{
    // The user's machine: ~8 clamps, 1.5" along the edge x 0.625" into the sheet.
    private static readonly double[] MachineCenters = { 6, 21, 36, 51, 66, 81, 96, 111 };

    private static ClampRail Machine() => new(1.5, 0.625, MachineCenters);

    // Plate(width, length): Width is the Y extent, Length the X extent along the clamp edge.
    private static Plate Sheet(double width, double length, int quadrant = 1) =>
        new(width, length) { Quadrant = quadrant };

    [Theory]
    [InlineData(0, 0.625)]
    [InlineData(-1, 0.625)]
    [InlineData(double.NaN, 0.625)]
    [InlineData(double.PositiveInfinity, 0.625)]
    [InlineData(1.5, 0)]
    [InlineData(1.5, -0.5)]
    [InlineData(1.5, double.NaN)]
    public void NonPositiveOrNonFiniteSizesAreRejected(double width, double depth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClampRail(width, depth, new[] { 6.0 }));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CentersMustBeFiniteAndFromTheOrigin(double center)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClampRail(1.5, 0.625, new[] { 6, center }));
        Assert.Throws<ArgumentNullException>(() => new ClampRail(1.5, 0.625, (IEnumerable<double>)null!));
    }

    [Fact]
    public void CentersAreSortedAndDistinct()
    {
        var rail = new ClampRail(1.5, 0.625, new[] { 36.0, 6, 21, 6 });
        Assert.Equal(new[] { 6.0, 21, 36 }, rail.Centers);
    }

    [Fact]
    public void FullLengthSheetGetsEveryClampOnTheFrontEdge()
    {
        var zones = Machine().ResolveFor(new Size(60, 120), quadrant: 1);

        Assert.Equal(8, zones.Count);
        for (var i = 0; i < zones.Count; i++)
        {
            Assert.Equal($"clamp-{i + 1}", zones[i].Id);
            Assert.Equal(MachineCenters[i] - 0.75, zones[i].Bounds.X);
            Assert.Equal(0, zones[i].Bounds.Y);
            Assert.Equal(1.5, zones[i].Bounds.Length);
            Assert.Equal(0.625, zones[i].Bounds.Width);
        }
    }

    [Fact]
    public void ShorterSheetSkipsClampsBeyondItWithoutRescaling()
    {
        // D2: a 60" sheet keeps the clamps at their table positions and drops the rest.
        var zones = Machine().ResolveFor(new Size(48, 60), quadrant: 1);

        Assert.Equal(new[] { "clamp-1", "clamp-2", "clamp-3", "clamp-4" }, zones.Select(z => z.Id));
        Assert.Equal(new[] { 5.25, 20.25, 35.25, 50.25 }, zones.Select(z => z.Bounds.X));
    }

    [Fact]
    public void ClampReachingOntoTheSheetIsKeptWhole()
    {
        // A clamp is an avoidance area: if any of it reaches the sheet, all of it applies.
        var rail = new ClampRail(1.5, 0.625, new[] { 59.5, 60.5, 60.75, 61 });

        var zones = rail.ResolveFor(new Size(48, 60), quadrant: 1);

        // 59.5 covers [58.75, 60.25] and 60.5 still reaches the last 0.25" [59.75, 61.25];
        // 60.75 only touches the sheet end and 61 lies wholly beyond it.
        Assert.Equal(new[] { "clamp-1", "clamp-2" }, zones.Select(z => z.Id));
        Assert.Equal(new[] { 58.75, 59.75 }, zones.Select(z => z.Bounds.X));
        Assert.All(zones, z => Assert.Equal(1.5, z.Bounds.Length));
    }

    [Fact]
    public void ClampsNearTheOriginOrDeeperThanTheSheetStayWhole()
    {
        var rail = new ClampRail(1.5, 0.625, new[] { 0.25 });
        var origin = Assert.Single(rail.ResolveFor(new Size(48, 96), quadrant: 1));
        Assert.Equal(-0.5, origin.Bounds.X);
        Assert.Equal(1.5, origin.Bounds.Length);

        var deep = new ClampRail(1.5, 10, new[] { 6.0 });
        var front = Assert.Single(deep.ResolveFor(new Size(4, 96), 1));
        Assert.Equal(0, front.Bounds.Y);
        Assert.Equal(10, front.Bounds.Width);
        // Sheets extending to -Y have the origin edge on top; the clamp hangs below the sheet.
        var top = Assert.Single(deep.ResolveFor(new Size(4, 96), 3));
        Assert.Equal(4.0, top.Bounds.Top);
        Assert.Equal(10, top.Bounds.Width);
    }

    [Theory]
    [InlineData(0, 96)]
    [InlineData(48, 0)]
    [InlineData(double.NaN, 96)]
    public void DegenerateSheetsGetNoZones(double width, double length)
    {
        Assert.Empty(Machine().ResolveFor(new Size(width, length), quadrant: 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ZonesSitOnTheOriginEdgeMeasuredFromTheOriginInEveryQuadrant(int quadrant)
    {
        var plate = Sheet(48, 96, quadrant);
        plate.SetClampRail(new ClampRail(1.5, 0.625, new[] { 6.0 }));
        var zone = Assert.Single(plate.ClampZones);

        // Plate coordinates start at the sheet's lower-left corner; translating by the
        // sheet's placement corner gives machine coordinates around the origin.
        var sheet = plate.BoundingBox(includeParts: false);
        var machine = zone.Bounds.Translate(sheet.X, sheet.Y);
        var awayFromOrigin = quadrant is 1 or 4 ? 1 : -1;
        var intoSheet = quadrant is 1 or 2 ? 1 : -1;

        Assert.Equal(6.0, System.Math.Abs(machine.Center.X), 12);
        Assert.Equal(awayFromOrigin, System.Math.Sign(machine.Center.X));
        Assert.Equal(0.0, intoSheet > 0 ? machine.Bottom : machine.Top, 12);
        Assert.Equal(0.625, machine.Width);
        Assert.Equal(1.5, machine.Length, 12);
        Assert.True(zone.Bounds.Left >= 0 && zone.Bounds.Right <= 96 + 1e-9);
        Assert.True(zone.Bounds.Bottom >= 0 && zone.Bounds.Top <= 48 + 1e-9);
    }

    [Fact]
    public void PlateFollowsItsRailWhenResizedOrRotated()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine());
        Assert.Equal(8, plate.ClampZones.Count);
        Assert.True(plate.ClampRail.Matches(plate));

        plate.Size = new Size(48, 60);
        Assert.Equal(4, plate.ClampZones.Count);

        // Rotating swaps the sheet's extents; the machine's clamps stay where they are.
        plate.Rotate90(RotationType.CW);
        Assert.Equal(new Size(60, 48), plate.Size);
        Assert.Equal(new[] { 5.25, 20.25, 35.25 }, plate.ClampZones.Select(z => z.Bounds.X));

        plate.Quadrant = 2;
        Assert.True(plate.ClampRail.Matches(plate));
        Assert.Equal(48 - 6.75, plate.ClampZones[0].Bounds.X);
    }

    [Fact]
    public void UnchangedResolutionRaisesNoEvents()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine());
        var original = plate.ClampZones.ToList();
        var changes = 0;
        plate.ClampZonesChanged += (_, _) => changes++;

        plate.Size = new Size(48, 120);
        plate.Quadrant = 1;
        plate.SetClampRail(Machine());

        Assert.Equal(0, changes);
        Assert.Equal(original, plate.ClampZones); // same zone instances
    }

    [Fact]
    public void DetachingTheRailRemovesItsZones()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine());

        plate.SetClampRail(null!);

        Assert.Null(plate.ClampRail);
        Assert.Empty(plate.ClampZones);
        plate.Size = new Size(48, 96);
        Assert.Empty(plate.ClampZones);
    }

    [Fact]
    public void PlatesWithoutARailKeepExplicitZonesThroughResizes()
    {
        var plate = Sheet(48, 96);
        plate.ClampZones.Add(new ClampZone("manual", new Box(5.25, 0, 1.5, 0.625)));

        plate.Size = new Size(48, 120);
        plate.Quadrant = 3;

        Assert.Equal("manual", Assert.Single(plate.ClampZones).Id);
        Assert.False(Machine().Matches(plate));
    }

    [Fact]
    public void NewPlatesFollowTheNestRailAtTheirOwnSize()
    {
        var nest = new Nest();
        nest.PlateDefaults.Size = new Size(48, 60);
        nest.SetClampRail(Machine());

        var plate = nest.CreatePlate();

        Assert.Same(nest.PlateDefaults.ClampRail, plate.ClampRail);
        Assert.Equal(4, plate.ClampZones.Count);
        plate.Size = new Size(60, 120);
        Assert.Equal(8, plate.ClampZones.Count);
    }

    [Fact]
    public void NestRailReplacesEveryPlatesZones()
    {
        var nest = new Nest();
        nest.PlateDefaults.Size = new Size(48, 120);
        var a = nest.CreatePlate();
        var b = nest.CreatePlate();
        b.Size = new Size(48, 60);
        b.ClampZones.Add(new ClampZone("manual", new Box(1, 0, 1, 1)));

        nest.SetClampRail(Machine());
        Assert.Equal(8, a.ClampZones.Count);
        Assert.Equal(4, b.ClampZones.Count);
        Assert.DoesNotContain(b.ClampZones, z => z.Id == "manual");

        // A rail without clamps is no rail: zones and the template are cleared everywhere.
        nest.SetClampRail(new ClampRail(1.5, 0.625, Array.Empty<double>()));
        Assert.Null(nest.PlateDefaults.ClampRail);
        Assert.All(nest.Plates, p => Assert.Empty(p.ClampZones));
        Assert.Empty(nest.CreatePlate().ClampZones);
    }

    [Fact]
    public void RailZonesStayOutOfQuantityAndUtilization()
    {
        var drawing = new Drawing("part", new OpenNest.CNC.Program());
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        nest.SetClampRail(Machine());
        var plate = nest.CreatePlate();

        Assert.Equal(8, plate.ClampZones.Count);
        Assert.Empty(plate.Parts);
        Assert.Equal(0, plate.Utilization());
        nest.UpdateDrawingQuantities();
        Assert.Equal(0, drawing.Quantity.Nested);
    }

    [Fact]
    public void RailsCompareByValue()
    {
        Assert.Equal(Machine(), new ClampRail(1.5, 0.625, MachineCenters.Reverse()));
        Assert.Equal(Machine().GetHashCode(), new ClampRail(1.5, 0.625, MachineCenters).GetHashCode());
        Assert.NotEqual(Machine(), new ClampRail(1.5, 0.5, MachineCenters));
        Assert.NotEqual(Machine(), new ClampRail(1.5, 0.625, MachineCenters.Take(7)));
    }

    // D9: minutes to remove one clamp and put it back, a variable setting on the rail.

    [Fact]
    public void RemovalMinutesDefaultsToThePlannerValue()
    {
        Assert.Equal(5.0, Machine().RemovalMinutes);
        Assert.Equal(5.0, ClampRail.DefaultRemovalMinutes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RemovalMinutesMustBeFiniteAndZeroOrMore(double minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ClampRail(1.5, 0.625, new[] { 6.0 }, minutes));
    }

    [Fact]
    public void ZeroRemovalMinutesIsAllowed()
    {
        var rail = new ClampRail(1.5, 0.625, new[] { 6.0 }, removalMinutes: 0);
        Assert.Equal(0, rail.RemovalMinutes);
    }

    [Fact]
    public void RailsWithDifferentRemovalMinutesAreNotEqual()
    {
        var fiveMinutes = new ClampRail(1.5, 0.625, MachineCenters, 5);
        var tenMinutes = new ClampRail(1.5, 0.625, MachineCenters, 10);
        Assert.NotEqual(fiveMinutes, tenMinutes);
        Assert.NotEqual(fiveMinutes.GetHashCode(), tenMinutes.GetHashCode());
    }

    // D8/C13: last-resort per-sheet removal of individual rail clamps.

    [Fact]
    public void RemovedClampsAreSubtractedFromTheRailResolution()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine(), new[] { "clamp-3" });

        Assert.Equal(7, plate.ClampZones.Count);
        Assert.DoesNotContain(plate.ClampZones, z => z.Id == "clamp-3");
        Assert.Equal(new[] { "clamp-3" }, plate.RemovedClamps);
    }

    [Fact]
    public void RemovedClampStaysOffAfterResizeAndReappearsIfTheClampComesBackOntoTheSheet()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine(), new[] { "clamp-8" }); // centered at 111"

        plate.Size = new Size(48, 60); // clamp-8 is beyond this sheet regardless
        Assert.Equal(new[] { "clamp-1", "clamp-2", "clamp-3", "clamp-4" },
            plate.ClampZones.Select(z => z.Id));
        Assert.Equal(new[] { "clamp-8" }, plate.RemovedClamps);

        plate.Size = new Size(48, 120); // clamp-8 is back on the sheet, but still removed
        Assert.Equal(7, plate.ClampZones.Count);
        Assert.DoesNotContain(plate.ClampZones, z => z.Id == "clamp-8");
    }

    [Fact]
    public void ClampRemovalMinutesSumsTheRailsPerClampTime()
    {
        var plate = Sheet(48, 120);
        Assert.Equal(0, plate.ClampRemovalMinutes); // no rail yet

        plate.SetClampRail(new ClampRail(1.5, 0.625, MachineCenters, removalMinutes: 7),
            new[] { "clamp-3", "clamp-4" });
        Assert.Equal(14, plate.ClampRemovalMinutes);

        plate.SetClampRail(null!);
        Assert.Equal(0, plate.ClampRemovalMinutes); // detaching forgets the removed set too
        Assert.Empty(plate.RemovedClamps);
    }

    [Fact]
    public void SetClampRailWithoutRemovedClampsBehavesAsBefore()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine());

        Assert.Empty(plate.RemovedClamps);
        Assert.Equal(8, plate.ClampZones.Count);
    }

    [Fact]
    public void MatchesWithRemovedClampsComparesAgainstTheReducedResolution()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine(), new[] { "clamp-3" });

        Assert.True(Machine().Matches(plate, new[] { "clamp-3" }));
        Assert.False(Machine().Matches(plate)); // full resolution: zones don't match
        Assert.False(Machine().Matches(plate, Array.Empty<string>()));
        Assert.False(Machine().Matches(plate, new[] { "clamp-4" }));
    }
    // PM 6ded8ac4 (a): only removed clamps that are actually on this sheet cost time.
    [Fact]
    public void RemovalTimeCountsOnlyRemovedClampsOnTheSheet()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine(), new[] { "clamp-3", "clamp-8", "clamp-99" });
        Assert.Equal(2 * ClampRail.DefaultRemovalMinutes, plate.ClampRemovalMinutes); // 3 and 8

        plate.Size = new Size(48, 60); // clamp-8 is now beyond the sheet
        Assert.Equal(ClampRail.DefaultRemovalMinutes, plate.ClampRemovalMinutes);
        Assert.Contains("clamp-8", plate.RemovedClamps); // still listed (C13)
    }

    // PM 6ded8ac4 (e): the removed set is read-only, even through a downcast, and an
    // untouched plate does not share a mutable empty set with every other plate.
    [Fact]
    public void RemovedClampsCannotBeMutatedThroughTheCollection()
    {
        var removed = Sheet(48, 120);
        removed.SetClampRail(Machine(), new[] { "clamp-3" });
        var untouched = Sheet(48, 120);

        Assert.True(((ICollection<string>)removed.RemovedClamps).IsReadOnly);
        Assert.True(((ICollection<string>)untouched.RemovedClamps).IsReadOnly);
    }

    // Manual per-sheet removal (right-click in the plate view).

    [Fact]
    public void SetClampRemovedTakesAClampOffAndPutsItBack()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine());
        var changes = 0;
        plate.ClampZonesChanged += (_, _) => changes++;

        Assert.True(plate.SetClampRemoved("clamp-3", true));
        Assert.Equal(new[] { "clamp-3" }, plate.RemovedClamps);
        Assert.DoesNotContain(plate.ClampZones, z => z.Id == "clamp-3");
        Assert.Equal(ClampRail.DefaultRemovalMinutes, plate.ClampRemovalMinutes);
        Assert.True(changes > 0);

        Assert.True(plate.SetClampRemoved("clamp-3", false));
        Assert.Empty(plate.RemovedClamps);
        Assert.True(Machine().Matches(plate));
        Assert.Equal(0, plate.ClampRemovalMinutes);
    }

    [Fact]
    public void SetClampRemovedKeepsOtherRemovedClamps()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine(), new[] { "clamp-1" });

        Assert.True(plate.SetClampRemoved("clamp-3", true));
        Assert.Equal(new[] { "clamp-1", "clamp-3" }, plate.RemovedClamps.OrderBy(id => id));

        Assert.True(plate.SetClampRemoved("clamp-1", false));
        Assert.Equal(new[] { "clamp-3" }, plate.RemovedClamps);
        Assert.Equal(Machine(), plate.ClampRail);
    }

    [Fact]
    public void SetClampRemovedReportsNoChange()
    {
        var bare = Sheet(48, 120);
        Assert.False(bare.SetClampRemoved("clamp-1", true)); // no rail

        var plate = Sheet(48, 60);
        plate.SetClampRail(Machine());
        Assert.False(plate.SetClampRemoved("clamp-1", false)); // already in place
        Assert.False(plate.SetClampRemoved("clamp-8", true)); // beyond this sheet
        Assert.False(plate.SetClampRemoved("clamp-99", true)); // unknown to the rail
        Assert.False(plate.SetClampRemoved(null!, true));
        Assert.Empty(plate.RemovedClamps);

        Assert.True(plate.SetClampRemoved("clamp-2", true));
        Assert.False(plate.SetClampRemoved("clamp-2", true)); // already removed
    }

    [Fact]
    public void ARemovedClampBeyondTheSheetCanStillBePutBack()
    {
        var plate = Sheet(48, 120);
        plate.SetClampRail(Machine(), new[] { "clamp-8" });
        plate.Size = new Size(48, 60);

        Assert.True(plate.SetClampRemoved("clamp-8", false));
        Assert.Empty(plate.RemovedClamps);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RailClampAtFindsClampsInThePartsPlacementFrame(int quadrant)
    {
        var plate = Sheet(48, 120, quadrant);
        plate.SetClampRail(Machine(), new[] { "clamp-3" });
        var full = Sheet(48, 120, quadrant);
        full.SetClampRail(Machine());
        var expected = ClampZoneFrame.ToPlacement(full); // every clamp, removed ones too

        foreach (var zone in expected)
        {
            var hit = plate.RailClampAt(zone.Bounds.Center);
            Assert.NotNull(hit);
            Assert.Equal(zone.Id, hit.Id);
            Assert.Equal(zone.Bounds.X, hit.Bounds.X, 9);
            Assert.Equal(zone.Bounds.Y, hit.Bounds.Y, 9);
        }
    }

    [Fact]
    public void RailClampAtHonoursTheToleranceAndNeedsARail()
    {
        var plate = Sheet(48, 120);
        Assert.Null(plate.RailClampAt(new Vector(36, 0.3))); // no rail

        plate.SetClampRail(Machine());
        Assert.Equal("clamp-3", plate.RailClampAt(new Vector(36, 0.3))?.Id);
        Assert.Null(plate.RailClampAt(new Vector(36, 0.7)));
        Assert.Equal("clamp-3", plate.RailClampAt(new Vector(36, 0.7), tolerance: 0.1)?.Id);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ClearanceMustBeFiniteAndZeroOrMore(double clearance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClampRail(1.5, 0.625, new[] { 6.0 }, clearance: clearance));
    }

    [Fact]
    public void ClearanceIsPartOfTheRailAndSurvivesNewClamps()
    {
        var rail = new ClampRail(1.5, 0.625, new[] { 6.0 }, clearance: 0.05);

        Assert.Equal(0.05, rail.Clearance);
        Assert.Equal(0.05, rail.WithClamps(new[] { new RailClamp(ClampEdge.FarSide, 9) }).Clearance);
        Assert.NotEqual(rail, new ClampRail(1.5, 0.625, new[] { 6.0 }));
        Assert.Equal(rail, new ClampRail(1.5, 0.625, new[] { 6.0 }, clearance: 0.05));
    }

    [Fact]
    public void PlateClampClearanceIsTheRailsOwnElseTheBottomEdgeSpacing()
    {
        var plate = Sheet(60, 120);
        plate.EdgeSpacing = new Spacing(0.25, 0.5, 0.25, 0.25);
        plate.PartSpacing = 1;
        Assert.Equal(0.5, plate.ClampClearance);

        plate.SetClampRail(Machine());
        Assert.Equal(0.5, plate.ClampClearance);

        plate.SetClampRail(new ClampRail(1.5, 0.625, MachineCenters, clearance: 0));
        Assert.Equal(0, plate.ClampClearance);
    }
}
