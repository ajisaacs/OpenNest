using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Tests.Jobs;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Fill;

/// <summary>
/// Expand Spacing on a selection that repeats on a lattice: the lattice is
/// stretched per axis so the pattern survives.
/// </summary>
public class ExpanderPatternTests
{
    private const double Radius = 2;

    private static Program Disk(double radius)
    {
        var p = new Program();
        p.MoveTo(radius, 0);
        p.ArcTo(-radius, 0, 0, 0, RotationType.CCW);
        p.ArcTo(radius, 0, 0, 0, RotationType.CCW);
        return p;
    }

    private static Plate MakePlate(double lengthX, double widthY, double edge)
    {
        var plate = new Plate(new Size(widthY, lengthX));
        plate.EdgeSpacing = new Spacing(edge, edge);
        return plate;
    }

    private static Part Add(Plate plate, Drawing drawing, double x, double y)
    {
        var part = new Part(drawing, new Vector(x, y));
        plate.Parts.Add(part);
        return part;
    }

    /// <summary>
    /// Disks staggered like a sheet fill at a 0.2 gap: same-row neighbors and
    /// diagonal neighbors both touch at the gap, the rows span the plate's
    /// length exactly, and the plate has spare height.
    /// </summary>
    private static (Plate Plate, List<Part> Parts) StaggeredDisks()
    {
        const double gap = 0.2;
        const double edge = 0.2;
        const int columns = 9;
        var pitch = 2 * Radius + gap;
        var dx = pitch / 2;
        var dy = System.Math.Sqrt(pitch * pitch - dx * dx);
        var plate = MakePlate(2 * edge + 2 * Radius + (columns - 1) * dx, 30, edge);
        var drawing = new Drawing("disk", Disk(Radius));
        var parts = new List<Part>();
        for (var column = 0; column < columns; column++)
        {
            var odd = column % 2 == 1;
            for (var row = 0; row < (odd ? 2 : 3); row++)
                parts.Add(Add(plate, drawing,
                    edge + Radius + column * dx,
                    edge + Radius + (odd ? dy : 0) + row * 2 * dy));
        }
        return (plate, parts);
    }

    private static double DiskGap(Part a, Part b) => a.Location.DistanceTo(b.Location) - 2 * Radius;

    [Fact]
    public void Expand_StaggeredFillTightAlongX_StretchesOnlyTheRowPitch()
    {
        var (plate, parts) = StaggeredDisks();
        var xs = parts.Select(p => p.Location.X).ToList();
        var rowMateGap = DiskGap(parts[0], parts[5]); // columns 0 and 2, first row

        var result = Expander.Expand(parts, plate);

        Assert.True(result.KeptPattern);
        Assert.True(result.PatternMoved);
        Assert.Empty(result.Violations);
        for (var i = 0; i < parts.Count; i++)
            Assert.Equal(xs[i], parts[i].Location.X, 6);

        // Every column stays evenly pitched and the stagger stays at half a pitch.
        var even = parts.Where(p => System.Math.Abs(p.Location.X - xs[0]) < 1e-6)
            .Select(p => p.Location.Y).OrderBy(y => y).ToList();
        var odd = parts.Where(p => System.Math.Abs(p.Location.X - xs[3]) < 1e-6)
            .Select(p => p.Location.Y).OrderBy(y => y).ToList();
        var rowPitch = even[1] - even[0];
        Assert.Equal(rowPitch, even[2] - even[1], 6);
        Assert.Equal(rowPitch, odd[1] - odd[0], 6);
        Assert.Equal(even[0] + rowPitch / 2, odd[0], 6);

        // The pattern fills the height and only the diagonal gap opened up.
        var work = plate.WorkArea();
        Assert.True(parts.Min(p => p.BoundingBox.Bottom) >= work.Bottom - 1e-9);
        Assert.True(parts.Max(p => p.BoundingBox.Top) <= work.Top + 1e-9);
        Assert.True(work.Top - parts.Max(p => p.BoundingBox.Top) < 0.02);
        Assert.Equal(rowMateGap, DiskGap(parts[0], parts[5]), 6);
        Assert.True(DiskGap(parts[0], parts[3]) > 1, $"diagonal gap {DiskGap(parts[0], parts[3])}");
        Assert.Equal(rowMateGap, result.AchievedSpacing, 2);
    }

