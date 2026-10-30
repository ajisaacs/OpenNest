using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenNest.Geometry;

namespace OpenNest.IO
{
    /// <summary>An SVG affine matrix: x' = A*x + C*y + E, y' = B*x + D*y + F.</summary>
    internal readonly struct SvgTransform
    {
        private const double SimilarityTolerance = 1e-9;

        private static readonly Regex Function = new Regex(
            @"([a-zA-Z]+)\s*\(([^)]*)\)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)
        );

        public static readonly SvgTransform Identity = new SvgTransform(1, 0, 0, 1, 0, 0);

        public SvgTransform(double a, double b, double c, double d, double e, double f)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            E = e;
            F = f;
        }

        public double A { get; }
        public double B { get; }
        public double C { get; }
        public double D { get; }
        public double E { get; }
        public double F { get; }

        /// <summary>True when circles stay circles.</summary>
        public bool IsSimilarity
        {
            get
            {
                var lengthX = A * A + B * B;
                var lengthY = C * C + D * D;
                var size = System.Math.Max(lengthX, lengthY);
                return System.Math.Abs(lengthX - lengthY) <= SimilarityTolerance * size
                    && System.Math.Abs(A * C + B * D) <= SimilarityTolerance * size;
            }
        }

        public double ScaleFactor => System.Math.Sqrt(A * A + B * B);

        public Vector Apply(Vector p) =>
            new Vector(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

        /// <summary>Applies the linear part only, for directions rather than positions.</summary>
        public Vector ApplyLinear(Vector v) => new Vector(A * v.X + C * v.Y, B * v.X + D * v.Y);

        /// <summary>This transform applied after <paramref name="inner"/>.</summary>
        public SvgTransform Then(SvgTransform inner) =>
            new SvgTransform(
                A * inner.A + C * inner.B,
                B * inner.A + D * inner.B,
                A * inner.C + C * inner.D,
                B * inner.C + D * inner.D,
                A * inner.E + C * inner.F + E,
                B * inner.E + D * inner.F + F
            );

        public static SvgTransform Translate(double x, double y) =>
            new SvgTransform(1, 0, 0, 1, x, y);

        public static SvgTransform Scale(double x, double y) =>
            new SvgTransform(x, 0, 0, y, 0, 0);

        /// <summary>Parses a transform list, applied left to right as nested coordinate systems.</summary>
        public static SvgTransform Parse(string text)
        {
            var result = Identity;
            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (Match match in Function.Matches(text))
            {
                var values = SvgReader.ParseNumbers(match.Groups[2].Value);
                result = result.Then(ParseFunction(match.Groups[1].Value, values, text));
            }

            return result;
        }

        private static SvgTransform ParseFunction(string name, double[] v, string text)
        {
            switch (name)
            {
                case "matrix" when v.Length == 6:
                    return new SvgTransform(v[0], v[1], v[2], v[3], v[4], v[5]);

                case "translate" when v.Length is 1 or 2:
                    return Translate(v[0], v.Length == 2 ? v[1] : 0);

                case "scale" when v.Length is 1 or 2:
                    return Scale(v[0], v.Length == 2 ? v[1] : v[0]);

                case "rotate" when v.Length == 1:
                    return Rotation(v[0]);

                case "rotate" when v.Length == 3:
                    return Translate(v[1], v[2])
                        .Then(Rotation(v[0]))
                        .Then(Translate(-v[1], -v[2]));

                case "skewX" when v.Length == 1:
                    return new SvgTransform(1, 0, Tan(v[0]), 1, 0, 0);

                case "skewY" when v.Length == 1:
                    return new SvgTransform(1, Tan(v[0]), 0, 1, 0, 0);

                default:
                    throw new InvalidDataException($"Unsupported SVG transform \"{text}\".");
            }
        }

        private static SvgTransform Rotation(double degrees)
        {
            var radians = degrees * System.Math.PI / 180;
            var cos = System.Math.Cos(radians);
            var sin = System.Math.Sin(radians);
            return new SvgTransform(cos, sin, -sin, cos, 0, 0);
        }

        private static double Tan(double degrees) =>
            System.Math.Tan(degrees * System.Math.PI / 180);
    }
}
