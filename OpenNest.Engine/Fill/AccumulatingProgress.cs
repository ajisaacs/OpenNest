using System;
using System.Collections.Generic;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Wraps an IProgress to prepend previously placed parts to each report,
    /// so the UI shows the full picture during incremental fills.
    /// </summary>
    internal class AccumulatingProgress : IProgress<NestProgress>
    {
        private readonly IProgress<NestProgress> inner;
        private readonly List<Part> previousParts;

        public AccumulatingProgress(IProgress<NestProgress> inner, List<Part> previousParts)
        {
            this.inner = inner;
            this.previousParts = previousParts;
        }

        public void Report(NestProgress value)
        {
            if (value.BestParts != null && previousParts.Count > 0)
            {
                var combined = new List<Part>(previousParts.Count + value.BestParts.Count);
                combined.AddRange(previousParts);
                combined.AddRange(value.BestParts);
                value.BestParts = combined;
            }

            inner.Report(value);
        }
    }
}
