using System;
using System.Threading;

namespace OpenNest;

/// <summary>Synchronous whole-job solver. Cancellation throws, rather than returning partial success.</summary>
public interface INestingEngine
{
    NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    );
}
