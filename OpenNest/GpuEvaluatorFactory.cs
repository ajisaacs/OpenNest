using System.Diagnostics;
using OpenNest.Engine.BestFit;
using OpenNest.Gpu;

namespace OpenNest
{
    internal static class GpuEvaluatorFactory
    {
        public static IPairEvaluator Create(Drawing drawing, double spacing)
        {
            try
            {
                return new GpuPairEvaluator(drawing, spacing);
            }
            catch
            {
                Debug.WriteLine("[GpuEvaluatorFactory] GPU not available, falling back to CPU");
                return null;
            }
        }
    }
}
