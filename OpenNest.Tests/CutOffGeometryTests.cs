using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class CutOffGeometryTests
{
    private static readonly CutOffSettings ZeroClearance = new() { PartClearance = 0.0 };

    private static Program MakeSquare(double size)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return pgm;
    }

    private static Program MakeCircle(double radius)
    {
        // Rapid to (radius, 0) relative to center at (0, 0),
        // then full-circle arc back to same point.
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(radius, 0)));
        pgm.Codes.Add(new ArcMove(new Vector(radius, 0), new Vector(0, 0)));
        return pgm;
    }

    private static Program MakeDiamond(double halfSize)
    {
        // Diamond: points at (half,0), (2*half,half), (half,2*half), (0,half)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(halfSize, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(halfSize * 2, halfSize)));
        pgm.Codes.Add(new LinearMove(new Vector(halfSize, halfSize * 2)));
        pgm.Codes.Add(new LinearMove(new Vector(0, halfSize)));
        pgm.Codes.Add(new LinearMove(new Vector(halfSize, 0)));
        return pgm;
    }

    private static Program MakeTriangle(double width, double height)
    {
        // Right triangle: (0,0) -> (width,0) -> (0,height) -> (0,0)
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(width, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, height)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return pgm;
    }

    [Fact]
    public void Square_GeometryExclusionMatchesBoundingBox()
    {
        // For a square, geometry and BB should produce the same exclusion.
        var drawing = new Drawing("sq", MakeSquare(20));
        var plate = new Plate(100, 100);
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new Vector(10, 10);
        plate.Parts.Add(part);

        // Vertical cut at X=20 (through the middle of the square).
        // BB exclusion Y = [10, 30]. Geometry should give the same.
        var cutoff = new CutOff(new Vector(20, 0), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, ZeroClearance);

        var codes = cutoff.Drawing.Program.Codes;
        // Two segments: before and after the square → 4 codes
        Assert.Equal(4, codes.Count);
    }

    [Fact]
    public void Circle_GeometryExclusionNarrowerThanBoundingBox()
    {
        // Circle radius=10, center at (10,10) after placement.
        // BB = (0,0,20,20). Vertical cut at X=2 clips the circle edge.
        // BB would exclude full Y=[0,20].
        // Geometry: at X=2, the chord is much narrower.
        var drawing = new Drawing("circ", MakeCircle(10));
        var plate = new Plate(100, 100);
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new Vector(0, 0);
        plate.Parts.Add(part);

        var cache = CutOff.BuildPerimeterCache(plate);

        // Cut at X=2: inside the BB but near the edge of the circle.
        var cutoff = new CutOff(new Vector(2, 0), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, ZeroClearance, cache);

        // The circle chord at X=2 from center (10,0) is much shorter than 20.
        // With geometry, we get a tighter exclusion, so the segments should
        // cover more of the plate than with BB.
        var segments = cutoff.Drawing.Program.Codes.OfType<LinearMove>().ToList();
        Assert.True(segments.Count >= 1);

        // Total cut length should be greater than 80 (BB would give 100-20=80)
        var totalCutLength = 0.0;
        for (var i = 0; i < cutoff.Drawing.Program.Codes.Count - 1; i += 2)
        {
            if (cutoff.Drawing.Program.Codes[i] is RapidMove rapid &&
                cutoff.Drawing.Program.Codes[i + 1] is LinearMove linear)
            {
                totalCutLength += System.Math.Abs(rapid.EndPoint.Y - linear.EndPoint.Y);
            }
        }

        Assert.True(totalCutLength > 80, $"Geometry should give more cut length than BB. Got {totalCutLength:F2}");
    }

    [Fact]
    public void Diamond_GeometryExclusionNarrowerThanBoundingBox()
    {
        // Diamond half=10 → points at (10,0), (20,10), (10,20), (0,10).
        // BB = (0,0,20,20).
        // Vertical cut at X=5: BB excludes Y=[0,20].
        // Diamond edge at X=5: intersects at Y=5 and Y=15 → exclusion [5,15].
        var drawing = new Drawing("dia", MakeDiamond(10));
        var plate = new Plate(100, 100);
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new Vector(0, 0);
        plate.Parts.Add(part);

        var cache = CutOff.BuildPerimeterCache(plate);
        var cutoff = new CutOff(new Vector(5, 0), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, ZeroClearance, cache);

        var totalCutLength = 0.0;
        for (var i = 0; i < cutoff.Drawing.Program.Codes.Count - 1; i += 2)
        {
            if (cutoff.Drawing.Program.Codes[i] is RapidMove rapid &&
                cutoff.Drawing.Program.Codes[i + 1] is LinearMove linear)
            {
                totalCutLength += System.Math.Abs(rapid.EndPoint.Y - linear.EndPoint.Y);
            }
        }

        // BB would exclude full 20 → cut length = 80.
        // Geometry excludes only 10 → cut length = 90.
        Assert.True(totalCutLength > 85, $"Diamond geometry should give more cut than BB. Got {totalCutLength:F2}");
    }

    [Fact]
    public void Triangle_AsymmetricExclusion()
    {
        // Right triangle: (0,0)→(30,0)→(0,30)→(0,0) placed at (10,10).
        // Vertical cut at X=20 (10 into the triangle from left).
        // The hypotenuse from (40,10) to (10,40): at X=20, Y = 30.
        // So geometry exclusion should be roughly [10, 30], not [10, 40] like BB.
        var drawing = new Drawing("tri", MakeTriangle(30, 30));
        var plate = new Plate(100, 100);
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new Vector(10, 10);
        plate.Parts.Add(part);

        var cache = CutOff.BuildPerimeterCache(plate);
        var cutoff = new CutOff(new Vector(20, 0), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, ZeroClearance, cache);

        var totalCutLength = 0.0;
        for (var i = 0; i < cutoff.Drawing.Program.Codes.Count - 1; i += 2)
        {
            if (cutoff.Drawing.Program.Codes[i] is RapidMove rapid &&
                cutoff.Drawing.Program.Codes[i + 1] is LinearMove linear)
            {
                totalCutLength += System.Math.Abs(rapid.EndPoint.Y - linear.EndPoint.Y);
            }
        }

        // BB would exclude [10,40] = 30 → cut = 70.
        // Geometry excludes [10,30] = 20 → cut = 80.
        Assert.True(totalCutLength > 75, $"Triangle geometry should give more cut than BB. Got {totalCutLength:F2}");
    }

    [Fact]
    public void CutLineMissesPart_NoExclusion()
    {
        var drawing = new Drawing("sq", MakeSquare(10));
        var plate = new Plate(100, 100);
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new Vector(50, 50);
        plate.Parts.Add(part);

        // Vertical cut at X=5: well outside the part at X=[50,60].
        var cutoff = new CutOff(new Vector(5, 0), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, ZeroClearance);

        // Single full-length segment → 2 codes
        Assert.Equal(2, cutoff.Drawing.Program.Codes.Count);
    }

    [Fact]
    public void HorizontalCut_Circle_UsesGeometry()
    {
        var drawing = new Drawing("circ", MakeCircle(10));
        var plate = new Plate(100, 100);
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new Vector(0, 0);
        plate.Parts.Add(part);

        var cache = CutOff.BuildPerimeterCache(plate);

        // Horizontal cut at Y=2: near the edge of the circle.
        var cutoff = new CutOff(new Vector(0, 2), CutOffAxis.Horizontal);
        cutoff.Regenerate(plate, ZeroClearance, cache);

        var totalCutLength = 0.0;
        for (var i = 0; i < cutoff.Drawing.Program.Codes.Count - 1; i += 2)
        {
            if (cutoff.Drawing.Program.Codes[i] is RapidMove rapid &&
                cutoff.Drawing.Program.Codes[i + 1] is LinearMove linear)
            {
                totalCutLength += System.Math.Abs(rapid.EndPoint.X - linear.EndPoint.X);
            }
        }

        // BB would exclude X=[0,20] → cut = 80.
        // Circle chord at Y=2 is much shorter → cut > 80.
        Assert.True(totalCutLength > 80, $"Circle horizontal cut should use geometry. Got {totalCutLength:F2}");
    }

    [Fact]
    public void Clearance_ExpandsGeometryExclusion()
    {
        var drawing = new Drawing("sq", MakeSquare(20));
        var plate = new Plate(100, 100);
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new Vector(10, 10);
        plate.Parts.Add(part);

        var settings = new CutOffSettings { PartClearance = 5.0 };
        var cache = CutOff.BuildPerimeterCache(plate);
        var cutoff = new CutOff(new Vector(20, 0), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, settings, cache);

        // Square at Y=[10,30]. With 5 clearance → exclusion [5,35].
        // Segments: [0,5] and [35,100] → 4 codes.
        Assert.Equal(4, cutoff.Drawing.Program.Codes.Count);
    }

    [Fact]
    public void BuildPerimeterCache_ReturnsOneEntryPerPart()
    {
        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(new Drawing("a", MakeSquare(10))));
        plate.Parts.Add(new Part(new Drawing("b", MakeCircle(5))));
        plate.Parts.Add(new Part(new Drawing("c", MakeDiamond(8))));

        var cache = CutOff.BuildPerimeterCache(plate);
        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void BuildPerimeterCache_SkipsCutOffParts()
    {
        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(new Drawing("real", MakeSquare(10))));
        plate.Parts.Add(new Part(new Drawing("cutoff", new Program()) { IsCutOff = true }));

        var cache = CutOff.BuildPerimeterCache(plate);
        Assert.Single(cache);
    }

    [Fact]
    public void BuildPerimeterCache_NullForOpenContour()
    {
        // Open contour: line that doesn't close
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));

        var plate = new Plate(100, 100);
        plate.Parts.Add(new Part(new Drawing("open", pgm)));

        var cache = CutOff.BuildPerimeterCache(plate);
        Assert.Single(cache);
        Assert.Null(cache[0]);
    }

    [Fact]
    public void NullCache_FallsBackToBoundingBox()
    {
        // Without a cache, should still work (using BB fallback).
        var drawing = new Drawing("sq", MakeSquare(20));
        var plate = new Plate(100, 100);
        var part = Part.CreateAtOrigin(drawing);
        part.Location = new Vector(10, 10);
        plate.Parts.Add(part);

        var cutoff = new CutOff(new Vector(20, 0), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, ZeroClearance, null);

        Assert.True(cutoff.Drawing.Program.Codes.Count > 0);
    }

    [Fact]
    public void MultipleParts_IndependentExclusions()
    {
        var plate = new Plate(100, 100);

        var sq1 = new Drawing("sq1", MakeSquare(10));
        var p1 = Part.CreateAtOrigin(sq1);
        p1.Location = new Vector(10, 10);
        plate.Parts.Add(p1);

        var sq2 = new Drawing("sq2", MakeSquare(10));
        var p2 = Part.CreateAtOrigin(sq2);
        p2.Location = new Vector(10, 50);
        plate.Parts.Add(p2);

        // Vertical cut at X=15 crosses both parts.
        var cutoff = new CutOff(new Vector(15, 0), CutOffAxis.Vertical);
        cutoff.Regenerate(plate, ZeroClearance);

        // 3 segments: before p1, between p1 and p2, after p2 → 6 codes
        Assert.Equal(6, cutoff.Drawing.Program.Codes.Count);
    }

    [Fact]
    public void ShapeProfile_SelectsLargestShapeAsPerimeter()
    {
        // Outer square: (5,0)→(25,0)→(25,20)→(5,20)→(5,0)
        // Inner cutout: (0,5)→(10,5)→(10,15)→(0,15)→(0,5)
        // The cutout has Left=0, perimeter has Left=5.
        // Old heuristic would pick the cutout as perimeter.
        var outer = new Shape();
        outer.Entities.Add(new Line(new Vector(5, 0), new Vector(25, 0)));
        outer.Entities.Add(new Line(new Vector(25, 0), new Vector(25, 20)));
        outer.Entities.Add(new Line(new Vector(25, 20), new Vector(5, 20)));
        outer.Entities.Add(new Line(new Vector(5, 20), new Vector(5, 0)));

        var inner = new Shape();
        inner.Entities.Add(new Line(new Vector(0, 5), new Vector(10, 5)));
        inner.Entities.Add(new Line(new Vector(10, 5), new Vector(10, 15)));
        inner.Entities.Add(new Line(new Vector(10, 15), new Vector(0, 15)));
        inner.Entities.Add(new Line(new Vector(0, 15), new Vector(0, 5)));

        // Combine all entities (simulating what ShapeBuilder.GetShapes would produce)
        var entities = new List<Entity>();
        entities.AddRange(inner.Entities);  // inner first — worst case for old heuristic
        entities.AddRange(outer.Entities);

        var profile = new ShapeProfile(entities);

        // Perimeter should be the outer (larger) shape
        var bb = profile.Perimeter.BoundingBox;
        Assert.Equal(20.0, bb.Width, 1);
        Assert.Equal(20.0, bb.Length, 1);
    }
}
