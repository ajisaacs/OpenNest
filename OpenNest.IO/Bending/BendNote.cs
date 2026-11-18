using System.Globalization;
using OpenNest.Bending;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.IO.Bending
{
    /// <summary>
    /// The note an export writes beside a bend line, in the form
    /// <see cref="SolidWorksBendDetector"/> reads back ("UP 90° R 0.06").
    /// </summary>
    internal static class BendNote
    {
        /// <summary>
        /// The bend's own note when it has one, otherwise one built from its direction, angle
        /// and radius; null when the direction is unknown, since the detector pairs a note with
        /// its line by the word UP or DOWN.
        /// </summary>
        public static string Text(Bend bend)
        {
            if (!string.IsNullOrWhiteSpace(bend.NoteText))
                return bend.NoteText;

            var direction = bend.Direction switch
            {
                BendDirection.Up => "UP",
                BendDirection.Down => "DOWN",
                _ => null,
            };
            if (direction == null)
                return null;

            var text = direction;
            if (bend.Angle.HasValue)
                text += $" {Number(bend.Angle.Value)}°";
            if (bend.Radius.HasValue)
                text += $" R {Number(bend.Radius.Value)}";
            return text;
        }

        /// <summary>A readable text height; the detector accepts a note within two heights of its line.</summary>
        public static double Height(Units units) => units == Units.Millimeters ? 3.0 : 0.125;

        /// <summary>
        /// Where the note sits: just off the middle of the line, turned along it so it reads
        /// left to right. The angle is in radians.
        /// </summary>
        public static (Vector Position, double Angle) Placement(Bend bend, double height)
        {
            var angle = bend.StartPoint.AngleTo(bend.EndPoint);
            if (angle > Angle.HalfPI + Tolerance.Epsilon)
                angle -= System.Math.PI;
            else if (angle <= -Angle.HalfPI + Tolerance.Epsilon)
                angle += System.Math.PI;

            var gap = height * 0.25;
            var middle = new Vector(
                (bend.StartPoint.X + bend.EndPoint.X) / 2,
                (bend.StartPoint.Y + bend.EndPoint.Y) / 2
            );
            var position = new Vector(
                middle.X - PortableMath.Sin(angle) * gap,
                middle.Y + PortableMath.Cos(angle) * gap
            );
            return (position, angle);
        }

        private static string Number(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
