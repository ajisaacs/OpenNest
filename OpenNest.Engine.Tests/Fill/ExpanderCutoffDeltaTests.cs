using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

public class ExpanderCutoffDeltaTests
{
    private static Plate Sheet() => new(new Size(30, 30)) { EdgeSpacing = new Spacing(0, 0) };

    private static Program Rectangle(double width = 2, double height = 2)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(width, 0);
        program.LineTo(width, height);
        program.LineTo(0, height);
        program.LineTo(0, 0);
        return program;
    }

    private static Part Add(Plate plate, Program program, double x, double y)
    {
        var part = new Part(new Drawing("neutral", program), new Vector(x, y));
        plate.Parts.Add(part);
        return part;
    }

    private static Program Disk(double radius) =>
        ConvertGeometry.ToProgram(new List<Entity> { new Circle(0, 0, radius) });

    [Theory]
    [InlineData(CutOffAxis.Horizontal)]
    [InlineData(CutOffAxis.Vertical)]
    public void Expand_RegeneratedCutoff_MustPreserveNativeEntryGap(CutOffAxis axis)
    {
        var plate = Sheet();
        var anchor = Add(plate, Rectangle(), 1, 1);
        var mover = Add(plate, Rectangle(), 4, 4);
        plate.CutOffs.Add(new CutOff(new Vector(10, 10), axis));
        var settings = new CutOffSettings();
        plate.RegenerateCutOffs(settings);
        var before = axis == CutOffAxis.Horizontal
            ? 10 - mover.BoundingBox.Top : 10 - mover.BoundingBox.Right;

        var result = Expander.Expand(new List<Part> { anchor, mover }, plate,
            new Expander.Options { MaxSpacing = 3 });
        var after = axis == CutOffAxis.Horizontal
            ? 10 - mover.BoundingBox.Top : 10 - mover.BoundingBox.Right;
        Assert.True(after >= before - 1.1e-6,
            $"Cutoff entry gap shrank {before:R}->{after:R}, mover=({mover.Location.X:R},{mover.Location.Y:R}), achieved={result.AchievedSpacing:R}, violations={result.Violations.Count}");
        plate.RegenerateCutOffs(settings);
        Assert.Single(plate.CutOffs);
    }

    [Fact]
    public void Expand_RemoteOpenU_MustNotMoveTowardNativeCutoff()
    {
        var plate = Sheet();
        var anchor = Add(plate, Rectangle(), 1, 4);
        var mover = Add(plate, Rectangle(), 4, 4);
        var program = new Program();
        program.MoveTo(0, 10);
        program.LineTo(30, 10);
        program.LineTo(30, 0);
        program.LineTo(0, 0);
        plate.Parts.Add(new Part(new Drawing("neutral-cutoff", program) { IsCutOff = true }, Vector.Zero));

        var result = Expander.Expand(new List<Part> { anchor, mover }, plate,
            new Expander.Options { MaxSpacing = 3 });
        // The three real segments are at least four units from the selected
        // square at entry; an invented closing chord is not a real obstacle.
        var gap = System.Math.Min(mover.BoundingBox.Bottom,
            System.Math.Min(10 - mover.BoundingBox.Top, 30 - mover.BoundingBox.Right));
        Assert.True(gap >= 4 - 1.1e-6,
            $"Open path gap shrank to {gap:R}; mover=({mover.Location.X:R},{mover.Location.Y:R}); spacing={result.AchievedSpacing:R}, violations={result.Violations.Count}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Separate_ShallowDiskOrRingVsRectangle_MustNotClaimClear(bool annulus)
    {
        var plate = Sheet();
        var entities = new List<Entity> { new Circle(0, 0, 2) };
        if (annulus)
            entities.Add(new Circle(0, 0, 1));
        Add(plate, ConvertGeometry.ToProgram(entities), 10, 10);
        const double angle = 0.017;
        const double depth = 0.0005;
        var mover = Add(plate, Rectangle(0.01, 0.01),
            10 + (2 - depth) * System.Math.Cos(angle),
            10 + (2 - depth) * System.Math.Sin(angle));

        var result = Expander.Separate(new List<Part> { mover }, plate, 0);
        var nearest = result.Positions[0].DistanceTo(new Vector(10, 10));
        Assert.True(nearest >= 2 - 1e-8 || result.Violations.Any(v => v.Achieved < 0),
            $"Native material overlap remains: nearest={nearest:R}, converged={result.Converged}, violations={result.Violations.Count}");
    }

    [Theory]
    [InlineData(CutOffAxis.Horizontal)]
    [InlineData(CutOffAxis.Vertical)]
    public void Separate_RegeneratedCutoff_MustNotCrossEvenAtFarClearEndpoint(CutOffAxis axis)
    {
        var plate = Sheet();
        var horizontal = axis == CutOffAxis.Horizontal;
        var anchor = Add(plate, Rectangle(), 1, 1);
        var mover = Add(plate, Rectangle(), horizontal ? 1 : 6, horizontal ? 6 : 1);
        plate.CutOffs.Add(new CutOff(new Vector(10, 10), axis));
        plate.RegenerateCutOffs(new CutOffSettings());

        var result = Expander.Separate(new List<Part> { anchor, mover }, plate, 12);
        var trailingEdge = (horizontal ? result.Positions[1].Y : result.Positions[1].X) + 2;
        Assert.True(trailingEdge <= 10 + 1e-6,
            $"Crossed native cutoff, trailing edge={trailingEdge:R}");
        Assert.False(result.Converged);
    }
}
