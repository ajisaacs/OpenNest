using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Labels
{
    /// <summary>
    /// A small built-in single-stroke font for etched labels: digits, capital letters and a few
    /// marks, drawn as lines and arcs (the digits' bowls are round). It needs no font file, so the
    /// same text etches on every machine.
    /// </summary>
    /// <remarks>
    /// A glyph is drawn on a grid four wide and eight tall; at a character height of 1 a glyph is
    /// <see cref="GlyphWidth"/> wide and the next one starts <see cref="Advance"/> after it, the
    /// advance <see cref="LabelBoxOptions.CharacterAdvance"/> sizes a label box with. Lowercase
    /// letters are drawn as capitals; characters the font lacks leave a gap.
    /// </remarks>
    public static class StrokeFont
    {
        private const double GridWidth = 4;
        private const double GridHeight = 8;
        private const double GridAdvance = 6;

        /// <summary>A glyph's width as a share of the character height.</summary>
        public const double GlyphWidth = GridWidth / GridHeight;

        /// <summary>The distance from one character's start to the next, as a share of the character height.</summary>
        public const double Advance = GridAdvance / GridHeight;

        private const double Root2 = 1.4142135623730951;
        private const double Root3 = 1.7320508075688772;
        private const bool Cw = true;
        private const bool Ccw = false;

        // Each glyph is a list of strokes on the grid: polylines (P, x,y pairs) or paths of lines and arcs (S).
        private static readonly Dictionary<char, GlyphStroke[]> Glyphs = new()
        {
            // The round digits' bowls are circles of radius 2 centered at (2, 2) and (2, 6); the 6 and
            // 9 reach them on a long arc of radius 6.5 that meets the bowl's side tangentially.
            ['0'] = new[] { S(0, 2).L(0, 6).Arc(2, 6, 0, Cw).L(4, 2).Arc(2, 2, 180, Cw),
                S(2 - Root2, 2 - Root2).L(2 + Root2, 6 + Root2) },
            ['1'] = new[] { P(1, 6, 2, 8, 2, 0), P(1, 0, 3, 0) },
            ['2'] = new[] { S(2 - Root3, 7).Arc(2, 6, -30, Cw).L(0, 0).L(4, 0) },
            ['3'] = new[] { S(2 - Root3, 7).Arc(2, 6, -90, Cw).Arc(2, 2, -150, Cw) },
            ['4'] = new[] { P(3, 0, 3, 8, 0, 3, 4, 3) },
            ['5'] = new[] { S(4, 8).L(2 - Root2, 8).L(2 - Root2, 2 + Root2).Arc(2, 2, -150, Cw) },
            ['6'] = new[] { S(3.2, 7.6).Arc(6.5, 2, 180, Ccw).Arc(2, 2, 0, Ccw).Arc(2, 2, 180, Ccw) },
            ['7'] = new[] { P(0, 8, 4, 8, 1, 0) },
            ['8'] = new[] { S(2, 4).Arc(2, 6, 90, Ccw).Arc(2, 6, -90, Ccw).Arc(2, 2, -90, Cw).Arc(2, 2, 90, Cw) },
            ['9'] = new[] { S(0.8, 0.4).Arc(-2.5, 6, 0, Ccw).Arc(2, 6, 180, Ccw).Arc(2, 6, 0, Ccw) },
            ['-'] = new[] { P(0, 4, 4, 4) },
            ['_'] = new[] { P(0, 0, 4, 0) },
            ['.'] = new[] { P(1.5, 0, 2.5, 0) },
            ['/'] = new[] { P(0, 0, 4, 8) },
            ['A'] = new[] { P(0, 0, 0, 6, 2, 8, 4, 6, 4, 0), P(0, 4, 4, 4) },
            ['B'] = new[] { P(0, 0, 0, 8, 3, 8, 4, 7, 4, 5, 3, 4, 0, 4), P(3, 4, 4, 3, 4, 1, 3, 0, 0, 0) },
            ['C'] = new[] { P(4, 7, 3, 8, 1, 8, 0, 7, 0, 1, 1, 0, 3, 0, 4, 1) },
            ['D'] = new[] { P(0, 0, 0, 8, 3, 8, 4, 7, 4, 1, 3, 0, 0, 0) },
            ['E'] = new[] { P(4, 8, 0, 8, 0, 0, 4, 0), P(0, 4, 3, 4) },
            ['F'] = new[] { P(4, 8, 0, 8, 0, 0), P(0, 4, 3, 4) },
            ['G'] = new[] { P(4, 7, 3, 8, 1, 8, 0, 7, 0, 1, 1, 0, 3, 0, 4, 1, 4, 4, 2, 4) },
            ['H'] = new[] { P(0, 0, 0, 8), P(4, 0, 4, 8), P(0, 4, 4, 4) },
            ['I'] = new[] { P(1, 8, 3, 8), P(2, 8, 2, 0), P(1, 0, 3, 0) },
            ['J'] = new[] { P(4, 8, 4, 1, 3, 0, 1, 0, 0, 1) },
            ['K'] = new[] { P(0, 0, 0, 8), P(4, 8, 0, 3), P(1, 4, 4, 0) },
            ['L'] = new[] { P(0, 8, 0, 0, 4, 0) },
            ['M'] = new[] { P(0, 0, 0, 8, 2, 4, 4, 8, 4, 0) },
            ['N'] = new[] { P(0, 0, 0, 8, 4, 0, 4, 8) },
            ['O'] = new[] { P(1, 0, 3, 0, 4, 1, 4, 7, 3, 8, 1, 8, 0, 7, 0, 1, 1, 0) },
            ['P'] = new[] { P(0, 0, 0, 8, 3, 8, 4, 7, 4, 5, 3, 4, 0, 4) },
            ['Q'] = new[] { P(1, 0, 3, 0, 4, 1, 4, 7, 3, 8, 1, 8, 0, 7, 0, 1, 1, 0), P(2, 2, 4, 0) },
            ['R'] = new[] { P(0, 0, 0, 8, 3, 8, 4, 7, 4, 5, 3, 4, 0, 4), P(2, 4, 4, 0) },
            ['S'] = new[] { P(4, 7, 3, 8, 1, 8, 0, 7, 0, 5, 1, 4, 3, 4, 4, 3, 4, 1, 3, 0, 1, 0, 0, 1) },
            ['T'] = new[] { P(0, 8, 4, 8), P(2, 8, 2, 0) },
            ['U'] = new[] { P(0, 8, 0, 1, 1, 0, 3, 0, 4, 1, 4, 8) },
            ['V'] = new[] { P(0, 8, 2, 0, 4, 8) },
            ['W'] = new[] { P(0, 8, 1, 0, 2, 4, 3, 0, 4, 8) },
            ['X'] = new[] { P(0, 8, 4, 0), P(0, 0, 4, 8) },
            ['Y'] = new[] { P(0, 8, 2, 4, 4, 8), P(2, 4, 2, 0) },
            ['Z'] = new[] { P(0, 8, 4, 8, 0, 0, 4, 0) },
        };

        private static GlyphStroke S(double x, double y) => new(x, y);

        private static GlyphStroke P(params double[] coordinates)
        {
            var stroke = S(coordinates[0], coordinates[1]);
            for (var i = 2; i + 1 < coordinates.Length; i += 2)
                stroke.L(coordinates[i], coordinates[i + 1]);
            return stroke;
        }

        // A glyph stroke on the grid, built from its start by straight lines and arcs.
        private sealed class GlyphStroke
        {
            private readonly Stroke stroke;
            private Vector position;

            public GlyphStroke(double x, double y)
            {
                position = new Vector(x, y);
                stroke = new Stroke(position);
            }

            public GlyphStroke L(double x, double y)
            {
                position = new Vector(x, y);
                stroke.Segments.Add(new StrokeSegment(position));
                return this;
            }

            // An arc about (cx, cy) from where the stroke is to the point at endDegrees on the
            // same circle; the stroke must already be on it.
            public GlyphStroke Arc(double cx, double cy, double endDegrees, bool clockwise)
            {
                var center = new Vector(cx, cy);
                var radius = center.DistanceTo(position);
                var angle = Angle.ToRadians(endDegrees);
                position = new Vector(cx + radius * PortableMath.Cos(angle), cy + radius * PortableMath.Sin(angle));
                stroke.Segments.Add(new StrokeSegment(position, true, center, clockwise));
                return this;
            }

            public Stroke Place(double left, double scale) =>
                stroke.Map(point => new Vector((left + point.X) * scale, point.Y * scale));
        }

        /// <summary>True when the font draws <paramref name="character"/> (case ignored).</summary>
        public static bool Has(char character) => Glyphs.ContainsKey(char.ToUpperInvariant(character));

        /// <summary>The length of <paramref name="text"/> at a character height of 1: the last glyph's width, not its advance.</summary>
        public static double Measure(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            return (text.Length - 1) * Advance + GlyphWidth;
        }

        /// <summary>
        /// The strokes of <paramref name="text"/> at <paramref name="height"/>, as paths of lines
        /// and arcs with the text's lower left at the origin and reading along +X.
        /// </summary>
        public static List<Stroke> Strokes(string text, double height)
        {
            var strokes = new List<Stroke>();
            if (string.IsNullOrEmpty(text))
                return strokes;

            var scale = height / GridHeight;
            for (var index = 0; index < text.Length; index++)
            {
                if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[index]), out var glyph))
                    continue;

                var left = index * GridAdvance;
                foreach (var stroke in glyph)
                    strokes.Add(stroke.Place(left, scale));
            }

            return strokes;
        }
    }
}
