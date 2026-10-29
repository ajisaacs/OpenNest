using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// Trimming a piece between two crossing lines leaves a gap from one crossing centre to the other,
/// and the line splits into segments as it does around a part. Extend closes a gap; every consumer
/// of a cut-off's limits treats a gap as uncut.
/// </summary>
public class CutOffGapTests
{
    private const double SeparatorY = 31.510593;
    private const double LastVerticalX = 70.221406;
    private static readonly CutOffSettings Settings = new();

    // N26-264 plate 1: three vertical lines stop at a full-length horizontal separator.
    private static (Plate Plate, CutOff Horizontal) N26264()
    {
        var plate = new Plate(48, 96);
        foreach (var x in new[] { 35, 68.6, LastVerticalX })
            plate.CutOffs.Add(new CutOff(new Vector(x, 0), CutOffAxis.Vertical) { EndLimit = SeparatorY });
        var horizontal = new CutOff(new Vector(0, SeparatorY), CutOffAxis.Horizontal);
        plate.CutOffs.Add(horizontal);
        plate.RegenerateCutOffs(Settings);
        return (plate, horizontal);
    }

    // The horizontal with the piece between X 35 and 68.6 trimmed out.
    private static (Plate Plate, CutOff Horizontal) Gapped()
    {
        var (plate, horizontal) = N26264();
        var edit = CutOffTrim.Trim(plate, horizontal, new Vector(50, SeparatorY), Settings);
        Assert.True(edit.Succeeded, edit.Refusal);
        edit.Apply();
        plate.RegenerateCutOffs(Settings);
        return (plate, horizontal);
    }

    [Fact]
    public void Trim_MiddlePiece_LeavesAGapBetweenTheCrossingCentres()
    {
        var (plate, horizontal) = Gapped();

        Assert.Null(horizontal.StartLimit);
        Assert.Null(horizontal.EndLimit);
        Assert.Equal(new[] { new CutOffGap(35, 68.6) }, horizontal.Gaps);

        // Two segments, each its own part with its own place in the sequence.
        var segments = CutOffSegments.Of(plate, horizontal);
        Assert.Equal(2, segments.Count);
        Assert.Equal(35, segments[0].End, 6);
        Assert.Equal(68.6, segments[1].Start, 6);
        Assert.Equal(2, segments.Select(s => plate.Parts.IndexOf(s.Part)).Distinct().Count());
        var sheet = plate.BoundingBox(includeParts: false);
        Assert.All(segments, s => Assert.False(CutOff.SeparatesSheet(s.Part, horizontal, sheet)));
    }

    [Fact]
    public void Trim_PieceBesideAGap_MergesWithIt()
    {
        var (plate, horizontal) = Gapped();

        CutOffTrim.Trim(plate, horizontal, new Vector(69.5, SeparatorY), Settings).Apply();

        Assert.Equal(new[] { new CutOffGap(35, LastVerticalX) }, horizontal.Gaps);
    }

    [Fact]
    public void Trim_EndPieceBesideAGap_MovesTheLimitPastTheGap()
    {
        var (plate, horizontal) = Gapped();

        CutOffTrim.Trim(plate, horizontal, new Vector(10, SeparatorY), Settings).Apply();

        Assert.Equal(68.6, horizontal.StartLimit);
        Assert.Null(horizontal.EndLimit);
        Assert.Empty(horizontal.Gaps);
    }

    [Fact]
    public void Trim_InsideAGap_IsRefused()
    {
        var (plate, horizontal) = Gapped();

        Assert.Equal(CutOffTrim.InGap, CutOffTrim.Trim(plate, horizontal, new Vector(50, SeparatorY), Settings).Refusal);
    }

    [Fact]
    public void Trim_RefusesAGapThatLeavesAPieceShorterThanTheMinimum()
    {
        var plate = new Plate(48, 96);
        var horizontal = new CutOff(new Vector(0, 10), CutOffAxis.Horizontal) { Gaps = [new CutOffGap(20, 35)] };
        plate.CutOffs.Add(horizontal);
        foreach (var x in new[] { 20, 35, 35.03, 60 })
            plate.CutOffs.Add(new CutOff(new Vector(x, 0), CutOffAxis.Vertical));

        // Removing 35.03..60 would leave 35..35.03 on its own.
        Assert.Equal(CutOffTrim.TooShort, CutOffTrim.Trim(plate, horizontal, new Vector(50, 10), Settings).Refusal);
    }

