using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.IO.Bending;

namespace OpenNest.IO.Tests;

public class BendRepairTests
{
    private static BendRepairOptions Options(
        BendRepairUnits units = BendRepairUnits.Inches,
        double limit = 2
    ) => new() { DrawingUnits = units, MaxEndpointMovementMillimeters = limit };

    private static (List<Entity> entities, List<Bend> bends) Fixture(
        double start = 0.05,
        double end = 9.95
    )
    {
        var entities = new List<Entity>
        {
            new Line(new Vector(0, 0), new Vector(10, 0)),
            new Line(new Vector(10, 0), new Vector(10, 10)),
            new Line(new Vector(10, 10), new Vector(0, 10)),
            new Line(new Vector(0, 10), new Vector(0, 0)),
            Mark(start, 5, start + 0.5, 5),
            Mark(end - 0.5, 5, end, 5),
            Mark(4, 2, 4, 3),
        };
        return (
            entities,
            new List<Bend>
            {
                new()
                {
                    StartPoint = new Vector(start, 5),
                    EndPoint = new Vector(end, 5),
                    Direction = BendDirection.Up,
                },
            }
        );
    }

    private static Line Mark(double x, double y, double x2, double y2) =>
        new(new Vector(x, y), new Vector(x2, y2))
        {
            Layer = new Layer("SCRIBE") { IsVisible = true },
        };

    [Theory]
    [InlineData(0.05, 9.95)]
    [InlineData(-0.05, 10.05)]
    [InlineData(0, 9.95)]
    public void RepairsAlongAxisPreservingCutAndUnrelatedMarksAndIsIdempotent(
        double start,
        double end
    )
    {
        var (entities, bends) = Fixture(start, end);
        var originals = entities.ToArray();
        var cutPoints = entities
            .Take(4)
            .Cast<Line>()
            .Select(l => (l.StartPoint, l.EndPoint))
            .ToArray();
        var report = Assert.Single(BendRepair.Apply(entities, bends, Options()));
        Assert.Equal("Repaired", report.Status);
        Assert.Equal(new Vector(0, 5), bends[0].StartPoint);
        Assert.Equal(new Vector(10, 5), bends[0].EndPoint);
        for (var i = 0; i < 4; i++)
            Assert.Same(originals[i], entities[i]);
        Assert.Equal(
            cutPoints,
            entities.Take(4).Cast<Line>().Select(l => (l.StartPoint, l.EndPoint)).ToArray()
        );
        Assert.Same(originals[6], entities[6]);
        Assert.Equal(new Vector(0, 5), ((Line)entities[4]).StartPoint);
        Assert.Equal(new Vector(10, 5), ((Line)entities[5]).EndPoint);
        Assert.Equal(0.5, entities[4].Length, 8);
        var after = entities.ToArray();
        Assert.Equal(
            "Unchanged",
            Assert.Single(BendRepair.Apply(entities, bends, Options())).Status
        );
        Assert.Equal(after, entities);
    }

    [Fact]
    public void OneInchTicksAreAcceptedAtPhysicalLengthCap()
    {
        var (entities, bends) = Fixture();
        entities[4] = Mark(0.05, 5, 1.05, 5);
        entities[5] = Mark(8.95, 5, 9.95, 5);
        Assert.Equal(
            "Repaired",
            Assert.Single(BendRepair.Apply(entities, bends, Options())).Status
        );
        Assert.Equal(
            "Unchanged",
            Assert.Single(BendRepair.Apply(entities, bends, Options())).Status
        );
    }

    [Fact]
    public void MillimeterCoordinatesUseSamePhysicalLimit()
    {
        var (entities, bends) = Fixture();
        foreach (var entity in entities)
            entity.Scale(25.4);
        bends[0].StartPoint *= 25.4;
        bends[0].EndPoint *= 25.4;
        Assert.Equal(
            "Repaired",
            Assert
                .Single(BendRepair.Apply(entities, bends, Options(BendRepairUnits.Millimeters)))
                .Status
        );
        Assert.Equal(254, bends[0].EndPoint.X, 8);
    }

