using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest
{
    internal class StripNestResult
    {
        public List<Part> Parts { get; set; } = new();
        public Box StripBox { get; set; }
        public FillScore Score { get; set; }
        public StripDirection Direction { get; set; }
    }
}
