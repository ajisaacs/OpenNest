using System;
using System.Diagnostics;
using System.IO;
using OpenNest.Engine.Jobs;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// Writes one solve's NestJobProgress as log lines prefixed with "[job/engine]". Every
    /// PlateCommitted is written; EvaluatingCandidate is throttled to one line per interval so a
    /// chatty engine cannot flood the console. One instance per solve; Report is thread-safe.
    /// </summary>
    public sealed class JobProgressLog : IProgress<NestJobProgress>
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

        private readonly TextWriter writer;
        private readonly string label;
        private readonly TimeSpan interval;
        private readonly Func<TimeSpan> clock;
        private readonly object sync = new();
        private TimeSpan? lastEvaluating;

        public JobProgressLog(
            TextWriter writer,
            string label,
            TimeSpan? interval = null,
            Func<TimeSpan> clock = null
        )
        {
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this.label = label;
            this.interval = interval ?? DefaultInterval;
            if (clock == null)
            {
                var stopwatch = Stopwatch.StartNew();
                clock = () => stopwatch.Elapsed;
            }
            this.clock = clock;
        }

        public void Started() => Write("started");

        public void Finished(NestJobResult result, long elapsedMs) =>
            Write(
                $"finished in {elapsedMs} ms: {result.Status} ({result.StopReason}), "
                    + $"{result.Plates.Count} plate(s)"
            );

        public void Failed(string error, long elapsedMs) =>
            Write($"failed after {elapsedMs} ms: {error}");

        public void Report(NestJobProgress value)
        {
            if (value == null)
                return;

            if (value.Stage == NestJobStage.PlateCommitted)
            {
                Write(
                    $"committed plate {value.CommittedPlates} on stock {value.StockId} "
                        + $"({value.CommittedParts} parts placed)"
                );
                return;
            }

            lock (sync)
            {
                var now = clock();
                if (lastEvaluating.HasValue && now - lastEvaluating.Value < interval)
                    return;
                lastEvaluating = now;
            }

            var plate = value.PlateIndex >= 0 ? value.PlateIndex + 1 : value.CommittedPlates + 1;
            Write(
                $"evaluating plate {plate} on stock {value.StockId} "
                    + $"({value.CommittedPlates} plate(s), {value.CommittedParts} parts committed)"
            );
        }

        private void Write(string message)
        {
            lock (sync)
                writer.WriteLine($"[{label}] {message}");
        }
    }
}
