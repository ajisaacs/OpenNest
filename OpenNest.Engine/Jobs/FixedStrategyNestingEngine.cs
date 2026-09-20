using System;
using System.Threading;

namespace OpenNest;

/// <summary>
/// Adapts one fixed IPlateNester strategy to the whole-job INestingEngine contract, so it can compete
/// as a full job solver alongside model-submitted engines. Delegates all multi-plate/size selection to
/// NestJobRunner; only the placement strategy key is forced, overriding whatever the job itself declared.
/// </summary>
public sealed class FixedStrategyNestingEngine : INestingEngine
{
    private readonly string strategy;
    private readonly NestJobRunner runner = new(PlateNesterFactory.Create);

    public FixedStrategyNestingEngine(string strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
            throw new ArgumentException("Strategy cannot be null or whitespace.", nameof(strategy));
        this.strategy = strategy;
    }

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        var forced = new NestJob(
            job.Parts,
            job.Plates,
            new NestJobOptions(strategy, job.Options.MaxPlates)
        );
        return runner.Solve(forced, progress, token);
    }
}
