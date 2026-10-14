using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

public class ExpanderFinalQualityTests
{
    private static Plate Sheet() => new(new Size(12, 30)) { EdgeSpacing = new Spacing(0, 0) };

    private static Program Disk(double radius) =>
        ConvertGeometry.ToProgram(new List<Entity> { new Circle(0, 0, radius) });

    private static Program Rectangle(double width, double height)
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

    [Theory]
    [InlineData(0.73, 0.001)]
    [InlineData(0.017, 0.001)]
    [InlineData(0.73, 0.0001)]
    public void Expand_NativeDisks_MustReportOrRemoveRemainingOverlap(double angle, double depth)
    {
        var plate = Sheet();
        var anchor = Add(plate, Disk(2), 7, 6);
        var mover = Add(plate, Disk(2),
            7 + (4 - depth) * System.Math.Cos(angle),
            6 + (4 - depth) * System.Math.Sin(angle));

        var result = Expander.Expand(new List<Part> { anchor, mover }, plate,
            new Expander.Options { MaxSpacing = 3 });
        var nativeGap = anchor.Location.DistanceTo(mover.Location) - 4;
        Assert.True(nativeGap >= -1e-8 || result.Violations.Any(v => v.Achieved < 0),
            $"Native disks still overlap by {-nativeGap:R} but spacing={result.AchievedSpacing:R}, violations={result.Violations.Count}; mover=({mover.Location.X:R},{mover.Location.Y:R})");
    }

    [Fact]
    public void Separate_CurvedExactContact_CanStillGrowARealGap()
    {
        var plate = Sheet();
        Add(plate, Disk(2), 10, 6);
        var mover = Add(plate, Rectangle(1, 1), 12, 5.5);
        var result = Expander.Separate(new List<Part> { mover }, plate, spacing: 1);
        Assert.True(result.Converged, $"curved contact pinned: {result.Violations.Count} violations");
        Assert.True(result.Positions[0].X >= 13 - 1e-3);
        Assert.Equal(12, mover.Location.X);
    }

    [Fact]
    public void Separate_SmallConcaveOverlap_MustNotClaimConvergedAtZero()
    {
        var plate = Sheet();
        var program = new Program();
        const double k = 0.0001;
        program.MoveTo(0, 0);
        program.LineTo(6 * k, 0);
        program.LineTo(6 * k, 6 * k);
        program.LineTo(4 * k, 6 * k);
        program.LineTo(4 * k, 2 * k);
        program.LineTo(2 * k, 2 * k);
        program.LineTo(2 * k, 6 * k);
        program.LineTo(0, 6 * k);
        program.LineTo(0, 0);
        Add(plate, program, 10, 5);
        var mover = Add(plate, Rectangle(2 * k, 2 * k), 10 + k, 5 + 3 * k);
        // Left finger [10,10.0002] × [5.0002,5.0006] intersects the
        // mover [10.0001,10.0003] × [5.0003,5.0005] in positive area.
        var result = Expander.Separate(new List<Part> { mover }, plate, 0);
        Assert.False(result.Converged,
            $"Known material overlap; pose={result.Positions[0]}, violations={result.Violations.Count}");
        Assert.Contains(result.Violations, v => v.Achieved < 0);
    }

    [Fact]
    public void Expand_RemoteRegeneratedCutoff_MustNotPinUnrelatedGrowth()
    {
        var plate = Sheet();
        var anchor = Add(plate, Rectangle(2, 2), 1, 4);
        var mover = Add(plate, Rectangle(2, 2), 4, 4);
        plate.CutOffs.Add(new CutOff(new Vector(0, 10), CutOffAxis.Horizontal));
        var settings = new CutOffSettings();
        plate.RegenerateCutOffs(settings);
        var cutPart = Assert.Single(plate.Parts.Where(p => p.BaseDrawing.IsCutOff));

        var result = Expander.Expand(new List<Part> { anchor, mover }, plate,
            new Expander.Options { MaxSpacing = 3 });
        Assert.True(mover.Location.X >= 6 - 1e-4 && result.AchievedSpacing >= 2.99,
            $"Remote cutoff pinned horizontal move: x={mover.Location.X:R}, achieved={result.AchievedSpacing:R}, violations={result.Violations.Count}");
        Assert.Equal(0, cutPart.Location.X);
        Assert.Equal(0, cutPart.Location.Y);
        plate.RegenerateCutOffs(settings);
        Assert.Single(plate.CutOffs);
        Assert.Single(plate.Parts.Where(p => p.BaseDrawing.IsCutOff));
    }

    [Fact]
    public void Expand_RegeneratedCutoffBlocksCrossingEvenWithClearFarEndpoint()
    {
        var plate = new Plate(new Size(20, 30)) { EdgeSpacing = new Spacing(0, 0) };
        var anchor = Add(plate, Rectangle(2, 2), 1, 1);
        var mover = Add(plate, Rectangle(2, 2), 1, 6);
        plate.CutOffs.Add(new CutOff(new Vector(0, 10), CutOffAxis.Horizontal));
        var settings = new CutOffSettings();
        plate.RegenerateCutOffs(settings);
        Assert.Single(plate.Parts.Where(p => p.BaseDrawing.IsCutOff));

        var result = Expander.Separate(new List<Part> { anchor, mover }, plate, spacing: 12);
        Assert.False(result.Converged);
        Assert.True(result.Positions[1].Y + 2 <= 10 + 1e-6,
            $"Mover crossed regenerated cut path: y={result.Positions[1].Y:R}");
        Assert.Equal(6, mover.Location.Y);
        plate.RegenerateCutOffs(settings);
        Assert.Single(plate.CutOffs);
    }

    [Fact]
    public void Expand_OutsideUnchangedMover_MustNotReturnUnqualifiedSuccess()
    {
        var plate = Sheet();
        var anchor = Add(plate, Rectangle(2, 2), 1, 4);
        var mover = Add(plate, Rectangle(2, 2), 35, 4);
        var result = Expander.Expand(new List<Part> { anchor, mover }, plate,
            new Expander.Options { MaxSpacing = 3 });

        Assert.True(mover.BoundingBox.Right > 30); // The invalid entry stays unchanged.
        Assert.Equal(35, mover.Location.X);
        Assert.Equal(0, result.AchievedSpacing);
        var edge = Assert.Single(result.Violations.Where(v => ReferenceEquals(v.A, mover)));
        Assert.Null(edge.B);
        Assert.True(edge.BlockedByEdge);
    }
}
