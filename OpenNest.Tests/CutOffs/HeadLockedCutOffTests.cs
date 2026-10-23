using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// A head-locked cut-off overcuts past the nominal far sheet edge, where oversize stock may still
/// be, with the head height locked; started from that edge it overcuts outward, rapids back with
/// the head still locked, unlocks and cuts toward the origin.
/// </summary>
public class HeadLockedCutOffTests
{
    private const double Overcut = 1.5;

    [Fact]
    public void TowardTheFarEdge_OvercutsItWithTheHeadLocked()
    {
        var plate = new Plate(60, 120);
        var cutOff = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { HeadLockOvercut = Overcut };

        cutOff.Regenerate(plate, new CutOffSettings());

        AssertCodes(cutOff.Drawing.Program,
            "rapid 40,0", "cut 40,60", HeadHeightLock.Lock, "cut 40,61.5", HeadHeightLock.Unlock);
        Assert.Single(cutOff.SegmentPrograms());
    }

    [Fact]
    public void FromTheFarEdge_OvercutsOutwardRapidsBackLockedThenCutsTowardTheOrigin()
    {
        var plate = new Plate(60, 120);
        var cutOff = new CutOff(new Vector(40, 0), CutOffAxis.Vertical)
        {
            HeadLockOvercut = Overcut,
            FarEdgeFirst = true,
        };

        cutOff.Regenerate(plate, new CutOffSettings());

        AssertCodes(cutOff.Drawing.Program, "rapid 40,60", HeadHeightLock.Lock, "cut 40,61.5", "rapid 40,60",
            HeadHeightLock.Unlock, "cut 40,0");
        // The locked rapid returns along the overcut; it does not start a segment of its own.
        var segment = Assert.Single(cutOff.SegmentPrograms());
        Assert.True(CutOff.StartsAtFarEdge(segment));
        // One continuous cut across the sheet still separates it.
        Assert.True(CutOff.SeparatesSheet(new Part(cutOff.Drawing, segment), cutOff, plate.BoundingBox(false)));
    }

    [Fact]
    public void WithoutAHeadLock_FarEdgeFirstIsIgnoredAndOvertravelStillApplies()
    {
        var plate = new Plate(60, 120);
        var cutOff = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { FarEdgeFirst = true };

        cutOff.Regenerate(plate, new CutOffSettings { Overtravel = 0.5 });

        AssertCodes(cutOff.Drawing.Program, "rapid 40,0", "cut 40,60.5");
    }

    [Fact]
    public void TheHeadLockedOvercutReplacesOvertravelAtTheFarEdge()
    {
        var plate = new Plate(60, 120);
        var cutOff = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { HeadLockOvercut = Overcut };

        cutOff.Regenerate(plate, new CutOffSettings { Overtravel = 0.5 });

        AssertCodes(cutOff.Drawing.Program,
            "rapid 40,0", "cut 40,60", HeadHeightLock.Lock, "cut 40,61.5", HeadHeightLock.Unlock);
    }

    [Fact]
    public void ALimitShortOfTheFarEdge_HasNoOvercut()
    {
        var plate = new Plate(60, 120);
        var cutOff = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { HeadLockOvercut = Overcut, EndLimit = 50 };

        cutOff.Regenerate(plate, new CutOffSettings());

        AssertCodes(cutOff.Drawing.Program, "rapid 40,0", "cut 40,50");
    }

    // The far edge is the one away from the plate's origin: a quadrant-3 sheet runs from the
    // origin toward negative coordinates, so its far edges are the low ones.
    [Theory]
    [InlineData(1, CutOffAxis.Vertical, "rapid 40,0", "cut 40,60", "cut 40,61.5")]
    [InlineData(3, CutOffAxis.Vertical, "rapid -40,0", "cut -40,-60", "cut -40,-61.5")]
    [InlineData(1, CutOffAxis.Horizontal, "rapid 0,30", "cut 120,30", "cut 121.5,30")]
    [InlineData(2, CutOffAxis.Horizontal, "rapid 0,30", "cut -120,30", "cut -121.5,30")]
    public void TheOvercutIsAtTheEdgeAwayFromTheOrigin(int quadrant, CutOffAxis axis, string rapid, string toEdge,
        string overcut)
    {
        var plate = new Plate(60, 120) { Quadrant = quadrant };
        var sign = new Vector(quadrant is 2 or 3 ? -1 : 1, quadrant is 3 or 4 ? -1 : 1);
        var position = axis == CutOffAxis.Vertical ? new Vector(40 * sign.X, 0) : new Vector(0, 30 * sign.Y);
        var cutOff = new CutOff(position, axis) { HeadLockOvercut = Overcut };

        cutOff.Regenerate(plate, new CutOffSettings());

        AssertCodes(cutOff.Drawing.Program, rapid, toEdge, HeadHeightLock.Lock, overcut, HeadHeightLock.Unlock);
    }

