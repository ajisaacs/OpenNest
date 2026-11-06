using System.Linq;
using OpenNest.Engine.Jobs;

namespace OpenNest.Benchmark
{
    /// <summary>
    /// <c>--common-lines</c>: solve every job through <see cref="NestPipeline"/> at this usage
    /// level and kerf, with every part allowed to share a common line.
    /// </summary>
    public sealed record CommonLineSetting(CommonLineUsage Usage, double Kerf)
    {
        public NestJob Apply(NestJob job)
        {
            var o = job.Options;
            var options = new NestJobOptions(o.PlacementStrategy, o.MaxPlates, o.SalvageRate,
                o.MinimumSalvageDimension, o.SearchUntilCancelled, o.OffcutCut)
            {
                CommonLineUsage = Usage,
                CommonLineKerf = Kerf,
            };
            var parts = job.Parts.Select(p => new NestJobPart(p.Id, p.Geometry, p.Quantity, p.Priority, p.Rotation)
            {
                AllowCommonLine = true,
            });
            return new NestJob(parts, job.Plates, options);
        }
    }
}
