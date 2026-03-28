using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Drawing;

namespace OpenNest.Bending
{
    public class Bend
    {
        public static readonly Layer EtchLayer = new Layer("ETCH")
        {
            Color = Color.Green,
            IsVisible = true
        };

        private const double DefaultEtchLength = 1.0;

        public Vector StartPoint { get; set; }
        public Vector EndPoint { get; set; }
        public BendDirection Direction { get; set; }
        public double? Angle { get; set; }
        public double? Radius { get; set; }
        public string NoteText { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public Entity SourceEntity { get; set; }

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

        /// <summary>
        /// Generates etch mark entities for this bend (up bends only).
        /// Returns 1" dashes at each end of the bend line, or the full line if shorter than 3".
        /// </summary>
        public List<Line> GetEtchEntities(double etchLength = DefaultEtchLength)
        {
            var result = new List<Line>();
            if (Direction != BendDirection.Up)
                return result;

            var length = Length;

            if (length < etchLength * 3.0)
            {
                result.Add(CreateEtchLine(StartPoint, EndPoint));
            }
            else
            {
                var angle = StartPoint.AngleTo(EndPoint);
                var dx = System.Math.Cos(angle) * etchLength;
                var dy = System.Math.Sin(angle) * etchLength;

                result.Add(CreateEtchLine(StartPoint, new Vector(StartPoint.X + dx, StartPoint.Y + dy)));
                result.Add(CreateEtchLine(new Vector(EndPoint.X - dx, EndPoint.Y - dy), EndPoint));
            }

            return result;
        }

        /// <summary>
        /// Removes existing etch entities from the list and regenerates from the given bends.
        /// </summary>
        public static void UpdateEtchEntities(List<Entity> entities, List<Bend> bends)
        {
            entities.RemoveAll(e => e.Layer == EtchLayer);
            if (bends == null) return;

            foreach (var bend in bends)
                entities.AddRange(bend.GetEtchEntities());
        }

        private static Line CreateEtchLine(Vector start, Vector end)
        {
            return new Line(start, end) { Layer = EtchLayer, Color = Color.Green };
        }

        public override string ToString()
        {
            var dir = Direction.ToString();
            var angle = Angle?.ToString("0.##") ?? "?";
            var radius = Radius?.ToString("0.###") ?? "?";
            return $"{dir} {angle}° R{radius}";
        }
    }
}
