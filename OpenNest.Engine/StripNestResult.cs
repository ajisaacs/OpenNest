using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest
{
    internal class StripNestResult
    {
        public List<Part> Parts { get; set; } = new();
        public Box StripBox { get; set; }
        public Box RemnantBox { get; set; }
        public FillScore Score { get; set; }
        public StripDirection Direction { get; set; }
    }
}
