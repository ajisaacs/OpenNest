using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Domain-boundary adapters: geometry snapshots, mapper round-trips, and materialization. The
/// former adapter-vs-runner contract tests moved to <see cref="NesterContractTests"/> when the
/// legacy plate-nester adapter was deleted in the jobs-only placement migration.
/// </summary>
public class JobAdapterTests
{
    [Fact]
    public void ExactGeometryRoundTripsIncludingOriginArcHoleAndMode()
    {
        var program = TestDrawingFactory.Rectangle();
        program.Offset(-17.123456789, 5.25);
        program.MoveTo(-14, 9);
        program.ArcTo(-14, 9, -13, 9, RotationType.CW);
        ((ArcMove)program.Codes[^1]).Layer = LayerType.Cut;
        var drawing = new Drawing("shape", program);
        var part = DrawingJobMapper.FromDrawing("p", drawing, 1);
        var snapshot = part.Geometry.Motions.ToArray();
        ((Motion)program.Codes[1]).EndPoint = new Vector(999, 888);
        Assert.Equal(snapshot, part.Geometry.Motions);
        var roundTrip = DrawingJobMapper.ToProgram(part.Geometry);
        Assert.Equal(snapshot, PartGeometrySnapshot.FromProgram(roundTrip).Motions);
        Assert.Equal(program.Mode, roundTrip.Mode);
        roundTrip.Codes.Clear();
        Assert.Equal(snapshot, part.Geometry.Motions);
        var incremental = new Program(Mode.Incremental);
        incremental.MoveTo(5, -3);
        incremental.LineTo(7, 4);
        var incrementalSnapshot = PartGeometrySnapshot.FromProgram(incremental);
        Assert.Equal(Mode.Incremental, DrawingJobMapper.ToProgram(incrementalSnapshot).Mode);
        Assert.Equal(
            incrementalSnapshot.Motions,
            PartGeometrySnapshot
                .FromProgram(DrawingJobMapper.ToProgram(incrementalSnapshot))
                .Motions
        );
    }

    [Fact]
    public void RealDefaultEngineRunsFromDrawingThroughMaterialization()
    {
        var drawing = new Drawing(
            "generated asymmetric rectangle",
            TestDrawingFactory.Rectangle(13, 7)
        );
        drawing.Quantity.Required = 1;
        var job = new NestJob(
            new[] { DrawingJobMapper.FromDrawing("rectangle", drawing, 1) },
            new[] { new NestPlateStock("sheet", new Size(40, 60), 1, 1, new Spacing(2, 2, 2, 2)) }
        );
        var result = new NestJobRunner(PlateNesterFactory.Create).Solve(job);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(new StockUsage("sheet", 1, 0), Assert.Single(result.StockUsage));
        var pose = Assert.Single(Assert.Single(result.Plates).Placements);
        var output = NestResultMaterializer.Materialize(job, result);
        var physicalPlate = Assert.Single(output.Nest.Plates);
        var placed = Assert.Single(physicalPlate.Parts);
        Assert.Equal(1, physicalPlate.Quantity);
        Assert.Same(output.DrawingsByPartId["rectangle"], placed.BaseDrawing);
        Assert.Equal(pose.X, placed.Location.X);
        Assert.Equal(pose.Y, placed.Location.Y);
        Assert.Equal(pose.Rotation, placed.Rotation, 10);
        Assert.Equal(1, placed.BaseDrawing.Quantity.Nested);
        Assert.Equal(0, drawing.Quantity.Nested);
        Assert.Equal(1, drawing.Quantity.Required);
        var bounds = placed.BoundingBox;
        var work = physicalPlate.WorkArea();
        Assert.True(bounds.Left >= work.Left - 1e-6 && bounds.Bottom >= work.Bottom - 1e-6);
        Assert.True(bounds.Right <= work.Right + 1e-6 && bounds.Top <= work.Top + 1e-6);
    }

    [Fact]
    public void MaterializationRotatesAboutSnapshotOriginThenTranslates()
    {
        var program = TestDrawingFactory.Rectangle();
        program.Offset(-5, 3);
        var job = new NestJob(
            new[] { new NestJobPart("p", PartGeometrySnapshot.FromProgram(program), 1) },
            FiniteStockJobTests.Job(1).Plates
        );
        var result = new NestJobRunner(_ => new FiniteStockJobTests.Nester(_ => new PlateCandidate(
            new[] { new NestJobPlacement("p", 0, 23, 31, 0.7) }
        ))).Solve(job);
        var output = NestResultMaterializer.Materialize(job, result);
        var part = output.Nest.Plates[0].Parts[0];
        var expected = new Vector(-5, 3).Rotate(0.7);
        Assert.Equal(expected.X, ((Motion)part.Program.Codes[0]).EndPoint.X, 10);
        Assert.Equal(expected.Y, ((Motion)part.Program.Codes[0]).EndPoint.Y, 10);
        Assert.Equal(new Vector(23, 31), part.Location);
    }
}
