using OpenNest.Benchmark;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Tests.Benchmark;

public class NestLayoutCheckMarksTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EtchTickPastFlushMaterialEdgePassesBoundsAndLeavesSalvageUnchanged(int quadrant)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 3);
        program.LineTo(0, 3);
        program.LineTo(0, 0);
        var clean = new NestJobPart("part", PartGeometrySnapshot.FromProgram(program), 1);
        program.MoveTo(3.75, 2);
        program.Codes.Add(new LinearMove(4.25, 2) { Layer = LayerType.Scribe });
        var marked = new NestJobPart("part", PartGeometrySnapshot.FromProgram(program), 1);
        var stock = new NestPlateStock("sheet", new Size(10, 20), quadrant: quadrant);
        var job = new NestJob(new[] { marked }, new[] { stock },
            new NestJobOptions(salvageRate: 0.5, minimumSalvageDimension: 1));
        var builder = new NestJobResultBuilder(job);
        builder.AddSheet(stock, new[] { (marked.Id, stock.WorkArea.Right - 4, stock.WorkArea.Bottom, 0.0) });
        var result = builder.Build(NestJobStopReason.Completed);
        Assert.Empty(NestLayoutCheck.Violations(job, result));
        var cleanJob = new NestJob(new[] { clean }, job.Plates, job.Options);
        Assert.Equal(NestJobCost.Evaluate(cleanJob, result), NestJobCost.Evaluate(job, result));

        var materialized = NestResultMaterializer.Materialize(job, result);
        var requirements = new Dictionary<Drawing, (string, int)>
        {
            [materialized.DrawingsByPartId[marked.Id]] = (marked.Id, 1),
        };
        var runs = materialized.Nest.Plates.Select(p => (p, p.Parts.ToList())).ToList();
        Assert.Contains(LegacyNestValidator.Validate(runs, requirements).Violations,
            message => message.Contains("outside the work area"));
        Assert.True(NestValidator.Validate(runs, requirements).Valid);
    }
}
