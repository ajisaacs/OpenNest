using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Labels
{
    /// <summary>
    /// A small built-in single-stroke font for etched labels: digits, capital letters and a few
    /// marks, drawn as polylines. It needs no font file, so the same text etches on every machine.
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

        // Each glyph is a list of strokes; each stroke is x,y pairs on the grid.
        private static readonly Dictionary<char, double[][]> Glyphs = new()
        {
            ['0'] = new[] { P(0, 0, 4, 0, 4, 8, 0, 8, 0, 0), P(0, 0, 4, 8) },
            ['1'] = new[] { P(1, 6, 2, 8, 2, 0), P(1, 0, 3, 0) },
            ['2'] = new[] { P(0, 7, 1, 8, 3, 8, 4, 7, 4, 5, 0, 0, 4, 0) },
            ['3'] = new[] { P(0, 7, 1, 8, 3, 8, 4, 7, 4, 5, 3, 4, 1, 4), P(3, 4, 4, 3, 4, 1, 3, 0, 1, 0, 0, 1) },
            ['4'] = new[] { P(3, 0, 3, 8, 0, 3, 4, 3) },
            ['5'] = new[] { P(4, 8, 0, 8, 0, 4, 3, 4, 4, 3, 4, 1, 3, 0, 1, 0, 0, 1) },
            ['6'] = new[] { P(4, 7, 3, 8, 1, 8, 0, 7, 0, 1, 1, 0, 3, 0, 4, 1, 4, 3, 3, 4, 0, 4) },
            ['7'] = new[] { P(0, 8, 4, 8, 1, 0) },
            ['8'] = new[] { P(1, 4, 0, 5, 0, 7, 1, 8, 3, 8, 4, 7, 4, 5, 3, 4, 1, 4, 0, 3, 0, 1, 1, 0, 3, 0, 4, 1, 4, 3, 3, 4) },
            ['9'] = new[] { P(0, 1, 1, 0, 3, 0, 4, 1, 4, 7, 3, 8, 1, 8, 0, 7, 0, 5, 1, 4, 4, 4) },
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

        private static double[] P(params double[] coordinates) => coordinates;

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
        /// The strokes of <paramref name="text"/> at <paramref name="height"/>, as polylines with
        /// the text's lower left at the origin and reading along +X.
        /// </summary>
        public static List<List<Vector>> Strokes(string text, double height)
        {
            var strokes = new List<List<Vector>>();
            if (string.IsNullOrEmpty(text))
                return strokes;

            var scale = height / GridHeight;
            for (var index = 0; index < text.Length; index++)
            {
                if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[index]), out var glyph))
                    continue;

                var left = index * GridAdvance;
                foreach (var coordinates in glyph)
                {
                    var stroke = new List<Vector>(coordinates.Length / 2);
                    for (var i = 0; i + 1 < coordinates.Length; i += 2)
                        stroke.Add(new Vector((left + coordinates[i]) * scale, coordinates[i + 1] * scale));
                    strokes.Add(stroke);
                }
            }

            return strokes;
        }
    }
}
