using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests;

/// <summary>
/// <see cref="PlateRemnant.Expected"/>: a full-span cut-off clear of the parts defines the drop,
/// otherwise the largest salvage edge offcut, otherwise none.
/// </summary>
public class PlateRemnantTests
{
    // A 48 x 96 sheet (Y x X) with one 10 x 10 part in the origin corner. The largest edge
    // offcut is the 86 x 48 strip to the right of the part.
    private static Plate PlateWithPart(Drawing drawing = null)
    {
        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(drawing ?? new RectangleShape { Name = "SQ", Length = 10, Width = 10 }.GetDrawing(),
            new Vector(0, 0)));
        return plate;
    }

    private static (double X, double Y, double Length, double Width, PlateRemnantSource Source) Of(PlateRemnant remnant) =>
        (remnant.Box.X, remnant.Box.Y, remnant.Box.Length, remnant.Box.Width, remnant.Source);

    [Fact]
    public void NoCutOff_IsTheLargestEdgeOffcut()
    {
        var remnant = PlateRemnant.Expected(PlateWithPart(), 12);

        Assert.Equal((10.0, 0.0, 86.0, 48.0, PlateRemnantSource.Offcut), Of(remnant));
    }

    [Fact]
    public void Offcut_KeepsThePartSpacingAndEdgeSpacing()
    {
        var plate = PlateWithPart();
        plate.PartSpacing = 0.5;
        plate.EdgeSpacing = new Spacing(1, 1, 1, 1);

        var remnant = PlateRemnant.Expected(plate, 12);

        Assert.Equal((10.5, 1.0, 84.5, 46.0, PlateRemnantSource.Offcut), Of(remnant));
    }

    [Fact]
    public void FullSpanCutOff_WinsOverALargerOffcut()
    {
        var plate = PlateWithPart();
        plate.CutOffs.Add(new CutOff(new Vector(60, 0), CutOffAxis.Vertical));

        var remnant = PlateRemnant.Expected(plate, 12);

        Assert.Equal((60.0, 0.0, 36.0, 48.0, PlateRemnantSource.CutOff), Of(remnant));
    }

    [Fact]
    public void CutOff_SubtractsTheKerf()
    {
        var plate = PlateWithPart();
        plate.CuttingParameters = new CuttingParameters { Kerf = 0.25 };
        plate.CutOffs.Add(new CutOff(new Vector(0, 30), CutOffAxis.Horizontal));

        var remnant = PlateRemnant.Expected(plate, 12);

        Assert.Equal((0.0, 30.25, 96.0, 17.75, PlateRemnantSource.CutOff), Of(remnant));
    }

    [Fact]
    public void CutOffThroughParts_IsNotADropEdge()
    {
        // The strip line at X = 5 crosses the part; only the separator at 60 frees material.
        var plate = PlateWithPart();
        plate.CutOffs.Add(new CutOff(new Vector(5, 0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(60, 0), CutOffAxis.Vertical));

        var remnant = PlateRemnant.Expected(plate, 12);

        Assert.Equal((60.0, 0.0, 36.0, 48.0, PlateRemnantSource.CutOff), Of(remnant));
    }

    [Fact]
    public void PiecesBetweenCutOffs_ReportTheLargest()
    {
        var plate = PlateWithPart();
        plate.CuttingParameters = new CuttingParameters { Kerf = 0.25 };
        plate.CutOffs.Add(new CutOff(new Vector(20, 0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(70, 0), CutOffAxis.Vertical));

        var remnant = PlateRemnant.Expected(plate, 12);

        // 20..70 less kerf at both lines beats 70..96 less one kerf.
        Assert.Equal((20.25, 0.0, 49.5, 48.0, PlateRemnantSource.CutOff), Of(remnant));
    }

    [Fact]
    public void PartialSpanCutOff_FallsThroughToTheOffcut()
    {
        var plate = PlateWithPart();
        plate.CutOffs.Add(new CutOff(new Vector(60, 0), CutOffAxis.Vertical) { EndLimit = 24 });

        var remnant = PlateRemnant.Expected(plate, 12);

        Assert.Equal((10.0, 0.0, 86.0, 48.0, PlateRemnantSource.Offcut), Of(remnant));
    }

    [Fact]
    public void HeadLockedOvercut_UsesTheNominalPosition()
    {
        var plate = PlateWithPart();
        plate.CutOffs.Add(new CutOff(new Vector(0, 30), CutOffAxis.Horizontal) { HeadLockOvercut = 1.5 });
        plate.RegenerateCutOffs(new CutOffSettings());

        var remnant = PlateRemnant.Expected(plate, 12);

        Assert.Equal((0.0, 30.0, 96.0, 18.0, PlateRemnantSource.CutOff), Of(remnant));
    }

    [Fact]
    public void CutOffDropBelowTheMinimum_FallsThroughToTheOffcut()
    {
        var plate = PlateWithPart();
        plate.CutOffs.Add(new CutOff(new Vector(90, 0), CutOffAxis.Vertical));

        var remnant = PlateRemnant.Expected(plate, 12);

        Assert.Equal((10.0, 0.0, 86.0, 48.0, PlateRemnantSource.Offcut), Of(remnant));
    }

    [Fact]
    public void NothingMeetsTheMinimum_IsNone()
    {
        Assert.Null(PlateRemnant.Expected(PlateWithPart(), 50));
    }

    [Fact]
    public void EmptyPlate_IsNone()
    {
        var plate = new Plate(48, 96);
        plate.CutOffs.Add(new CutOff(new Vector(60, 0), CutOffAxis.Vertical));

        Assert.Null(PlateRemnant.Expected(plate, 0));
    }

    [Fact]
    public void Marks_DoNotBoundMaterial()
    {
        // A scribe line reaching X = 50 is not material: the offcut and the cut-off drop both
        // start at the 10-inch part edge or the cut-off line.
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(10, 0);
        program.LineTo(10, 10);
        program.LineTo(0, 10);
        program.LineTo(0, 0);
        program.MoveTo(1, 5);
        program.Codes.Add(new LinearMove(50, 5) { Layer = LayerType.Scribe });
        var marked = new Drawing("MARKED", program);

        Assert.Equal((10.0, 0.0, 86.0, 48.0, PlateRemnantSource.Offcut), Of(PlateRemnant.Expected(PlateWithPart(marked), 12)));

        var cut = PlateWithPart(marked);
        cut.CutOffs.Add(new CutOff(new Vector(30, 0), CutOffAxis.Vertical));
        Assert.Equal((30.0, 0.0, 66.0, 48.0, PlateRemnantSource.CutOff), Of(PlateRemnant.Expected(cut, 12)));
    }
}
