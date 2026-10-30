using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// A cut-off's trimmed segments are materialized as one part each, with its own place in the
/// cut sequence; each stands for the span of the line between its neighboring segments.
/// </summary>
public class CutOffSegmentsTests
{
    private const double Clearance = 0.02;

    [Fact]
    public void Of_GivesEachSegmentTheSpanBetweenItsNeighbors()
    {
        var (plate, cutOff, _, _) = ThreeSegments();

        var segments = CutOffSegments.Of(plate, cutOff);

        Assert.Equal(3, segments.Count);
        AssertSegment(segments[0], 0, 10 - Clearance, 0, 20 + Clearance);
        AssertSegment(segments[1], 20 + Clearance, 30 - Clearance, 10 - Clearance, 40 + Clearance);
        AssertSegment(segments[2], 40 + Clearance, 60, 30 - Clearance, 60);
        Assert.Equal(segments.Select(s => s.Part), plate.Parts.Where(p => p.BaseDrawing.IsCutOff));
    }

    [Fact]
    public void Of_LimitedLine_SpansEndAtItsLimits()
    {
        var plate = TestHelpers.MakePlate(60, 120, TestHelpers.MakePartAt(10, 10, 10));
        var cutOff = new CutOff(new Vector(15, 0), CutOffAxis.Vertical) { StartLimit = 5, EndLimit = 45 };
        plate.CutOffs.Add(cutOff);
        plate.RegenerateCutOffs(new CutOffSettings());

        var segments = CutOffSegments.Of(plate, cutOff);

        Assert.Equal(2, segments.Count);
        AssertSegment(segments[0], 5, 10 - Clearance, 5, 20 + Clearance);
        AssertSegment(segments[1], 20 + Clearance, 45, 10 - Clearance, 45);
    }

    [Fact]
    public void Of_PartThatCannotBeMeasured_StandsForTheWholeLine()
    {
        var plate = TestHelpers.MakePlate(60, 120);
        var cutOff = new CutOff(new Vector(15, 0), CutOffAxis.Vertical);
        var program = new Program(Mode.Incremental);
        program.Codes.Add(new RapidMove(15, 0));
        program.Codes.Add(new LinearMove(0, 60));
        cutOff.Drawing.Program = program;
        plate.CutOffs.Add(cutOff);
        plate.Parts.Add(new Part(cutOff.Drawing));

        var segment = Assert.Single(CutOffSegments.Of(plate, cutOff));

        AssertSegment(segment, 0, 60, 0, 60);
    }

    [Fact]
    public void RegenerateCutOffs_KeepsEachSegmentsPlaceInTheSequence()
    {
        var (plate, cutOff, lower, upper) = ThreeSegments();
        var segments = CutOffSegments.Of(plate, cutOff).Select(s => s.Part).ToArray();
        plate.Parts.Reorder([segments[0], lower, segments[1], upper, segments[2]]);
        var settings = new CutOffSettings();

        plate.RegenerateCutOffs(settings);

        AssertArrangement(plate, cutOff, lower, upper);
        // A part moved along the line still sits in the same gap: its segments keep their places.
        lower.Offset(1, 0);
        plate.RegenerateCutOffs(settings);
        AssertArrangement(plate, cutOff, lower, upper);
    }

    [Fact]
    public void RegenerateCutOffs_SegmentWithoutASavedPlace_FollowsTheOneBeforeIt()
    {
        var (plate, cutOff, lower, upper) = ThreeSegments();
        var segments = CutOffSegments.Of(plate, cutOff).Select(s => s.Part).ToArray();
        plate.Parts.Reorder([segments[0], lower, segments[1], upper, segments[2]]);
        // A part dropped onto the top segment splits it in two.
        var added = TestHelpers.MakePartAt(10, 48, 5);
        plate.Parts.Add(added);

        plate.RegenerateCutOffs(new CutOffSettings());

        var regenerated = CutOffSegments.Of(plate, cutOff);
        Assert.Equal(4, regenerated.Count);
        Assert.Equal(new[] { regenerated[0].Part, lower, regenerated[1].Part, upper, regenerated[2].Part, regenerated[3].Part, added },
            plate.Parts);
    }

