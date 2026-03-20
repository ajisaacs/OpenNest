using OpenNest.Geometry;

namespace OpenNest.Engine.Nfp
{
    /// <summary>
    /// Represents a part that has been placed by the BLF algorithm.
    /// </summary>
    public class PlacedPart
    {
        public int DrawingId { get; set; }
        public double Rotation { get; set; }
        public Vector Position { get; set; }
        public Drawing Drawing { get; set; }
    }
}
