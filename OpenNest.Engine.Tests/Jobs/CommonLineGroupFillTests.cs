using System.Threading;
using OpenNest.Clamps;
using OpenNest.CNC;
using OpenNest.CommonLines;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.CommonLines;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Filling with a copied common-line group (docs/common-line-cutting.md): the group is placed as
/// one outline and every placed copy is joined again.
/// </summary>
public class CommonLineGroupFillTests
{
    private const double Kerf = 0.01;

    private static Drawing Outline(string name, params (double X, double Y)[] points)
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(points[0].X, points[0].Y));
        foreach (var point in points.Skip(1).Append(points[0]))
            program.Codes.Add(new LinearMove(point.X, point.Y));
        return new Drawing(name, program) { AllowCommonLine = true };
    }

    /// <summary>A 10 x 8 T with a 3 x 4 rectangle lying on its top edge one kerf away.</summary>
    private static List<Part> TeeAndRectangle()
    {
        var tee = new Part(Outline("T 10x8",
            (3.5, 0), (3.5, 5), (0, 5), (0, 8), (10, 8), (10, 5), (6.5, 5), (6.5, 0)));
        tee.Location = new Vector(35.205, 38.707);
        tee.UpdateBounds();
        var rectangle = new Part(Outline("Rectangle 3x4", (0, 0), (0, 4), (3, 4), (3, 0)));
        rectangle.Rotate(System.Math.PI / 2);
        rectangle.Location = new Vector(39.205, 46.717);
        rectangle.UpdateBounds();
        return [tee, rectangle];
    }

    private static Plate Sheet() => new(new Size(60, 120))
    {
        PartSpacing = 0.3,
        EdgeSpacing = new Spacing(0.3, 0.75, 0.3, 0.25),
    };

    private static void AssertWholeIntactGroups(Plate plate, PlateFillProposal fill)
    {
        Assert.NotEmpty(fill.CommonLines);
        Assert.Equal(2 * fill.CommonLines.Count, fill.Parts.Count);
        Assert.All(fill.Parts, part => Assert.Single(fill.CommonLines, group => group.Contains(part)));
        Assert.All(fill.CommonLines, group =>
        {
            Assert.Equal(Kerf, group.Kerf);
            Assert.Equal(["T 10x8", "Rectangle 3x4"], group.Members.Select(m => m.BaseDrawing.Name));
            Assert.True(CommonLineGeometry.Check(group).IsIntact);
        });

        var work = plate.WorkArea();
        Assert.All(fill.Parts, part =>
        {
            Assert.True(part.BoundingBox.Left >= work.Left - 1e-6 && part.BoundingBox.Right <= work.Right + 1e-6);
            Assert.True(part.BoundingBox.Bottom >= work.Bottom - 1e-6 && part.BoundingBox.Top <= work.Top + 1e-6);
        });
        foreach (var part in fill.Parts)
            plate.Parts.Add(part);
        foreach (var group in fill.CommonLines)
            plate.CommonLines.Add(group);
        var ids = fill.Parts.Select(p => p.BaseDrawing).Distinct()
            .ToDictionary(d => d, d => (d.Name, int.MaxValue));
        Assert.Empty(NestLayoutCheck.Validate([(plate, plate.Parts.ToList())], ids));
    }

    [Theory]
    [InlineData("Lattice")]
    [InlineData("Strip")]
    public void FillsWithWholeGroupsEachJoinedAgain(string strategy)
    {
        var plate = Sheet();
        var fill = PlateFillService.FillGroup(strategy, plate, TeeAndRectangle(), Kerf, plate.WorkArea(),
            null, null, CancellationToken.None);

        Assert.True(fill.CommonLines.Count >= 20, $"placed {fill.CommonLines.Count} groups");
        AssertWholeIntactGroups(plate, fill);
    }

    [Fact]
    public void SettlesWholeGroupsAndPreviewsMemberParts()
    {
        var plate = Sheet();
        var previews = new List<NestProgress>();
        var settled = 0;
        var fill = PlateFillService.FillGroup("Lattice", plate, TeeAndRectangle(), Kerf, plate.WorkArea(),
            units =>
            {
                settled = units.Count;
                Compactor.Settle(units, plate.WorkArea(), plate.PartSpacing);
            },
            new Immediate(previews.Add), CancellationToken.None);

        Assert.Equal(fill.CommonLines.Count, settled);
        Assert.NotEmpty(previews);
        Assert.All(previews.Where(p => p.BestParts != null), p =>
        {
            Assert.Equal(0, p.BestParts.Count % 2);
            Assert.All(p.BestParts, part => Assert.Contains(part.BaseDrawing.Name, new[] { "T 10x8", "Rectangle 3x4" }));
        });
        AssertWholeIntactGroups(plate, fill);
    }

    [Fact]
    public void KeepsWholeGroupsClearOfClamps()
    {
        var plate = Sheet();
        foreach (var x in new[] { 19.25, 39.25, 59.25, 79.25, 99.25 })
            plate.ClampZones.Add(new ClampZone($"clamp-{x}", new Box(x, 0, 1.5, 0.625)));
        var fill = PlateFillService.FillGroup("Lattice", plate, TeeAndRectangle(), Kerf, plate.WorkArea(),
            units => Compactor.Settle(units, plate, plate.WorkArea()), null, CancellationToken.None);

        AssertWholeIntactGroups(plate, fill);
    }

    [Fact]
    public void WithoutAKerfIsThePlainGroupFill()
    {
        var plate = Sheet();
        var plain = PlateFillService.FillGroup("Lattice", plate, TeeAndRectangle(), plate.WorkArea(),
            null, CancellationToken.None);
        var fill = PlateFillService.FillGroup("Lattice", plate, TeeAndRectangle(), null, plate.WorkArea(),
            null, null, CancellationToken.None);

        Assert.Empty(fill.CommonLines);
        Assert.Equal(plain.Select(p => (p.Location, p.Rotation)), fill.Parts.Select(p => (p.Location, p.Rotation)));
    }

    [Fact]
    public void PartsThatAreNotOneGroupFallBackToThePlainFill()
    {
        var plate = Sheet();
        var apart = TeeAndRectangle();
        apart[1].Offset(0, 1);
        Assert.Null(CommonLineCompound.Build(apart, Kerf));

        var fill = PlateFillService.FillGroup("Lattice", plate, apart, Kerf, plate.WorkArea(),
            null, null, CancellationToken.None);

        Assert.Empty(fill.CommonLines);
        Assert.NotEmpty(fill.Parts);
    }

    [Fact]
    public void ExpandedMembersKeepTheirPoseInATurnedCopy()
    {
        var compound = CommonLineCompound.Build(TeeAndRectangle(), Kerf);
        Assert.NotNull(compound);
        var placed = new Part(compound.Proxy);
        placed.Rotate(0.7);
        placed.Location = new Vector(20, 30);
        placed.UpdateBounds();

        var (parts, group) = compound.Expand(placed);

        Assert.True(CommonLineGeometry.Check(group).IsIntact);
        var box = ((IEnumerable<IBoundable>)parts).GetBoundingBox();
        Assert.Equal(placed.BoundingBox.Left, box.Left, 4);
        Assert.Equal(placed.BoundingBox.Bottom, box.Bottom, 4);
        Assert.Equal(placed.BoundingBox.Right, box.Right, 4);
        Assert.Equal(placed.BoundingBox.Top, box.Top, 4);
    }

    private sealed class Immediate(Action<NestProgress> sink) : IProgress<NestProgress>
    {
        public void Report(NestProgress value) => sink(value);
    }
}