    [Theory]
    [InlineData(CutDirection.AwayFromOrigin)]
    [InlineData(CutDirection.TowardOrigin)]
    public void RegenerateCutOffs_ListOverload_PlacesSegmentsByAscendingPosition(CutDirection direction)
    {
        var lower = TestHelpers.MakePartAt(10, 10, 10);
        var upper = TestHelpers.MakePartAt(10, 30, 10);
        var plate = TestHelpers.MakePlate(60, 120, lower, upper);
        var cutOff = new CutOff(new Vector(15, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutOff);

        plate.RegenerateCutOffs(new CutOffSettings { CutDirection = direction },
            new Dictionary<CutOff, IReadOnlyList<int>> { [cutOff] = [0, 2, 4] });

        AssertArrangement(plate, cutOff, lower, upper);
        // The program runs toward the origin, but the file lists segments ascending.
        var segments = CutOffSegments.Of(plate, cutOff);
        Assert.True(segments[0].Start < segments[1].Start && segments[1].Start < segments[2].Start);
    }

    [Fact]
    public void RegenerateCutOffs_ListOfWrongLength_PlacesSegmentsConsecutively()
    {
        var lower = TestHelpers.MakePartAt(10, 10, 10);
        var upper = TestHelpers.MakePartAt(10, 30, 10);
        var plate = TestHelpers.MakePlate(60, 120, lower, upper);
        var cutOff = new CutOff(new Vector(15, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutOff);

        plate.RegenerateCutOffs(new CutOffSettings(),
            new Dictionary<CutOff, IReadOnlyList<int>> { [cutOff] = [1, 7] });

        var segments = CutOffSegments.Of(plate, cutOff).Select(s => s.Part).ToArray();
        Assert.Equal(new[] { lower, segments[0], segments[1], segments[2], upper }, plate.Parts);
    }

    [Fact]
    public void RegenerateCutOffs_MoreSegmentsThanSaved_KeepTheLaterPartsAndCutOffsInPlace()
    {
        // Saved with settings that left one segment of the line (place 1) before the upper part
        // (place 2) and a second cut-off (place 3); the reader's settings regenerate three.
        var lower = TestHelpers.MakePartAt(10, 10, 10);
        var upper = TestHelpers.MakePartAt(10, 30, 10);
        var plate = TestHelpers.MakePlate(60, 120, lower, upper);
        var line = new CutOff(new Vector(15, 0), CutOffAxis.Vertical);
        var clear = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { EndLimit = 50 };
        plate.CutOffs.Add(line);
        plate.CutOffs.Add(clear);

        plate.RegenerateCutOffs(new CutOffSettings(), new Dictionary<CutOff, IReadOnlyList<int>>
        {
            [line] = [1],
            [clear] = [3],
        });

        var segments = CutOffSegments.Of(plate, line).Select(s => s.Part).ToArray();
        Assert.Equal(3, segments.Length);
        var other = Assert.Single(CutOffSegments.Of(plate, clear)).Part;
        Assert.Equal(new[] { lower, segments[0], segments[1], segments[2], upper, other }, plate.Parts);
    }

    [Fact]
    public void RegenerateCutOffs_FirstPlacesOnly_KeepTheSheetSeparatorLast()
    {
        // An older file recorded first places only. The line held one place when saved, before
        // the upper part and the separator; three segments now would put the separator's place
        // before the upper part, so a line splitting the sheet goes last instead.
        var lower = TestHelpers.MakePartAt(10, 10, 10);
        var upper = TestHelpers.MakePartAt(10, 30, 10);
        var plate = TestHelpers.MakePlate(60, 120, lower, upper);
        var line = new CutOff(new Vector(15, 0), CutOffAxis.Vertical) { EndLimit = 50 };
        var separator = new CutOff(new Vector(0, 50), CutOffAxis.Horizontal);
        plate.CutOffs.Add(line);
        plate.CutOffs.Add(separator);

        plate.RegenerateCutOffs(new CutOffSettings(), null, new Dictionary<CutOff, int>
        {
            [line] = 1,
            [separator] = 3,
        });

        var segments = CutOffSegments.Of(plate, line).Select(s => s.Part).ToArray();
        Assert.Equal(3, segments.Length);
        var last = Assert.Single(CutOffSegments.Of(plate, separator)).Part;
        Assert.Equal(new[] { lower, segments[0], segments[1], segments[2], upper, last }, plate.Parts);
    }

    [Fact]
    public void RegenerateCutOffs_IndexOverload_PlacesSegmentsConsecutively()
    {
        var lower = TestHelpers.MakePartAt(10, 10, 10);
        var upper = TestHelpers.MakePartAt(10, 30, 10);
        var plate = TestHelpers.MakePlate(60, 120, lower, upper);
        var cutOff = new CutOff(new Vector(15, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutOff);

        plate.RegenerateCutOffs(new CutOffSettings(), new Dictionary<CutOff, int> { [cutOff] = 1 });

        var segments = CutOffSegments.Of(plate, cutOff).Select(s => s.Part).ToArray();
        Assert.Equal(new[] { lower, segments[0], segments[1], segments[2], upper }, plate.Parts);
        Assert.All(segments, s => Assert.Equal(2, s.Program.Codes.Count));
    }

    [Fact]
    public void SegmentPrograms_SplitTheLineAtEachPierce()
    {
        var (plate, cutOff, _, _) = ThreeSegments();

        var programs = cutOff.SegmentPrograms();

        Assert.Equal(3, programs.Count);
        Assert.All(programs, p =>
        {
            Assert.IsType<RapidMove>(p.Codes[0]);
            Assert.IsType<LinearMove>(p.Codes[1]);
            Assert.Equal(2, p.Codes.Count);
        });
        Assert.Equal(cutOff.Drawing.Program.Codes, programs.SelectMany(p => p.Codes));
        Assert.Equal(programs.Count, plate.Parts.Count(p => p.BaseDrawing.IsCutOff));
    }

    // Two squares one above the other with a vertical line through both: a segment below,
    // between and above them.
    internal static (Plate Plate, CutOff CutOff, Part Lower, Part Upper) ThreeSegments()
    {
        var lower = TestHelpers.MakePartAt(10, 10, 10);
        var upper = TestHelpers.MakePartAt(10, 30, 10);
        var plate = TestHelpers.MakePlate(60, 120, lower, upper);
        var cutOff = new CutOff(new Vector(15, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutOff);
        plate.RegenerateCutOffs(new CutOffSettings());
        Assert.Equal(5, plate.Parts.Count);
        return (plate, cutOff, lower, upper);
    }

    private static void AssertArrangement(Plate plate, CutOff cutOff, Part lower, Part upper)
    {
        var segments = CutOffSegments.Of(plate, cutOff);
        Assert.Equal(3, segments.Count);
        Assert.Equal(new[] { segments[0].Part, lower, segments[1].Part, upper, segments[2].Part }, plate.Parts);
    }

    // The clearance outline is offset conservatively, so a trimmed end can sit a few ten
    // thousandths farther from the part than the nominal clearance.
    private static void AssertSegment(CutOffSegment segment, double start, double end, double spanStart, double spanEnd)
    {
        Assert.Equal(start, segment.Start, 0.001);
        Assert.Equal(end, segment.End, 0.001);
        Assert.Equal(spanStart, segment.SpanStart, 0.001);
        Assert.Equal(spanEnd, segment.SpanEnd, 0.001);
        Assert.True(segment.Part.BaseDrawing.IsCutOff);
    }
}
