using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public enum ShapeDimensionKind
    {
        /// <summary>A dimension line outside the shape, past its bounds on the <see cref="ShapeDimension.Side"/> side.</summary>
        Linear,

        /// <summary>A dimension line drawn directly between the two points, such as across a hole.</summary>
        Inline,

        /// <summary>A leader from <see cref="ShapeDimension.Start"/> out along <see cref="ShapeDimension.Side"/>.</summary>
        Leader,
    }

    /// <summary>
    /// Where the Shape Library draws one parameter's dimension on the shape's preview, in the
    /// shape's own coordinates (those of the entities <see cref="ShapeDefinition.GetDrawing"/>
    /// builds). Annotation only: it never affects the drawing.
    /// </summary>
    public sealed class ShapeDimension
    {
        private ShapeDimension(string property, ShapeDimensionKind kind, Vector start, Vector end, Vector side, string prefix)
        {
            Property = property;
            Kind = kind;
            Start = start;
            End = end;
            Side = side;
            Prefix = prefix ?? "";
        }

        /// <summary>The name of the shape property the dimension shows and edits.</summary>
        public string Property { get; }

        public ShapeDimensionKind Kind { get; }

        public Vector Start { get; }

        /// <summary>The second measured point; equal to <see cref="Start"/> for a leader.</summary>
        public Vector End { get; }

        /// <summary>Unit vector: the side a linear dimension sits on, or a leader's direction.</summary>
        public Vector Side { get; }

        /// <summary>Text before the value, such as "Ø" or "R".</summary>
        public string Prefix { get; }

        /// <summary>
        /// An integer property shown after the value as "x {count} {noun}", such as a
        /// flange's hole count beside its hole diameter; null when there is none.
        /// </summary>
        public string CountProperty { get; private init; }

        /// <summary>The plural noun after the count, such as "holes".</summary>
        public string CountNoun { get; private init; }

        /// <summary>This dimension with "x {count} {noun}" after its value.</summary>
        public ShapeDimension WithCount(string countProperty, string noun) =>
            new(Property, Kind, Start, End, Side, Prefix) { CountProperty = countProperty, CountNoun = noun };

        /// <summary>
        /// Measures from <paramref name="start"/> to <paramref name="end"/> across
        /// <paramref name="side"/>, which must be one of the four axis directions.
        /// </summary>
        public static ShapeDimension Linear(string property, Vector start, Vector end, Vector side, string prefix = "") =>
            new(property, ShapeDimensionKind.Linear, start, end, side, prefix);

        public static ShapeDimension Inline(string property, Vector start, Vector end, string prefix = "") =>
            new(property, ShapeDimensionKind.Inline, start, end, new Vector(0, 1), prefix);

        public static ShapeDimension Leader(string property, Vector point, Vector direction, string prefix = "")
        {
            var length = System.Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            return new(property, ShapeDimensionKind.Leader, point, point,
                new Vector(direction.X / length, direction.Y / length), prefix);
        }

        /// <summary>The distance the dimension shows between its points (zero for a leader).</summary>
        public double Measured =>
            Kind switch
            {
                ShapeDimensionKind.Linear => System.Math.Abs(Side.X) > System.Math.Abs(Side.Y)
                    ? System.Math.Abs(End.Y - Start.Y)
                    : System.Math.Abs(End.X - Start.X),
                ShapeDimensionKind.Inline => Start.DistanceTo(End),
                _ => 0,
            };

        internal static readonly Vector Up = new(0, 1);
        internal static readonly Vector Down = new(0, -1);
        internal static readonly Vector Left = new(-1, 0);
        internal static readonly Vector Right = new(1, 0);
    }
}
