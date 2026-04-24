using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Threading;

namespace OpenNest.Tests.Engine;

public class NestInvarianceTests
{
    private static OpenNest.CNC.Program MakeLShapedProgram()
    {
        // L-shape: 100x50 outer rect with a 50x30 notch removed from top-right.
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(100, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(100, 20)));
        pgm.Codes.Add(new LinearMove(new Vector(50, 20)));
        pgm.Codes.Add(new LinearMove(new Vector(50, 50)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 50)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return pgm;
    }

    private static Drawing MakeImportedAt(double rotation)
    {
        var pgm = MakeLShapedProgram();
        if (!Tolerance.IsEqualTo(rotation, 0))
            pgm.Rotate(rotation, pgm.BoundingBox().Center);
        return new Drawing("L", pgm);
    }

    private static Plate MakePlate() => new Plate(new Size(500, 500))
    {
        Quadrant = 1,
        PartSpacing = 2,
    };

    private static int RunFillCount(Drawing drawing, Plate plate)
    {
        BestFitCache.Clear();
        var engine = new DefaultNestEngine(plate);
        var item = new NestItem { Drawing = drawing };
        var parts = engine.Fill(item, plate.WorkArea(), progress: null, token: CancellationToken.None);
        return parts?.Count ?? 0;
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.8)]
    [InlineData(1.2)]
    public void Fill_SameCount_AcrossImportOrientations(double theta)
    {
        var baseline = RunFillCount(MakeImportedAt(0.0), MakePlate());
        var rotated = RunFillCount(MakeImportedAt(theta), MakePlate());

        // Allow +/-1 tolerance for sweep quantization edge effects near plate boundaries.
        Assert.InRange(rotated, baseline - 1, baseline + 1);
    }

    [Fact]
    public void Fill_PlacedPartsStayWithinWorkArea_AcrossImportOrientations()
    {
        var plate = MakePlate();
        var workArea = plate.WorkArea();

        foreach (var theta in new[] { 0.0, 0.3, 0.8, 1.2 })
        {
            BestFitCache.Clear();
            var engine = new DefaultNestEngine(plate);
            var item = new NestItem { Drawing = MakeImportedAt(theta) };
            var parts = engine.Fill(item, workArea, progress: null, token: CancellationToken.None);

            Assert.NotNull(parts);
            foreach (var p in parts)
            {
                Assert.InRange(p.BoundingBox.Left, workArea.Left - 0.5, workArea.Right + 0.5);
                Assert.InRange(p.BoundingBox.Bottom, workArea.Bottom - 0.5, workArea.Top + 0.5);
            }
        }
    }
}