    [Fact]
    public void OnlyTheSegmentReachingTheFarEdgeIsHeadLocked()
    {
        var part = TestHelpers.MakePartAt(35, 20, 10);
        var plate = TestHelpers.MakePlate(60, 120, part);
        var cutOff = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { HeadLockOvercut = Overcut, FarEdgeFirst = true };
        plate.CutOffs.Add(cutOff);

        plate.RegenerateCutOffs(new CutOffSettings());

        var segments = CutOffSegments.Of(plate, cutOff);
        Assert.Equal(2, segments.Count);
        Assert.False(HeadHeightLock.IsMarked(segments[0].Part.Program));
        Assert.True(CutOff.StartsAtFarEdge(segments[1].Part.Program));
        Assert.Equal(61.5, segments[1].End, 6);
    }

    [Fact]
    public void ReverseAtFarEdge_TurnsEitherFormIntoTheOther()
    {
        var plate = new Plate(60, 120);
        var cutOff = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { HeadLockOvercut = Overcut };
        cutOff.Regenerate(plate, new CutOffSettings());
        var toward = cutOff.Drawing.Program;

        var from = CutOff.ReverseAtFarEdge(toward);
        var back = CutOff.ReverseAtFarEdge(from);

        Assert.True(CutOff.StartsAtFarEdge(from));
        AssertCodes(back, "rapid 40,0", "cut 40,60", HeadHeightLock.Lock, "cut 40,61.5", HeadHeightLock.Unlock);
        // A plain segment has no far-edge form.
        cutOff.HeadLockOvercut = 0;
        cutOff.Regenerate(plate, new CutOffSettings());
        Assert.Null(CutOff.ReverseAtFarEdge(cutOff.Drawing.Program));
    }

    [Fact]
    public void AutomaticCutOffs_GetTheHeadLockedOvercutWhenItIsOn()
    {
        var plate = TestHelpers.MakePlate(60, 120, TestHelpers.MakePartAt(5, 5, 10));
        var settings = new CutOffSettings { LockHeadHeight = true, HeadLockOvercut = Overcut };

        var plan = AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = 35 }, settings);

        Assert.NotEmpty(plan.Definitions);
        Assert.All(plan.Definitions, definition =>
        {
            Assert.Equal(Overcut, definition.HeadLockOvercut);
            Assert.True(HeadHeightLock.IsMarked(definition.SegmentPrograms()[^1]));
        });
        Assert.All(AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = 35 }, new CutOffSettings())
            .Definitions, definition => Assert.Equal(0, definition.HeadLockOvercut));
    }

    [Fact]
    public void RoundTrip_KeepsTheOvercutAndDirection()
    {
        var nest = new Nest();
        var plate = new Plate(60, 120);
        nest.Plates.Add(plate);
        plate.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { HeadLockOvercut = Overcut, FarEdgeFirst = true });
        plate.CutOffs.Add(new CutOff(new Vector(80, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        stream.Position = 0;
        var loaded = Assert.Single(new NestReader(stream).Read().Plates);

        Assert.Equal(new[] { Overcut, 0 }, loaded.CutOffs.Select(c => c.HeadLockOvercut));
        Assert.Equal(new[] { true, false }, loaded.CutOffs.Select(c => c.FarEdgeFirst));
        Assert.True(CutOff.StartsAtFarEdge(Assert.Single(TestHelpers.CutOffParts(loaded, loaded.CutOffs[0])).Program));
    }

    [Fact]
    public void LockedMask_RefusesMarkersThatDoNotPair()
    {
        var unlockFirst = new ICode[] { new Comment(HeadHeightLock.Unlock) };
        var neverReleased = new ICode[] { new Comment(HeadHeightLock.Lock), new LinearMove(1, 0) };
        var lockedTwice = new ICode[] { new Comment(HeadHeightLock.Lock), new Comment(HeadHeightLock.Lock) };

        Assert.Throws<ArgumentException>(() => HeadHeightLock.LockedMask(unlockFirst));
        Assert.Throws<ArgumentException>(() => HeadHeightLock.LockedMask(neverReleased));
        Assert.Throws<ArgumentException>(() => HeadHeightLock.LockedMask(lockedTwice));
        Assert.Equal(new[] { false, false, true, false },
            HeadHeightLock.LockedMask([new RapidMove(0, 0), new Comment(HeadHeightLock.Lock), new LinearMove(1, 0),
                new Comment(HeadHeightLock.Unlock)]));
    }

    [Fact]
    public void EnsurePostable_RefusesAHeadLockedCutOffWithoutLockCodes()
    {
        var nest = new Nest();
        var plate = new Plate(60, 120);
        nest.Plates.Add(plate);
        plate.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { HeadLockOvercut = Overcut });
        plate.RegenerateCutOffs(new CutOffSettings());

        var refusal = Assert.Throws<InvalidOperationException>(() => HeadHeightLock.EnsurePostable(nest, false, "Test"));
        Assert.Contains("head-height lock and unlock codes", refusal.Message);
        HeadHeightLock.EnsurePostable(nest, true, "Test");
    }

    internal static void AssertCodes(Program program, params string[] expected) =>
        Assert.Equal(expected, program.Codes.Select(Describe));

    private static string Describe(ICode code) => code switch
    {
        Comment comment => comment.Value,
        RapidMove rapid => $"rapid {Point(rapid.EndPoint)}",
        LinearMove line => $"cut {Point(line.EndPoint)}",
        _ => code.GetType().Name,
    };

    private static string Point(Vector point) =>
        FormattableString.Invariant($"{System.Math.Round(point.X, 6)},{System.Math.Round(point.Y, 6)}");
}
