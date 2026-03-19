using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.RapidPlanning
{
    public readonly struct RapidPath
    {
        public bool HeadUp { get; init; }
        public List<Vector> Waypoints { get; init; }
    }
}
