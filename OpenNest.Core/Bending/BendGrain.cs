using OpenNest.Math;

namespace OpenNest.Bending
{
    /// <summary>How a bend line runs relative to the sheet's grain.</summary>
    public enum BendGrainKind
    {
        /// <summary>Perpendicular to the grain: the preferred direction, which avoids cracking.</summary>
        AcrossGrain,

        /// <summary>Along the grain.</summary>
        WithGrain,

        /// <summary>Any other angle.</summary>
        Other,
    }

    /// <summary>
    /// Classifies a bend line against the sheet grain. A bend line is undirected, so every angle
    /// is taken modulo 180°.
    /// </summary>
    public static class BendGrain
    {
        /// <summary>How far a bend may be from exactly across or exactly with the grain, in radians (5°).</summary>
        public static readonly double AngleTolerance = Angle.ToRadians(5);

        /// <summary>The width of one <see cref="Bucket"/> step, in radians (15°).</summary>
        public static readonly double BucketStep = Angle.ToRadians(15);

        /// <summary>The number of buckets in a half turn.</summary>
        public const int BucketCount = 12;

        /// <summary>
        /// The angle between a bend line and the grain, in [0, π/2]: 0 is with the grain and
        /// π/2 is across it.
        /// </summary>
        public static double AngleToGrain(double lineAngle, double grainAngle)
        {
            var diff = Wrap(lineAngle - grainAngle);
            return System.Math.Min(diff, System.Math.PI - diff);
        }

        /// <summary>Classifies a bend line (already rotated into the plate) against the grain.</summary>
        public static BendGrainKind Classify(double lineAngle, double grainAngle)
        {
            var angle = AngleToGrain(lineAngle, grainAngle);
            var slack = OpenNest.Math.Tolerance.Epsilon;

            if (angle <= AngleTolerance + slack)
                return BendGrainKind.WithGrain;

            return angle >= Angle.HalfPI - AngleTolerance - slack
                ? BendGrainKind.AcrossGrain
                : BendGrainKind.Other;
        }

        /// <summary>
        /// Classifies <paramref name="bend"/> of a part placed at <paramref name="partRotation"/>.
        /// </summary>
        public static BendGrainKind Classify(Bend bend, double partRotation, double grainAngle) =>
            Classify(bend.LineAngle + partRotation, grainAngle);

        /// <summary>
        /// The bend line's direction relative to the grain in 15° steps, 0 to
        /// <see cref="BucketCount"/> - 1. Unlike <see cref="AngleToGrain"/> it keeps the side of the
        /// grain, so a bend 30° one way and one 30° the other way are different buckets (they need
        /// the part turned differently). 0 is with the grain and 6 is across it; every bend
        /// <see cref="Classify(double, double)"/> calls <see cref="BendGrainKind.Other"/> falls in
        /// buckets 1 to 5 or 7 to 11.
        /// </summary>
        public static int Bucket(double lineAngle, double grainAngle)
        {
            var kind = Classify(lineAngle, grainAngle);
            if (kind == BendGrainKind.WithGrain)
                return 0;
            if (kind == BendGrainKind.AcrossGrain)
                return BucketCount / 2;

            // A bend just outside the tolerance can round onto the with/across bucket: nudge it to
            // the neighbouring bucket on its own side so those two stay exclusive to their kinds.
            var wrapped = Wrap(lineAngle - grainAngle);
            var bucket = (int)System.Math.Round(wrapped / BucketStep) % BucketCount;
            var firstSide = wrapped < Angle.HalfPI;

            if (bucket == 0)
                return firstSide ? 1 : BucketCount - 1;

            if (bucket == BucketCount / 2)
                return firstSide ? BucketCount / 2 - 1 : BucketCount / 2 + 1;

            return bucket;
        }

        /// <summary>An undirected line angle wrapped into [0, π).</summary>
        private static double Wrap(double angle)
        {
            var wrapped = angle % System.Math.PI;
            if (wrapped < 0)
                wrapped += System.Math.PI;
            return wrapped;
        }
    }
}
