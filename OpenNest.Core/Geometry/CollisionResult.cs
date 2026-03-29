using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Geometry
{
    public class CollisionResult
    {
        public static readonly CollisionResult None = new(false, new List<Polygon>(), new List<Vector>());

        public CollisionResult(bool overlaps, List<Polygon> overlapRegions, List<Vector> intersectionPoints)
        {
            Overlaps = overlaps;
            OverlapRegions = overlapRegions;
            IntersectionPoints = intersectionPoints;
            OverlapArea = overlapRegions.Sum(r => r.Area());
        }

        public bool Overlaps { get; }
        public List<Polygon> OverlapRegions { get; }
        public List<Vector> IntersectionPoints { get; }
        public double OverlapArea { get; }
    }
}
