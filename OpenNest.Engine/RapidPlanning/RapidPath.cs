using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.RapidPlanning
{
    public readonly struct RapidPath
    {
        public bool HeadUp { get; init; }
        public List<Vector> Waypoints { get; init; }
    }
}