    [Fact]
    public void Extend_AtAGapEdge_ClosesTheGap()
    {
        var (plate, horizontal) = Gapped();

        var edit = CutOffTrim.Extend(plate, horizontal, new Vector(34, SeparatorY));
        Assert.True(edit.Succeeded, edit.Refusal);
        var (removed, added) = CutOffTrim.Difference(plate, Assert.Single(edit.Changes));
        Assert.Empty(removed);
        Assert.Equal(new[] { (35.0, 68.6) }, added);
        edit.Apply();

        Assert.Empty(horizontal.Gaps);
        Assert.Null(horizontal.StartLimit);
        Assert.Null(horizontal.EndLimit);
    }

    [Fact]
    public void Extend_FromTheFarSideOfAGap_ClosesItToo()
    {
        var (plate, horizontal) = Gapped();

        CutOffTrim.Extend(plate, horizontal, new Vector(69, SeparatorY)).Apply();

        Assert.Empty(horizontal.Gaps);
    }

    [Fact]
    public void Extend_AwayFromAGap_StillRunsTheLineEnd()
    {
        var (plate, horizontal) = Gapped();
        horizontal.EndLimit = 80;

        CutOffTrim.Extend(plate, horizontal, new Vector(79, SeparatorY)).Apply();

        Assert.Null(horizontal.EndLimit);
        Assert.Equal(new[] { new CutOffGap(35, 68.6) }, horizontal.Gaps);
    }

    [Fact]
    public void Swap_RefusesAGapAtTheJunction()
    {
        var (plate, horizontal) = Gapped();
        CutOffTrim.Trim(plate, horizontal, new Vector(69.5, SeparatorY), Settings).Apply();
        var vertical = plate.CutOffs[2];

        Assert.Null(CutOffTrim.JunctionAt(plate, new Vector(LastVerticalX, SeparatorY), 0.5));
        Assert.Equal(CutOffTrim.GapAtJunction, CutOffTrim.Swap(plate, horizontal, vertical, Settings).Refusal);
    }

