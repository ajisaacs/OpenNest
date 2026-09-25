using OpenNest.Benchmark;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Benchmark;

public class NestLayoutCheckEquivalenceTests
{
    [Theory]
    [InlineData("StockLadder")]
    [InlineData("Default")]
    [InlineData("Strip")]
    [InlineData("Vertical Remnant")]
    [InlineData("Horizontal Remnant")]
    public void ExistingBenchmarkDxfFixtureMatchesFrozenValidator(string engine)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "validator-fixtures");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "bracket.manifest.json");
        var dxf = Path.GetFullPath(Path.Combine("Bending", "TestData", "4526 A14 PT11.dxf"));
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            sheetSizes = new[] { "48x96" },
            parts = new[] { new { dxf, quantity = 2 } },
        }));
        var job = Assert.Single(JobLoader.Load(path)).BuildNestJob(10);
        AssertEquivalent(job, NestingEngineRegistry.Create(engine).Solve(job));
        // The fixture can be too large for its offered sheet. Also force real geometry
        // through the arbiter so equivalence cannot pass solely on empty solver results.
        var id = job.Parts[0].Id;
        var sheet = new NestJobPlateResult(0, job.Plates[0], new[]
        {
            new NestJobPlacement(id, 0, 0, 0, 0),
            new NestJobPlacement(id, 1, 1, 1, 0.3),
        });
        var forced = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { sheet }, Array.Empty<PartFulfillment>(), Array.Empty<StockUsage>());
        Assert.NotEmpty(NestLayoutCheck.Violations(job, forced));
        AssertEquivalent(job, forced);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(9, 0, 0)]
    [InlineData(2, 0.25, 0)]
    [InlineData(2, 0, 0.3)]
    public void BenchmarkSyntheticRectangleCasesMatchIncludingViolationOrder(double x, double spacing, double angle)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(2, 0);
        program.LineTo(2, 2);
        program.LineTo(0, 2);
        program.LineTo(0, 0);
        var part = new NestJobPart("rectangle", PartGeometrySnapshot.FromProgram(program), 1,
            rotation: RotationPolicy.Fixed(0));
        var stock = new NestPlateStock("sheet", new Size(10, 10), quantity: 1, partSpacing: spacing);
        var job = new NestJob(new[] { part }, new[] { stock });
        var sheets = new[]
        {
            new NestJobPlateResult(0, stock, new[] { new NestJobPlacement(part.Id, 0, 0, 0, 0),
                new NestJobPlacement(part.Id, 1, x, 0, angle) }),
            new NestJobPlateResult(1, stock, new[] { new NestJobPlacement(part.Id, 2, 0, 0, 0) }),
        };
        AssertEquivalent(job, new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            sheets, Array.Empty<PartFulfillment>(), Array.Empty<StockUsage>()));
    }

    private static void AssertEquivalent(NestJob job, NestJobResult result)
    {
        var materialized = NestResultMaterializer.Materialize(job, result);
        var requirements = job.Parts.ToDictionary(p => materialized.DrawingsByPartId[p.Id], p => (p.Id, p.Quantity));
        var runs = materialized.Nest.Plates.Select(p => (p, p.Parts.ToList())).ToList();
        var names = job.Parts.ToDictionary(p => p.Id, p => p.Id);
        var legacy = LegacyNestValidator.Validate(runs, requirements);
        LegacyNestValidator.ValidateAgainstJob(job, result, names, legacy);
        var wrapper = NestValidator.Validate(runs, requirements);
        NestValidator.ValidateAgainstJob(job, result, names, wrapper);
        Assert.Equal(legacy.Violations, wrapper.Violations);
        Assert.Equal(legacy.Violations, NestLayoutCheck.Violations(job, result));
    }
}
