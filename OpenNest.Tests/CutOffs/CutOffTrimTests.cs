using OpenNest.CNC;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// Trim, Extend and Swap Through-Cut at a line's ends change only its start and end limits (gaps
/// are in <see cref="CutOffGapTests"/>): a trim stops on
/// the crossing line's center, an extend runs to the next crossing line or the sheet edge, and a
/// swap makes the stopped line run through while the through line loses its far side.
/// </summary>
public class CutOffTrimTests
{
    private const double SeparatorY = 31.510593;
    private const double LastVerticalX = 70.221406;
    private static readonly CutOffSettings Settings = new();

    // N26-264 plate 1: three vertical lines stop at a full-length horizontal separator.
    private static (Plate Plate, CutOff Vertical, CutOff Horizontal) N26264()
    {
        var plate = new Plate(48, 96);
        foreach (var x in new[] { 35, 68.6, LastVerticalX })
            plate.CutOffs.Add(new CutOff(new Vector(x, 0), CutOffAxis.Vertical) { EndLimit = SeparatorY });
        var horizontal = new CutOff(new Vector(0, SeparatorY), CutOffAxis.Horizontal);
        plate.CutOffs.Add(horizontal);
        plate.RegenerateCutOffs(Settings);
        return (plate, plate.CutOffs[2], horizontal);
    }

    [Fact]
    public void Swap_AtTheN26264Junction_RunsTheVerticalThroughAndStopsTheHorizontalAtIt()
    {
        var (plate, vertical, horizontal) = N26264();

        var junction = CutOffTrim.JunctionAt(plate, new Vector(LastVerticalX + 0.1, SeparatorY), 0.5);
        Assert.Equal((horizontal, vertical), junction);
        var edit = CutOffTrim.Swap(plate, junction!.Value.Through, junction.Value.Stopper, Settings);
        Assert.True(edit.Succeeded, edit.Refusal);
        edit.Apply();

        Assert.Null(vertical.StartLimit);
        Assert.Null(vertical.EndLimit);
        Assert.Null(horizontal.StartLimit);
        Assert.Equal(LastVerticalX, horizontal.EndLimit);
        Assert.All(plate.CutOffs.Take(2), other => Assert.Equal(SeparatorY, other.EndLimit));
        Assert.Null(edit.Notice);
    }

    [Fact]
    public void Swap_PlacesTheNewSeparatorLast()
    {
        var (plate, vertical, horizontal) = N26264();
        Assert.Same(horizontal.Drawing, plate.Parts[^1].BaseDrawing);

        var edit = CutOffTrim.Swap(plate, horizontal, vertical, Settings);
        edit.Apply();
        plate.RegenerateCutOffs(Settings);
        PlateSequencing.PlaceCutOffs(plate, edit.Changes.Select(change => change.CutOff));

        var last = plate.Parts[^1];
        Assert.Same(vertical.Drawing, last.BaseDrawing);
        Assert.True(CutOff.SeparatesSheet(last, vertical, plate.BoundingBox(includeParts: false)));
    }

    [Fact]
    public void Swap_RefusesLinesThatBothRunThrough()
    {
        var plate = new Plate(48, 96);
        var vertical = new CutOff(new Vector(40, 0), CutOffAxis.Vertical);
        var horizontal = new CutOff(new Vector(0, 20), CutOffAxis.Horizontal);
        plate.CutOffs.Add(vertical);
        plate.CutOffs.Add(horizontal);

        Assert.Null(CutOffTrim.JunctionAt(plate, new Vector(40, 20), 0.5));
        Assert.Equal(CutOffTrim.NotAJunction, CutOffTrim.Swap(plate, vertical, horizontal, Settings).Refusal);
    }

    [Fact]
    public void Swap_RefusesWhenTheThroughLineWouldBeTooShort()
    {
        var plate = new Plate(48, 96);
        var vertical = new CutOff(new Vector(0.03, 0), CutOffAxis.Vertical) { EndLimit = 20 };
        var horizontal = new CutOff(new Vector(0, 20), CutOffAxis.Horizontal);
        plate.CutOffs.Add(vertical);
        plate.CutOffs.Add(horizontal);

        Assert.Equal(CutOffTrim.TooShort, CutOffTrim.Swap(plate, horizontal, vertical, Settings).Refusal);
    }

    [Fact]
    public void Trim_RemovesTheFarPieceUpToTheCrossingLine()
    {
        var (plate, _, horizontal) = N26264();

        var edit = CutOffTrim.Trim(plate, horizontal, new Vector(80, SeparatorY), Settings);
        edit.Apply();

        Assert.Null(horizontal.StartLimit);
        Assert.Equal(LastVerticalX, horizontal.EndLimit);
    }

    [Fact]
    public void Trim_RemovesTheOriginPieceUpToTheCrossingLine()
    {
        var (plate, _, horizontal) = N26264();

        var edit = CutOffTrim.Trim(plate, horizontal, new Vector(10, SeparatorY), Settings);
        edit.Apply();

        Assert.Equal(35, horizontal.StartLimit);
        Assert.Null(horizontal.EndLimit);
    }

