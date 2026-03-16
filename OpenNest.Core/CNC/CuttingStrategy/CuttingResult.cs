using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public readonly struct CuttingResult
    {
        public Program Program { get; init; }
        public Vector LastCutPoint { get; init; }
    }
}