    [Fact]
    public void Expand_GridWithRoomBothWays_FillsTheWorkAreaEvenly()
    {
        var plate = MakePlate(40, 30, 0.5);
        var drawing = new Drawing("square", TestDrawingFactory.Rectangle(4, 4));
        var parts = new List<Part>();
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
                parts.Add(Add(plate, drawing, 1 + column * 5, 1 + row * 5));

        var result = Expander.Expand(parts, plate);

        Assert.True(result.KeptPattern);
        Assert.True(result.PatternMoved);
        var xs = parts.Select(p => p.Location.X).Distinct(new Near()).OrderBy(x => x).ToList();
        var ys = parts.Select(p => p.Location.Y).Distinct(new Near()).OrderBy(y => y).ToList();
        Assert.Equal(3, xs.Count);
        Assert.Equal(3, ys.Count);
        Assert.Equal(xs[1] - xs[0], xs[2] - xs[1], 6);
        Assert.Equal(ys[1] - ys[0], ys[2] - ys[1], 6);
        Assert.True(xs[1] - xs[0] > 5.5 && ys[1] - ys[0] > 5.5);

        var work = plate.WorkArea();
        Assert.True(parts.Min(p => p.BoundingBox.Left) >= work.Left - 1e-9);
        Assert.True(parts.Max(p => p.BoundingBox.Right) <= work.Right + 1e-9);
        Assert.True(work.Right - parts.Max(p => p.BoundingBox.Right) + parts.Min(p => p.BoundingBox.Left) - work.Left < 0.02);
        Assert.True(work.Top - parts.Max(p => p.BoundingBox.Top) + parts.Min(p => p.BoundingBox.Bottom) - work.Bottom < 0.02);
    }

    [Fact]
    public void Expand_TwoPartMotif_KeepsEachMotifRigid()
    {
        var plate = MakePlate(60, 40, 0.5);
        var tall = new Drawing("tall", TestDrawingFactory.Rectangle(2, 4));
        var small = new Drawing("small", TestDrawingFactory.Rectangle(1, 1));
        var tallParts = new List<Part>();
        var smallParts = new List<Part>();
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < 3; column++)
            {
                tallParts.Add(Add(plate, tall, 1 + column * 4, 1 + row * 5));
                smallParts.Add(Add(plate, small, 1 + column * 4 + 2.25, 1 + row * 5));
            }
        var offset = smallParts[0].BoundingBox.Center - tallParts[0].BoundingBox.Center;
        var selected = tallParts.Concat(smallParts).ToList();

        var result = Expander.Expand(selected, plate);

        Assert.True(result.KeptPattern);
        Assert.True(result.PatternMoved);
        for (var i = 0; i < tallParts.Count; i++)
        {
            var moved = smallParts[i].BoundingBox.Center - tallParts[i].BoundingBox.Center;
            Assert.Equal(offset.X, moved.X, 6);
            Assert.Equal(offset.Y, moved.Y, 6);
        }
        Assert.True(tallParts[1].Location.X - tallParts[0].Location.X > 4.5);
    }

    [Fact]
    public void Expand_PatternBesideFixedPart_StopsAtItsFloor()
    {
        var plate = MakePlate(40, 12, 0.5);
        var drawing = new Drawing("square", TestDrawingFactory.Rectangle(2, 2));
        var parts = new List<Part>();
        for (var column = 0; column < 4; column++)
            parts.Add(Add(plate, drawing, 1 + column * 3, 1));
        var wall = Add(plate, new Drawing("wall", TestDrawingFactory.Rectangle(2, 2)), 20, 1);

        var result = Expander.Expand(parts, plate);

        Assert.True(result.KeptPattern);
        Assert.True(result.PatternMoved);
        Assert.Equal(new Vector(20, 1), wall.Location);
        // The row's own gap is 1, so the gap to the fixed part closes no further.
        Assert.True(wall.BoundingBox.Left - parts.Max(p => p.BoundingBox.Right) >= 1 - 1e-3);
        Assert.True(parts[1].Location.X - parts[0].Location.X > 3.5);
        for (var i = 1; i < parts.Count; i++)
            Assert.Equal(parts[1].Location.X - parts[0].Location.X, parts[i].Location.X - parts[i - 1].Location.X, 6);
    }

    [Fact]
    public void Expand_IrregularSelection_UsesThePairSearch()
    {
        var plate = MakePlate(40, 30, 0.5);
        var drawing = new Drawing("square", TestDrawingFactory.Rectangle(2, 2));
        var parts = new List<Part>
        {
            Add(plate, drawing, 5, 5),
            Add(plate, drawing, 8.3, 5.4),
            Add(plate, drawing, 5.7, 9.1),
            Add(plate, drawing, 11.6, 12.2),
        };

        var result = Expander.Expand(parts, plate);

        Assert.False(result.KeptPattern);
        Assert.Equal(new Vector(5, 5), parts[0].Location); // the pair search's anchor
    }

    [Fact]
    public void Detect_NeedsMorePartsThanTheLatticeFits()
    {
        var drawing = new Drawing("square", TestDrawingFactory.Rectangle(1, 1));
        Part At(double x, double y) => new(drawing, new Vector(x, y));

        Assert.Null(Expander.RepeatingPattern.Detect(new[] { At(0, 0), At(3, 1), At(1, 4) }));

        var row = Expander.RepeatingPattern.Detect(new[] { At(0, 0), At(3, 0), At(6, 0) });
        Assert.NotNull(row);
        Assert.Equal(Vector.Zero, row.B);

        Assert.Null(Expander.RepeatingPattern.Detect(new[] { At(0, 0), At(3, 0), At(7, 0) }));
    }

    private sealed class Near : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => System.Math.Abs(a - b) < 1e-6;
        public int GetHashCode(double value) => 0;
    }
}