    [Fact]
    public void RotatedAxisIsNotRotatedByRepair()
    {
        var (entities, bends) = Fixture();
        foreach (var entity in entities)
            entity.Rotate(0.7);
        var axis = bends[0].ToLine();
        axis.Rotate(0.7);
        bends[0].StartPoint = axis.StartPoint;
        bends[0].EndPoint = axis.EndPoint;
        Assert.Equal(
            "Repaired",
            Assert.Single(BendRepair.Apply(entities, bends, Options())).Status
        );
        Assert.Equal(0.7, bends[0].LineAngle, 8);
        Assert.Equal(10, bends[0].Length, 8);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("perpendicular")]
    [InlineData("offset")]
    [InlineData("excessive")]
    [InlineData("open")]
    [InlineData("hole")]
    [InlineData("shared")]
    [InlineData("unknown-layer")]
    [InlineData("cut-tick")]
    [InlineData("nonfinite")]
    public void SafetyFailuresAreAtomic(string failure)
    {
        var (entities, bends) = Fixture();
        switch (failure)
        {
            case "missing":
                entities.RemoveAt(5);
                break;
            case "duplicate":
                entities.Add(entities[4].Clone());
                break;
            case "perpendicular":
                entities[5] = Mark(9.95, 5, 9.95, 5.5);
                break;
            case "offset":
                entities[5].Offset(0, 0.01);
                break;
            case "excessive":
                bends[0].EndPoint = new Vector(9, 5);
                entities[5] = Mark(8.5, 5, 9, 5);
                break;
            case "open":
                entities.RemoveAt(0);
                break;
            case "hole":
                entities.Add(new Circle(new Vector(5, 5), 1));
                break;
            case "shared":
                bends.Add(
                    new Bend { StartPoint = bends[0].StartPoint, EndPoint = bends[0].EndPoint }
                );
                break;
            case "unknown-layer":
                foreach (var e in entities.Take(4))
                    e.Layer = new Layer("UNKNOWN");
                break;
            case "cut-tick":
                entities[5].Layer = Layer.Default;
                break;
            case "nonfinite":
                bends[0].StartPoint = new Vector(double.NaN, 5);
                break;
        }
        var before = entities.ToArray();
        var start = bends[0].StartPoint;
        var end = bends[0].EndPoint;
        var reports = BendRepair.Apply(entities, bends, Options());
        Assert.All(reports, r => Assert.Equal("Skipped", r.Status));
        Assert.Equal(before, entities);
        Assert.Equal(start.X, bends[0].StartPoint.X);
        Assert.Equal(start.Y, bends[0].StartPoint.Y);
        Assert.Equal(end, bends[0].EndPoint);
    }

    [Theory]
    [InlineData(BendRepairUnits.Unspecified, 2)]
    [InlineData(BendRepairUnits.Inches, 0)]
    [InlineData(BendRepairUnits.Inches, -1)]
    [InlineData(BendRepairUnits.Inches, 3.176)]
    [InlineData(BendRepairUnits.Inches, double.NaN)]
    [InlineData(BendRepairUnits.Inches, double.PositiveInfinity)]
    public void InvalidConfigurationDoesNotMutate(BendRepairUnits units, double limit)
    {
        var (entities, bends) = Fixture();
        var before = entities.ToArray();
        Assert.Equal(
            "Skipped",
            Assert.Single(BendRepair.Apply(entities, bends, Options(units, limit))).Status
        );
        Assert.Equal(before, entities);
        Assert.Equal(0.05, bends[0].StartPoint.X);
    }

    [Fact]
    public void DefaultsOff()
    {
        Assert.Null(CadImportOptions.Default.BendRepair);
        var (entities, bends) = Fixture();
        var before = entities.ToArray();
        Assert.Empty(BendRepair.Apply(entities, bends, null));
        Assert.Equal(before, entities);
        Assert.Equal(0.05, bends[0].StartPoint.X);
    }
}
