using System;
using System.Linq;
using System.Threading;

namespace OpenNest;

/// <summary>Contract-stage runner: empty jobs only. Nonempty allocation is not implemented yet.</summary>
public sealed class NestJobRunner : INestingEngine
{
    private readonly Func<string, IPlateNester> plateNesterFactory;

    /// <summary>Stores a runner-local strategy factory; never consults the global engine registry.</summary>
    public NestJobRunner(Func<string, IPlateNester> plateNesterFactory)
    {
        ArgumentNullException.ThrowIfNull(plateNesterFactory);
        this.plateNesterFactory = plateNesterFactory;
    }

    public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();
        if (job.Parts.Count != 0)
            throw new NotSupportedException("Whole-job allocation is not implemented yet; only empty jobs are supported.");
        return new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            Array.Empty<NestJobPlateResult>(), Array.Empty<PartFulfillment>(),
            job.Plates.Select(stock => new StockUsage(stock.Id, 0, stock.Quantity)));
    }
}
