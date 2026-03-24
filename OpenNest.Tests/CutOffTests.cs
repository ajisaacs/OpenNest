using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class CutOffTests
{
    [Fact]
    public void Drawing_IsCutOff_DefaultsFalse()
    {
        var drawing = new Drawing("test", new Program());
        Assert.False(drawing.IsCutOff);
    }

    [Fact]
    public void Plate_CutOffPart_DoesNotIncrementQuantity()
    {
        var drawing = new Drawing("cutoff", new Program()) { IsCutOff = true };
        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(drawing));
        Assert.Equal(0, drawing.Quantity.Nested);
    }

    [Fact]
    public void Plate_Utilization_ExcludesCutOffParts()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Geometry.Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(0, 0)));
        var realDrawing = new Drawing("real", pgm);
        var cutoffDrawing = new Drawing("cutoff", new Program()) { IsCutOff = true };

        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(realDrawing));
        plate.Parts.Add(new Part(cutoffDrawing));

        var utilization = plate.Utilization();
        var expected = realDrawing.Area / plate.Area();
        Assert.Equal(expected, utilization, 5);
    }

    [Fact]
    public void Plate_HasOverlappingParts_SkipsCutOffParts()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Geometry.Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Geometry.Vector(0, 0)));

        var realDrawing = new Drawing("real", pgm);
        var cutoffDrawing = new Drawing("cutoff", pgm) { IsCutOff = true };

        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(realDrawing));
        plate.Parts.Add(new Part(cutoffDrawing));

        var hasOverlap = plate.HasOverlappingParts(out var pts);
        Assert.False(hasOverlap);
    }

    [Fact]
    public void CutOff_VerticalCut_GeneratesFullLineOnEmptyPlate()
    {
        var plate = new Plate(100, 50);
        var settings = new CutOffSettings();
        var cutoff = new CutOff(new Vector(25, 20), CutOffAxis.Vertical);

        cutoff.Regenerate(plate, settings);

        Assert.NotNull(cutoff.Drawing);
        Assert.True(cutoff.Drawing.IsCutOff);
        Assert.True(cutoff.Drawing.Program.Codes.Count > 0);
    }

    [Fact]
    public void CutOff_HorizontalCut_GeneratesFullLineOnEmptyPlate()
    {
        var plate = new Plate(100, 50);
        var settings = new CutOffSettings();
        var cutoff = new CutOff(new Vector(25, 20), CutOffAxis.Horizontal);

        cutoff.Regenerate(plate, settings);

        var codes = cutoff.Drawing.Program.Codes;
        Assert.Equal(2, codes.Count);
    }

    [Fact]
    public void CutOff_VerticalCut_TrimsAroundPart()
    {
        // Create a 10x10 part at the origin, then move it to (20,20)
        // so the bounding box is Box(20,20,10,10) and doesn't span the origin.
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("sq", pgm);

        var plate = new Plate(50, 50);
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new Vector(20, 20);
        plate.Parts.Add(part);

        // Vertical cut at X=25 runs along Y from 0 to 50.
        // Part BB at (20,20,10,10) with clearance 1 → exclusion X=[19,31], Y=[19,31].
        // X=25 is within [19,31] so exclusion applies: skip Y=[19,31].
        // Segments: (0, 19) and (31, 50) → 2 segments → 4 codes.
        var settings = new CutOffSettings { PartClearance = 1.0 };
        var cutoff = new CutOff(new Vector(25, 10), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, settings);

        var codes = cutoff.Drawing.Program.Codes;
        Assert.Equal(4, codes.Count);
    }

    [Fact]
    public void CutOff_ShortSegment_FilteredByMinLength()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(20, 0.02)));
        pgm.Codes.Add(new LinearMove(new Vector(30, 0.02)));
        pgm.Codes.Add(new LinearMove(new Vector(30, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(20, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(20, 0.02)));
        var drawing = new Drawing("sq", pgm);

        var plate = new Plate(50, 50);
        plate.Parts.Add(new Part(drawing));

        var settings = new CutOffSettings { PartClearance = 0.0, MinSegmentLength = 0.05 };
        var cutoff = new CutOff(new Vector(25, 10), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, settings);

        var rapidCount = cutoff.Drawing.Program.Codes.Count(c => c is RapidMove);
        var lineCount = cutoff.Drawing.Program.Codes.Count(c => c is LinearMove);
        Assert.Equal(rapidCount, lineCount);
    }

    [Fact]
    public void CutOff_Overtravel_ExtendsFarEnd()
    {
        var plate = new Plate(100, 50);
        var settings = new CutOffSettings { Overtravel = 2.0 };
        var cutoff = new CutOff(new Vector(25, 10), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, settings);

        // Plate(100, 50) = Width=100, Length=50. Vertical cut runs along Y (Width axis).
        // BoundingBox Y extent = Size.Width = 100. With 2" overtravel = 102.
        // Default AwayFromOrigin: RapidMove to near end (0), LinearMove to far end (102).
        var linearMoves = cutoff.Drawing.Program.Codes.OfType<LinearMove>().ToList();
        Assert.Single(linearMoves);
        Assert.Equal(102.0, linearMoves[0].EndPoint.Y, 5);
    }

    [Fact]
    public void CutOff_StartLimit_TruncatesNearEnd()
    {
        var plate = new Plate(100, 50);
        var settings = new CutOffSettings();
        var cutoff = new CutOff(new Vector(25, 10), CutOffAxis.Vertical)
        {
            StartLimit = 20.0
        };
        cutoff.Regenerate(plate, settings);

        // AwayFromOrigin: RapidMove to near end (StartLimit=20), LinearMove to far end (100).
        var rapidMoves = cutoff.Drawing.Program.Codes.OfType<RapidMove>().ToList();
        Assert.Single(rapidMoves);
        Assert.Equal(20.0, rapidMoves[0].EndPoint.Y, 5);
    }

    [Fact]
    public void CutOff_EndLimit_TruncatesFarEnd()
    {
        var plate = new Plate(100, 50);
        var settings = new CutOffSettings();
        var cutoff = new CutOff(new Vector(25, 10), CutOffAxis.Vertical)
        {
            EndLimit = 80.0
        };
        cutoff.Regenerate(plate, settings);

        // AwayFromOrigin: RapidMove to near end (0), LinearMove to far end (EndLimit=80).
        var linearMoves = cutoff.Drawing.Program.Codes.OfType<LinearMove>().ToList();
        Assert.Single(linearMoves);
        Assert.Equal(80.0, linearMoves[0].EndPoint.Y, 5);
    }

    [Fact]
    public void CutOff_BothLimits_LShapedCornerCut()
    {
        var plate = new Plate(60, 120);
        var settings = new CutOffSettings { PartClearance = 0 };

        var hCut = new CutOff(new Vector(85, 30), CutOffAxis.Horizontal)
        {
            EndLimit = 85.0
        };
        hCut.Regenerate(plate, settings);

        var vCut = new CutOff(new Vector(85, 30), CutOffAxis.Vertical)
        {
            StartLimit = 30.0
        };
        vCut.Regenerate(plate, settings);

        Assert.True(hCut.Drawing.Program.Codes.Count > 0);
        Assert.True(vCut.Drawing.Program.Codes.Count > 0);
    }

    [Fact]
    public void Plate_RegenerateCutOffs_MaterializesParts()
    {
        var plate = new Plate(100, 50);
        var cutoff = new CutOff(new Geometry.Vector(25, 10), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutoff);

        plate.RegenerateCutOffs(new CutOffSettings());

        Assert.Single(plate.Parts);
        Assert.True(plate.Parts[0].BaseDrawing.IsCutOff);
    }

    [Fact]
    public void Plate_RegenerateCutOffs_ReplacesOldParts()
    {
        var plate = new Plate(100, 50);
        var cutoff = new CutOff(new Geometry.Vector(25, 10), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutoff);

        var settings = new CutOffSettings();
        plate.RegenerateCutOffs(settings);
        plate.RegenerateCutOffs(settings);

        Assert.Single(plate.Parts);
    }

    [Fact]
    public void Plate_RegenerateCutOffs_DoesNotAffectRegularParts()
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Geometry.Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Geometry.Vector(5, 5)));
        var drawing = new Drawing("real", pgm);

        var plate = new Plate(100, 50);
        plate.Parts.Add(new Part(drawing));

        var cutoff = new CutOff(new Geometry.Vector(25, 10), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutoff);

        plate.RegenerateCutOffs(new CutOffSettings());

        Assert.Equal(2, plate.Parts.Count);
        Assert.False(plate.Parts[0].BaseDrawing.IsCutOff);
        Assert.True(plate.Parts[1].BaseDrawing.IsCutOff);
    }
}