    [Fact]
    public void Swap_DropsGapsBeyondTheNewEnd()
    {
        var plate = new Plate(48, 96);
        var vertical = new CutOff(new Vector(20, 0), CutOffAxis.Vertical) { EndLimit = 10 };
        var horizontal = new CutOff(new Vector(0, 10), CutOffAxis.Horizontal) { Gaps = [new CutOffGap(50, 60)] };
        plate.CutOffs.Add(vertical);
        plate.CutOffs.Add(horizontal);
        plate.CutOffs.Add(new CutOff(new Vector(50, 0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(60, 0), CutOffAxis.Vertical));

        var edit = CutOffTrim.Swap(plate, horizontal, vertical, Settings);
        Assert.True(edit.Succeeded, edit.Refusal);
        edit.Apply();

        Assert.Equal(20, horizontal.EndLimit);
        Assert.Empty(horizontal.Gaps);
        Assert.Null(vertical.EndLimit);
    }

    [Fact]
    public void Gaps_AreSortedAndMergedAndDropEmptyOnes()
    {
        var cutOff = new CutOff(new Vector(0, 10), CutOffAxis.Horizontal)
        {
            Gaps = [new CutOffGap(50, 60), new CutOffGap(10, 20), new CutOffGap(55, 70), new CutOffGap(30, 30),
                new CutOffGap(double.NaN, 5)],
        };

        Assert.Equal(new[] { new CutOffGap(10, 20), new CutOffGap(50, 70) }, cutOff.Gaps);
        Assert.Equal(new[] { (0.0, 10.0), (20.0, 50.0), (70.0, 96.0) }, cutOff.Pieces(0, 96));
    }

    [Fact]
    public void Segments_SpansStopAtTheGap()
    {
        var (plate, horizontal) = Gapped();

        var segments = CutOffSegments.Of(plate, horizontal);

        Assert.Equal(35, segments[0].SpanEnd, 6);
        Assert.Equal(68.6, segments[1].SpanStart, 6);
    }

    [Fact]
    public void Dependencies_APartInsideAGapIsNotCrossed()
    {
        var plate = new Box(0, 0, 96, 48);
        var part = new Box(40, 25, 20, 10);
        var gaps = new[] { new CutOffGap(35, 68.6) };

        Assert.True(CuttingDependencyGraph.CutOffCrosses(CutOffAxis.Horizontal, new Vector(0, SeparatorY),
            null, null, part, plate));
        Assert.False(CuttingDependencyGraph.CutOffCrosses(CutOffAxis.Horizontal, new Vector(0, SeparatorY),
            null, null, part, plate, gaps));
        Assert.True(CuttingDependencyGraph.CutOffCrosses(CutOffAxis.Horizontal, new Vector(0, SeparatorY),
            null, null, new Box(30, 25, 10, 10), plate, gaps));
    }

    [Fact]
    public void Sequencing_ASegmentDoesNotWaitForAPartInTheGap()
    {
        var (plate, horizontal) = N26264();
        var part = new Part(new RectangleShape { Name = "R", Length = 20, Width = 10 }.GetDrawing(), new Vector(40, 25));
        plate.Parts.Insert(0, part);
        CutOffTrim.Trim(plate, horizontal, new Vector(50, SeparatorY), Settings).Apply();
        plate.RegenerateCutOffs(Settings);
        PlateSequencing.PlaceCutOffs(plate, [horizontal]);

        // No segment is pulled ahead of the part the gap leaves uncut.
        Assert.Same(part, plate.Parts[0]);
    }

    [Fact]
    public void CuttingState_AGapChangeMakesItStale()
    {
        var (plate, horizontal) = N26264();
        var state = PlateCuttingState.Capture(plate);
        var stamp = OverlapGeometryStamp.Capture(plate);
        Assert.True(state.IsCurrent());
        Assert.True(stamp.Matches(plate));

        horizontal.Gaps = [new CutOffGap(35, 68.6)];

        Assert.Equal("Cutoff 4 definition changed.", state.Difference());
        Assert.False(stamp.Matches(plate));
    }

    [Fact]
    public void ClampCheck_AClampInsideAGapIsNotReported()
    {
        var plate = new Plate(24, 48);
        plate.ClampZones.Add(new ClampZone("front", new Box(10, 0, 1.5, 0.625)));
        var cut = new CutOff(new Vector(10.5, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cut);
        Assert.Single(ClampCutDiagnostics.Find(plate));

        cut.Gaps = [new CutOffGap(0, 5)];

        Assert.Empty(ClampCutDiagnostics.Find(plate));
    }

    [Fact]
    public void Remnant_AGappedLineDoesNotCutADropFree()
    {
        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(new RectangleShape { Name = "SQ", Length = 10, Width = 10 }.GetDrawing(), new Vector(0, 0)));
        var cut = new CutOff(new Vector(60, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cut);
        Assert.Equal(PlateRemnantSource.CutOff, PlateRemnant.Expected(plate, 12).Source);

        cut.Gaps = [new CutOffGap(20, 30)];

        Assert.Equal(PlateRemnantSource.Offcut, PlateRemnant.Expected(plate, 12).Source);
    }

    [Fact]
    public void AutomaticCutOffs_AGappedSameLineCutIsALimitedConflict()
    {
        var plate = new Plate(81, 120) { PartSpacing = 0.5, Quantity = 2 };
        plate.Parts.Add(new Part(new RectangleShape { Name = "R", Length = 70, Width = 20 }.GetDrawing(), new Vector(10, 20)));
        plate.CutOffs.Add(new CutOff(new Vector(35, 0), CutOffAxis.Vertical) { Gaps = [new CutOffGap(50, 60)] });

        var plan = AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions { Spacing = 35 }, Settings);

        Assert.True(plan.HasBlockingDiagnostics);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.LimitedCutOffConflict);
    }

    [Fact]
    public void OutlinedSheet_TheGapStaysUncut()
    {
        // An L-shaped sheet: material is 24 tall right of X 48.
        var plate = new Plate { Outline = SheetOutlineTests.EllSheet() };
        var horizontal = new CutOff(new Vector(0, 12), CutOffAxis.Horizontal);
        plate.CutOffs.Add(horizontal);
        plate.CutOffs.Add(new CutOff(new Vector(20, 0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(70, 0), CutOffAxis.Vertical));

        CutOffTrim.Trim(plate, horizontal, new Vector(40, 12), Settings).Apply();
        plate.RegenerateCutOffs(Settings);

        Assert.Equal(new[] { new CutOffGap(20, 70) }, horizontal.Gaps);
        var program = horizontal.Drawing.Program;
        for (var i = 0; i + 1 < program.Codes.Count; i += 2)
        {
            var from = Assert.IsType<RapidMove>(program.Codes[i]).EndPoint.X;
            var to = Assert.IsType<LinearMove>(program.Codes[i + 1]).EndPoint.X;
            Assert.True(System.Math.Max(from, to) <= 20 + 1e-9 || System.Math.Min(from, to) >= 70 - 1e-9,
                $"A cut from {from} to {to} runs into the gap.");
        }
    }
}
