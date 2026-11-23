using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Labels
{
    /// <summary>
    /// Where a part's number label would be etched: a rectangle on the flat face, in the drawing's
    /// source frame. Only the placement is stored. It is an annotation, like a dimension: it never
    /// affects the program, nesting geometry or posts.
    /// </summary>
    public sealed class LabelBox
    {
        public LabelBox()
        {
        }

        public LabelBox(Vector center, double width, double height, double rotation)
        {
            Center = center;
            Width = width;
            Height = height;
            Rotation = rotation;
        }

        public Vector Center { get; set; }

        /// <summary>The side along the text, before <see cref="Rotation"/>.</summary>
        public double Width { get; set; }

        /// <summary>The side across the text, before <see cref="Rotation"/>.</summary>
        public double Height { get; set; }

        /// <summary>Counterclockwise turn about <see cref="Center"/>, in radians.</summary>
        public double Rotation { get; set; }

        /// <summary>True when the center, both sides and the rotation are finite and both sides are positive.</summary>
        public bool IsValid =>
            double.IsFinite(Center.X) && double.IsFinite(Center.Y)
            && double.IsFinite(Width) && Width > 0
            && double.IsFinite(Height) && Height > 0
            && double.IsFinite(Rotation);

        public LabelBox Clone() => new(Center, Width, Height, Rotation);

        /// <summary>
        /// The same box turned by <paramref name="rotation"/> instead, kept readable: a half turn
        /// covers the same rectangle, so text is never turned past a quarter (upside down).
        /// </summary>
        public LabelBox WithRotation(double rotation) => new(Center, Width, Height, ReadableRotation(rotation));

        /// <summary>
        /// <paramref name="rotation"/> brought into (-π/2, π/2] by half turns, so text along it reads
        /// left to right or bottom to top; a nonfinite value is returned as is.
        /// </summary>
        public static double ReadableRotation(double rotation)
        {
            if (!double.IsFinite(rotation))
                return rotation;
            var turned = System.Math.IEEERemainder(rotation, System.Math.PI);
            if (turned <= -Angle.HalfPI + Tolerance.Epsilon)
                turned += System.Math.PI;
            return System.Math.Abs(turned) < Tolerance.Epsilon ? 0 : turned;
        }

        /// <summary>The corners counterclockwise from the text's lower left.</summary>
        public Vector[] Corners()
        {
            var cos = PortableMath.Cos(Rotation);
            var sin = PortableMath.Sin(Rotation);
            var hw = Width / 2;
            var hh = Height / 2;
            Vector Corner(double x, double y) => new(Center.X + x * cos - y * sin, Center.Y + x * sin + y * cos);
            return new[] { Corner(-hw, -hh), Corner(hw, -hh), Corner(hw, hh), Corner(-hw, hh) };
        }

        /// <summary>A point in the box's own frame (x along the text, y across it), origin at the center.</summary>
        public Vector ToLocal(Vector point)
        {
            var cos = PortableMath.Cos(Rotation);
            var sin = PortableMath.Sin(Rotation);
            var dx = point.X - Center.X;
            var dy = point.Y - Center.Y;
            return new Vector(dx * cos + dy * sin, -dx * sin + dy * cos);
        }

        /// <summary>The point at <paramref name="local"/> in the box's own frame.</summary>
        public Vector ToWorld(Vector local)
        {
            var cos = PortableMath.Cos(Rotation);
            var sin = PortableMath.Sin(Rotation);
            return new Vector(
                Center.X + local.X * cos - local.Y * sin,
                Center.Y + local.X * sin + local.Y * cos);
        }

        public bool Contains(Vector point)
        {
            var local = ToLocal(point);
            return System.Math.Abs(local.X) <= Width / 2 && System.Math.Abs(local.Y) <= Height / 2;
        }
    }
}
