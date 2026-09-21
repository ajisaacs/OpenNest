using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// The jobs-boundary contract that <see cref="CandidatePlacementContext"/> and
/// <see cref="NestJobRunner"/> share with every <see cref="IPlateNester"/>. Retargeted from the
/// deleted legacy adapter's tests: the private-geometry, reference-identity, and unknown-part
/// guarantees are boundary properties, so they are proven with a nester stub that mutates its
/// private items exactly as an engine would.
/// </summary>
public class NesterContractTests
{
    [Fact]
    public void NesterMutationsCannotDoubleSubtractOrReachCallerObjects()
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
        var nester = new RecordingNester(
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
        );
        var result = new NestJobRunner(_ => nester).Solve(job);
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
    public void ReferenceIdentityNotNamesControlsPlacements()
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
        var nester = new RecordingNester(
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
        );
        var result = new NestJobRunner(_ => nester).Solve(job);
        Assert.Equal(new[] { "a", "b" }, result.Plates[0].Placements.Select(p => p.PartId));
        var output = NestResultMaterializer.Materialize(job, result);
        Assert.NotSame(output.DrawingsByPartId["a"], output.DrawingsByPartId["b"]);
        Assert.All(output.DrawingsByPartId.Values, d => Assert.Equal(1, d.Quantity.Nested));
    }

    [Fact]
    public void UnknownPrivateDrawingIsRejectedEvenWithMatchingName()
    {
        var nester = new RecordingNester(
            items =>
                new List<Part>
                {
                    new(new Drawing(items[0].Drawing.Name, TestDrawingFactory.Rectangle())),
                }
        );
        Assert.Throws<InvalidOperationException>(() =>
            new NestJobRunner(_ => nester).Solve(FiniteStockJobTests.Job())
        );
        Assert.Throws<NotSupportedException>(() => PlateNesterFactory.Create("not registered"));
    }

    /// <summary>Runs a caller-supplied function over freshly built private items and maps the
    /// returned parts back by Drawing reference — the same boundary mechanics as the production
    /// <see cref="CandidatePlacementContext"/>, but rebuilt per call so each trial is independent.</summary>
    private sealed class RecordingNester(Func<List<NestItem>, List<Part>> propose) : IPlateNester
    {
        public PlateCandidate Place(
            PlatePlacementRequest request,
            IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default
        )
        {
            var items = new List<NestItem>();
            var idsByDrawing = new Dictionary<Drawing, string>(ReferenceEqualityComparer.Instance);
            foreach (var requirement in request.Parts)
            {
                var drawing = DrawingJobMapper.CreateDrawing(requirement);
                idsByDrawing.Add(drawing, requirement.Id);
                items.Add(
                    new NestItem
                    {
                        Drawing = drawing,
                        Quantity = requirement.Quantity,
                        Priority = requirement.Priority,
                        StepAngle = DrawingJobMapper.LegacyStep(requirement.Rotation),
                        RotationStart = requirement.Rotation.Start,
                        RotationEnd = requirement.Rotation.End,
                    }
                );
            }
            return new PlateCandidate(
                propose(items).Select(p => new NestJobPlacement(
                    IdFor(idsByDrawing, p),
                    0,
                    p.Location.X,
                    p.Location.Y,
                    p.Rotation
                ))
            );
        }

        private static string IdFor(Dictionary<Drawing, string> idsByDrawing, Part part)
        {
            // Deliberately reference-based, like the placement context.
            if (part?.BaseDrawing == null || !idsByDrawing.TryGetValue(part.BaseDrawing, out var id))
                throw new InvalidOperationException(
                    "Placement does not reference a known requirement drawing."
                );
            return id;
        }
    }
}