    [Fact]
    public void Trim_RefusalChangesNothing()
    {
        var plate = new Plate(48, 96);
        var lone = new CutOff(new Vector(40, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(lone);

        var refused = CutOffTrim.Trim(plate, lone, new Vector(40, 10), Settings);
        Assert.Empty(refused.Changes);
        Assert.Throws<InvalidOperationException>(refused.Apply);
    }

    [Fact]
    public void Trim_RefusesALineNothingCrosses()
    {
        var plate = new Plate(48, 96);
        var lone = new CutOff(new Vector(40, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(lone);

        Assert.Equal(CutOffTrim.NothingToTrimTo, CutOffTrim.Trim(plate, lone, new Vector(40, 10), Settings).Refusal);
    }

    [Fact]
    public void Trim_IgnoresALineThatStopsShortOfTheTarget()
    {
        var (plate, _, _) = N26264();
        var high = new CutOff(new Vector(0, 40), CutOffAxis.Horizontal);
        plate.CutOffs.Add(high);

        // The verticals end at the separator, below this line.
        Assert.Equal(CutOffTrim.NothingToTrimTo, CutOffTrim.Trim(plate, high, new Vector(80, 40), Settings).Refusal);
    }

    [Fact]
    public void Trim_RefusesToLeaveLessThanTheMinimumSegment()
    {
        var plate = new Plate(48, 96);
        var horizontal = new CutOff(new Vector(0, 10), CutOffAxis.Horizontal);
        plate.CutOffs.Add(horizontal);
        plate.CutOffs.Add(new CutOff(new Vector(0.03, 0), CutOffAxis.Vertical));

        Assert.Equal(CutOffTrim.TooShort, CutOffTrim.Trim(plate, horizontal, new Vector(50, 10), Settings).Refusal);
    }

    [Fact]
    public void Edits_RefuseACutOffFromAnotherPlate()
    {
        var (plate, _, _) = N26264();
        var stranger = new CutOff(new Vector(10, 0), CutOffAxis.Vertical);

        Assert.Equal(CutOffTrim.NotOnPlate, CutOffTrim.Trim(plate, stranger, new Vector(10, 5), Settings).Refusal);
        Assert.Equal(CutOffTrim.NotOnPlate, CutOffTrim.Extend(plate, stranger, new Vector(10, 5)).Refusal);
        Assert.Equal(CutOffTrim.NotOnPlate, CutOffTrim.Swap(plate, stranger, plate.CutOffs[3], Settings).Refusal);
    }

    [Fact]
    public void Extend_WithNothingBeyond_RunsToTheSheetEdge()
    {
        var (plate, vertical, _) = N26264();

        var edit = CutOffTrim.Extend(plate, vertical, new Vector(LastVerticalX, 30));
        edit.Apply();

        Assert.Null(vertical.StartLimit);
        Assert.Null(vertical.EndLimit);
    }

    [Fact]
    public void Extend_SkipsTheLineAtItsEndAndStopsAtTheNextOne()
    {
        var (plate, vertical, _) = N26264();
        plate.CutOffs.Add(new CutOff(new Vector(0, 40), CutOffAxis.Horizontal));

        var edit = CutOffTrim.Extend(plate, vertical, new Vector(LastVerticalX, 30));
        edit.Apply();

        Assert.Equal(40, vertical.EndLimit);
    }

    [Fact]
    public void Extend_GrowsTheStartWhenPickedNearIt()
    {
        var plate = new Plate(48, 96);
        var vertical = new CutOff(new Vector(40, 0), CutOffAxis.Vertical) { StartLimit = 20, EndLimit = 30 };
        plate.CutOffs.Add(vertical);
        plate.CutOffs.Add(new CutOff(new Vector(0, 5), CutOffAxis.Horizontal));

        CutOffTrim.Extend(plate, vertical, new Vector(40, 21)).Apply();

        Assert.Equal(5, vertical.StartLimit);
        Assert.Equal(30, vertical.EndLimit);
    }

    [Fact]
    public void Extend_RefusesAnEndAlreadyAtTheSheetEdge()
    {
        var (plate, _, horizontal) = N26264();

        Assert.Equal(CutOffTrim.AtSheetEdge, CutOffTrim.Extend(plate, horizontal, new Vector(90, SeparatorY)).Refusal);
    }

    [Fact]
    public void HeadLockedOvercut_FollowsTheFarEdgeLimit()
    {
        var (plate, vertical, horizontal) = N26264();
        horizontal.HeadLockOvercut = 1.5;
        vertical.HeadLockOvercut = 1.5;

        var trim = CutOffTrim.Trim(plate, horizontal, new Vector(80, SeparatorY), Settings);
        Assert.Equal(CutOffTrim.OvercutOff, trim.Notice);
        var extend = CutOffTrim.Extend(plate, vertical, new Vector(LastVerticalX, 30));
        Assert.Equal(CutOffTrim.OvercutOn, extend.Notice);

        trim.Apply();
        plate.RegenerateCutOffs(Settings);
        Assert.DoesNotContain(horizontal.Drawing.Program.Codes, HeadHeightLock.IsLock);
    }

    [Fact]
    public void OutlinedSheet_ExtendStillCutsOnlyMaterial()
    {
        // An L-shaped sheet: material is 24 tall right of X 48.
        var plate = new Plate { Outline = SheetOutlineTests.EllSheet() };
        var vertical = new CutOff(new Vector(60, 0), CutOffAxis.Vertical) { EndLimit = 12 };
        plate.CutOffs.Add(vertical);
        plate.CutOffs.Add(new CutOff(new Vector(0, 12), CutOffAxis.Horizontal));

        CutOffTrim.Extend(plate, vertical, new Vector(60, 11)).Apply();
        plate.RegenerateCutOffs(Settings);

        Assert.Null(vertical.EndLimit);
        var top = vertical.Drawing.Program.Codes.OfType<LinearMove>().Max(move => move.EndPoint.Y);
        Assert.Equal(24, top, 6);
    }
}
