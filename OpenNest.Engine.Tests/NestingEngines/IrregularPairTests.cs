using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Irregular;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class IrregularPairTests
{
    // A 20-long wedge, 8 wide at one end and 6 at the other. Lying down, two copies need
    // 8 + 0.25 + 8 = 16.25 across, but nested slant-to-slant they need only about 14.25.
    // The 30 x 15 sheet holds the pair only when the two copies interlock.
    private static OpenNest.CNC.Program Wedge() => Polyline((0, 0), (8, 0), (6, 20), (0, 20));

    private static OpenNest.CNC.Program MirroredWedge() => Polyline((0, 0), (8, 0), (8, 20), (2, 20));

    public static TheoryData<string> Wedges => new() { "wedge", "mirrored" };

    private static OpenNest.CNC.Program NativeU()
    {
        // Same native semicircular U as the shared curve-contact regression; no DXF dependency.
        var p = new OpenNest.CNC.Program();
        p.MoveTo(2.5, 0.625);
        p.LineTo(2.5, 0);
        p.LineTo(1.5, 0);
        p.Codes.Add(new OpenNest.CNC.ArcMove(1.5, 3, 1.5, 1.5, OpenNest.RotationType.CW));
        p.LineTo(2.5, 3);
        p.LineTo(2.5, 2.375);
        p.LineTo(1.5, 2.375);
        p.Codes.Add(new OpenNest.CNC.ArcMove(1.5, 0.625, 1.5, 1.5, OpenNest.RotationType.CCW));
        p.LineTo(2.5, 0.625);
        return p;
    }

    [Fact]
    public void NativeUQuantityTwoOffersAndPlacesAnInterlockedClearPair()
    {
        var job = Job(new[] { Part("native-u", NativeU(), 2, RotationPolicy.Automatic) },
            new[] { Stock("sheet", 6, 6, spacing: 0.25) });
        var types = PartCatalog.Build(job);
        var pairs = PairCatalog.Build(types, 0.25, 6, 6, CancellationToken.None);
        Assert.NotEmpty(pairs[0]);
        var pair = pairs[0][0];
        // Bounding boxes overlap on both axes: this is an interlock, not adjacent boxes.
        Assert.True(System.Math.Min(pair.A.MaxX, pair.Dx + pair.B.MaxX)
            > System.Math.Max(pair.A.MinX, pair.Dx + pair.B.MinX));
        Assert.True(System.Math.Min(pair.A.MaxY, pair.Dy + pair.B.MaxY)
            > System.Math.Max(pair.A.MinY, pair.Dy + pair.B.MinY));
        var tightJob = Job(job.Parts.ToArray(),
            new[] { Stock("tight", pair.Height + 0.01, pair.Width + 0.01, spacing: 0.25) });
        var result = new IrregularNestingEngine().Solve(tightJob);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, Assert.Single(result.Plates).Placements.Count);
        LayoutAssert.Valid(tightJob, result);
        var geometry = JobPartGeometry.Read(job.Parts[0].Geometry);
        Assert.Empty(geometry.Cutouts);
        Assert.Equal(2.5, geometry.Bounds.Length, 9);
        Assert.Equal(3, geometry.Bounds.Width, 9);
        var outlines = result.Plates[0].Placements.Select(placement =>
        {
            var shape = (OpenNest.Geometry.Shape)geometry.Perimeter.Clone();
            shape.Rotate(placement.Rotation);
            shape.Offset(placement.X, placement.Y);
            return shape.ToPolygonWithTolerance(1e-6);
        }).ToArray();
        // Raw fine boundaries, independent of BestFit's offsets and the NFP free regions.
        Assert.True(OpenNest.Geometry.Clearance.Between(outlines[0], outlines[1]).Distance >= 0.25 - 2e-6);
        Assert.True(outlines[0].BoundingBox.Right > outlines[1].BoundingBox.Left
            && outlines[1].BoundingBox.Right > outlines[0].BoundingBox.Left);
        Assert.True(outlines[0].BoundingBox.Top > outlines[1].BoundingBox.Bottom
            && outlines[1].BoundingBox.Top > outlines[0].BoundingBox.Bottom);
    }

    [Theory]
    [InlineData(0.0)] // Coincident material.
    [InlineData(3.1)] // Disjoint outlines, but only 0.1 clearance rather than 0.25.
    public void PairResolutionRejectsOverlapAndInsufficientSpacing(double offset)
    {
        var program = NativeU();
        var job = Job(new[] { Part("native-u", program, 2, RotationPolicy.Automatic) },
            new[] { Stock("sheet", 10, 10, spacing: 0.25) });
        var type = Assert.Single(PartCatalog.Build(job));
        var geometry = JobPartGeometry.Read(job.Parts[0].Geometry);
        var drawing = new Drawing("native-u", program);
        var members = new List<OpenNest.Part>
        {
            new(drawing),
            new(drawing) { Location = new OpenNest.Geometry.Vector(0, offset) },
        };
        var extra = new List<Orientation>();
        Assert.Null(PairCatalog.Resolve(type, extra, geometry, members, 0, 0.25));
        Assert.Empty(extra);
    }

    [Fact]
    public void PairOnlyOrientationRemainsEligibleWhenSampledSinglesDoNotFit()
    {
        var wedge = MirroredWedge();
        wedge.Rotate(System.Math.PI / 4);
        // A many-type job samples only two single orientations. The pair adds its own
        // legal rotations; neither sampled single fits this narrow stock.
        var parts = Enumerable.Range(0, 24).Select(i => Rectangle($"oversized-{i}", 100, 100, 1)).ToList();
        parts.Insert(0, Part("wedge", wedge, 2, RotationPolicy.Automatic));
        var stock = Stock("sheet", 15, 30, spacing: 0.25);
        var job = Job(parts.ToArray(), new[] { stock });
        var types = PartCatalog.Build(job);
        Assert.DoesNotContain(types[0].Orientations, o => stock.Fits(o.Width, o.Height));
        var pairs = PairCatalog.Build(types, 0.25, 30, 15, CancellationToken.None);
        Assert.Contains(pairs[0], p => stock.Fits(p.Width, p.Height));

        var result = new IrregularNestingEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(2, Assert.Single(result.Plates).Placements.Count);
    }

    [Theory]
    [MemberData(nameof(Wedges))]
    public void QuantityTwoInterlocksWhenOnlyAPairFits(string shape)
    {
        var program = shape == "wedge" ? Wedge() : MirroredWedge();
        var job = Job(
            new[] { Part("wedge", program, 2, RotationPolicy.Fixed(System.Math.PI / 2, allow180Equivalent: true)) },
            new[] { Stock("sheet", 15, 30, spacing: 0.25) }
        );

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, Assert.Single(result.Plates).Placements.Count);
    }

    [Theory]
    [MemberData(nameof(Wedges))]
    public void AutomaticRotationAlsoFindsThePair(string shape)
    {
        var program = shape == "wedge" ? Wedge() : MirroredWedge();
        var job = Job(
            new[] { Part("wedge", program, 2, RotationPolicy.Automatic) },
            new[] { Stock("sheet", 15, 30, spacing: 0.25) }
        );

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Single(result.Plates);
    }

    [Fact]
    public void PairsNeverUseARotationThePolicyForbids()
    {
        // Fixed at 0 with no 180-degree twin: every best-fit pair turns its second copy,
        // so no pair is allowed and the parts must be placed as singles.
        var job = Job(
            new[] { Part("wedge", Wedge(), 2, RotationPolicy.Fixed(0)) },
            new[] { Stock("sheet", 25, 30, spacing: 0.25) }
        );

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.All(result.Plates.SelectMany(p => p.Placements), p => Assert.Equal(0, p.Rotation, 9));
    }

    [Fact]
    public void OddDemandPlacesTheLastCopyAsASingleWithoutOverdrawing()
    {
        var job = Job(new[] { Part("wedge", MirroredWedge(), 3, RotationPolicy.Automatic) },
            new[] { Stock("sheet", 15, 30, spacing: 0.25) });
        var result = new IrregularNestingEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(new[] { 1, 2 }, result.Plates.Select(p => p.Placements.Count).OrderBy(n => n));
    }

    [Fact]
    public void PairMembersLeaveTheirEnclosedGapAvailableForLaterParts()
    {
        var stock = Stock("sheet", 6.1, 6.1, spacing: 0.1);
        var job = Job(new[] { Rectangle("bar", 2, 6, 2), Rectangle("insert", 1, 1, 1) }, new[] { stock });
        var types = PartCatalog.Build(job);
        var bar = Assert.Single(types[0].Orientations);
        var pair = new PairPose { A = bar, B = bar, Dx = 4, Dy = 0 };
        var pairs = new Dictionary<int, IReadOnlyList<PairPose>> { [0] = new[] { pair } };
        // Along Y the pair buys twice the area for the same advance, so it wins first.
        var fill = new FrontierPacker(types, new NoFitCache(0.1), pairs, stock,
            PackAxis.Y, 1, new WorkCounter()).Fill(new[] { 2, 1 }, CancellationToken.None);
        Assert.Equal(3, fill.Parts.Count);
        Assert.Equal(0, fill.Parts[0].Orientation.TypeIndex);
        Assert.Equal(0, fill.Parts[1].Orientation.TypeIndex);
        var insert = fill.Parts[2];
        Assert.Equal(1, insert.Orientation.TypeIndex);
        Assert.True(insert.Left > fill.Parts[0].Right);
        Assert.True(insert.Right < fill.Parts[1].Left);
        var geometries = job.Parts.Select(p => JobPartGeometry.Read(p.Geometry)).ToArray();
        for (var i = 0; i < fill.Parts.Count; i++)
            for (var j = i + 1; j < fill.Parts.Count; j++)
            {
                var a = fill.Parts[i];
                var b = fill.Parts[j];
                Assert.True(NestLayoutCheck.Clears(geometries[a.Orientation.TypeIndex],
                    new NestJobPlacement(job.Parts[a.Orientation.TypeIndex].Id, i, a.X, a.Y, a.Orientation.Rotation),
                    geometries[b.Orientation.TypeIndex],
                    new NestJobPlacement(job.Parts[b.Orientation.TypeIndex].Id, j, b.X, b.Y, b.Orientation.Rotation), 0.1));
            }
    }

    [Fact]
    public void PairedJobsAreDeterministic()
    {
        // Best-fit evaluates in parallel; equal-area pairs must still be chosen the same way.
        NestJob Build() =>
            Job(
                new[]
                {
                    Part("wedge", Wedge(), 6, RotationPolicy.Automatic),
                    Part("ell", LShape(9, 7, 3), 4, RotationPolicy.Automatic),
                },
                new[] { Stock("sheet", 30, 40, spacing: 0.25) }
            );

        var first = new IrregularNestingEngine().Solve(Build());
        var second = new IrregularNestingEngine().Solve(Build());

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(second));
    }
}
