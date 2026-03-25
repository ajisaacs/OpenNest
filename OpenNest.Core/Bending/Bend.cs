using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Bending
{
    public class Bend
    {
        public Vector StartPoint { get; set; }
        public Vector EndPoint { get; set; }
        public BendDirection Direction { get; set; }
        public double? Angle { get; set; }
        public double? Radius { get; set; }
        public string NoteText { get; set; }

        public double Length => StartPoint.DistanceTo(EndPoint);

        public double AngleRadians => Angle.HasValue
            ? OpenNest.Math.Angle.ToRadians(Angle.Value)
            : 0;

        public Line ToLine() => new Line(StartPoint, EndPoint);

        /// <summary>
        /// Returns the angle of the bend line itself (not the bend angle).
        /// Used for grain direction comparison.
        /// </summary>
        public double LineAngle => StartPoint.AngleTo(EndPoint);

        public override string ToString()
        {
            var dir = Direction.ToString();
            var angle = Angle?.ToString("0.##") ?? "?";
            var radius = Radius?.ToString("0.###") ?? "?";
            return $"{dir} {angle}° R{radius}";
        }
    }
}
