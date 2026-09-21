using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Tests.Jobs;

public class JobAdapterTests
{
    [Fact]
    public void LegacyMutationsCannotDoubleSubtractOrReachCallerObjects()
    {
        var drawing = new Drawing("same name", TestDrawingFactory.Rectangle());
        drawing.Quantity.Required = 9;
        var item = new NestItem
        {
            Drawing = drawing,
            Quantity = 3,
            Priority = 7,
            StepAngle = 0,
        };
        var sourcePlate = new Plate(100, 200) { Quantity = 3, PartSpacing = 2 };
        var job = new NestJob(
            new[] { DrawingJobMapper.FromItem("requirement", item) },
            new[] { DrawingJobMapper.FromPlate("stock", sourcePlate, 3) }
        );
        var quantities = new List<int>();
        var adapter = new LegacyPlateNesterAdapter(p => new MutatingEngine(
            p,
            items =>
            {
                var privateItem = Assert.Single(items);
                quantities.Add(privateItem.Quantity);
                Assert.NotSame(drawing, privateItem.Drawing);
                Assert.Equal(0, privateItem.StepAngle);
                Assert.Equal(7, privateItem.Priority);
                var part = new Part(privateItem.Drawing);
                privateItem.Quantity = 0;
                privateItem.Drawing.Quantity.Required = 0;
                return new List<Part> { part };
            }
        ));
        var result = new NestJobRunner(_ => adapter).Solve(job);
        var materialized = NestResultMaterializer.Materialize(job, result);
        Assert.Equal(new[] { 3, 2, 1 }, quantities);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(3, materialized.Nest.Plates.Count);
        Assert.All(
            materialized.Nest.Plates,
            p =>
            {
                Assert.Equal(1, p.Quantity);
                Assert.Single(p.Parts);
            }
        );
        var outputDrawing = materialized.DrawingsByPartId["requirement"];
        Assert.Equal(3, outputDrawing.Quantity.Required);
        Assert.Equal(3, outputDrawing.Quantity.Nested);
        Assert.All(
            materialized.Nest.Plates,
            p => Assert.Same(outputDrawing, p.Parts[0].BaseDrawing)
        );
        Assert.NotSame(drawing, outputDrawing);
        Assert.Equal(9, drawing.Quantity.Required);
        Assert.Equal(0, drawing.Quantity.Nested);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(3, sourcePlate.Quantity);
        Assert.Empty(sourcePlate.Parts);
        Assert.Equal(2, sourcePlate.PartSpacing);
        Assert.Equal(
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle()).Motions,
            PartGeometrySnapshot.FromProgram(drawing.Program).Motions
        );
    }

    [Fact]
    public void ReferenceIdentityNotNamesControlsLegacyPlacements()
    {
        var drawing = new Drawing("duplicate", TestDrawingFactory.Rectangle());
        var job = new NestJob(
            new[]
            {
                DrawingJobMapper.FromDrawing("a", drawing, 1),
                DrawingJobMapper.FromDrawing("b", drawing, 1),
            },
            FiniteStockJobTests.Job(1).Plates
        );
        var adapter = new LegacyPlateNesterAdapter(p => new MutatingEngine(
            p,
            items =>
            {
                Assert.NotSame(items[0].Drawing, items[1].Drawing);
                foreach (var item in items)
                    item.Drawing.Name = "identical";
                return new List<Part>
                {
                    new Part(items[0].Drawing, new Vector(0, 0)),
                    new Part(items[1].Drawing, new Vector(10, 0)),
                };
            }
        ));
        var result = new NestJobRunner(_ => adapter).Solve(job);
        Assert.Equal(new[] { "a", "b" }, result.Plates[0].Placements.Select(p => p.PartId));
        var output = NestResultMaterializer.Materialize(job, result);
        Assert.NotSame(output.DrawingsByPartId["a"], output.DrawingsByPartId["b"]);
        Assert.All(output.DrawingsByPartId.Values, d => Assert.Equal(1, d.Quantity.Nested));
    }

    [Fact]
    public void UnknownPrivateDrawingIsRejectedEvenWithMatchingName()
    {
        var adapter = new LegacyPlateNesterAdapter(p => new MutatingEngine(
            p,
            items => new List<Part>
            {
                new(new Drawing(items[0].Drawing.Name, TestDrawingFactory.Rectangle())),
            }
        ));
        Assert.Throws<InvalidOperationException>(() =>
            new NestJobRunner(_ => adapter).Solve(FiniteStockJobTests.Job())
        );
        Assert.Throws<NotSupportedException>(() =>
            LegacyPlateNesterAdapter.Create("not registered")
        );
    }

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
        var result = new NestJobRunner(LegacyPlateNesterAdapter.Create).Solve(job);
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

    private sealed class MutatingEngine(Plate plate, Func<List<NestItem>, List<Part>> nest)
        : NestEngineBase(plate)
    {
        public override string Name => "test";
        public override string Description => "mutates private demand";

        public override List<Part> Nest(
            List<NestItem> items,
            IProgress<NestProgress> progress,
            CancellationToken token
        ) => nest(items);
    }
}
