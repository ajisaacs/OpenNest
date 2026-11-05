using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Strategies;

public class CircleFillStrategyTests
{
    private const double Radius = 4;
    private const double Gap = 0.25;

    private static Drawing MakeCircleDrawing()
    {
        var pgm = new Program();
        var start = new Vector(Radius, 0);
        pgm.Codes.Add(new RapidMove(start));
        pgm.Codes.Add(new ArcMove(start, new Vector(0, 0), RotationType.CCW));
        return new Drawing("circle", pgm);
    }

    private static Plate MakePlate()
    {
        var plate = new Plate(60, 120) { PartSpacing = Gap };
        plate.EdgeSpacing = new Spacing(Gap, Gap, Gap, Gap);
        return plate;
    }

    [Fact]
    public void Fill_EightInchCirclesOnASixtyByOneTwenty_BeatsTheStripeLayout()
    {
        var plate = MakePlate();
        var filler = new PlateFiller(plate);
        var item = new NestItem { Drawing = MakeCircleDrawing() };

        var parts = filler.Fill(item, plate.WorkArea(), null, CancellationToken.None);

        Assert.Equal(112, parts.Count);
        Assert.Equal(NestPhase.Custom, filler.WinnerPhase);
        AssertSpacedInside(parts, plate.WorkArea());
    }

    [Fact]
    public void Fill_NineInchCirclesOnASixtyByOneTwenty_EndsWithStraightRows()
    {
        // Seven staggered rows of 13 and 12 place 88; five staggered rows and two straight rows
        // of 13 over the fifth place 89.
        var pgm = new Program();
        var start = new Vector(4.5, 0);
        pgm.Codes.Add(new RapidMove(start));
        pgm.Codes.Add(new ArcMove(start, new Vector(0, 0), RotationType.CCW));
        var plate = new Plate(60, 120) { PartSpacing = 0.2 };
        plate.EdgeSpacing = new Spacing(0.2, 0.2, 0.2, 0.2);
        var filler = new PlateFiller(plate);

        var parts = filler.Fill(
            new NestItem { Drawing = new Drawing("nine", pgm) },
            plate.WorkArea(),
            null,
            CancellationToken.None
        );

        Assert.Equal(89, parts.Count);
        Assert.Equal(NestPhase.Custom, filler.WinnerPhase);
        var rows = parts.GroupBy(p => System.Math.Round(p.BoundingBox.Center.Y, 6)).OrderBy(g => g.Key);
        Assert.Equal(new[] { 13, 12, 13, 12, 13, 13, 13 }, rows.Select(r => r.Count()));
        var work = plate.WorkArea();
        Assert.All(parts, p => Assert.True(work.Contains(p.BoundingBox.Offset(-Tolerance.Epsilon))));
        var centers = parts.Select(p => p.BoundingBox.Center).ToList();
        for (var i = 0; i < centers.Count; i++)
        {
            for (var j = i + 1; j < centers.Count; j++)
                Assert.True(centers[i].DistanceTo(centers[j]) >= 9.2 - Tolerance.Epsilon);
        }
    }

    [Fact]
    public void Fill_RoundPartOffItsOrigin_PlacesItAtTheFilledCenter()
    {
        var pgm = new Program();
        var start = new Vector(25, 10);
        pgm.Codes.Add(new RapidMove(start));
        pgm.Codes.Add(new ArcMove(start, new Vector(21, 10), RotationType.CCW));
        var plate = MakePlate();
        var filler = new PlateFiller(plate);

        var parts = filler.Fill(
            new NestItem { Drawing = new Drawing("offset", pgm) },
            plate.WorkArea(),
            null,
            CancellationToken.None
        );

        Assert.Equal(112, parts.Count);
        var work = plate.WorkArea();
        Assert.All(parts, p => Assert.True(work.Contains(p.BoundingBox.Offset(-Tolerance.Epsilon))));
    }

    [Fact]
    public void Strategy_SkipsPartsThatAreNotRound()
    {
        var plate = MakePlate();
        var context = new FillContext
        {
            Item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) },
            WorkArea = plate.WorkArea(),
            Plate = plate,
            PartType = PartType.Rectangle,
        };

        Assert.Null(new CircleFillStrategy().Fill(context));
    }

    private static void AssertSpacedInside(System.Collections.Generic.List<Part> parts, Box work)
    {
        var centers = parts.Select(p => p.BoundingBox.Center).ToList();
        for (var i = 0; i < centers.Count; i++)
        {
            Assert.True(centers[i].X - Radius >= work.Left - Tolerance.Epsilon);
            Assert.True(centers[i].X + Radius <= work.Right + Tolerance.Epsilon);
            Assert.True(centers[i].Y - Radius >= work.Bottom - Tolerance.Epsilon);
            Assert.True(centers[i].Y + Radius <= work.Top + Tolerance.Epsilon);

            for (var j = i + 1; j < centers.Count; j++)
                Assert.True(centers[i].DistanceTo(centers[j]) >= 2 * Radius + Gap - Tolerance.Epsilon);
        }
    }
}
