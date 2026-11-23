using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Labels
{
    /// <summary>
    /// The text etched in a part's <see cref="LabelBox"/>: its drawing name and the bend groups
    /// its bends fall in for the way it is placed against the sheet grain.
    /// </summary>
    /// <remarks>
    /// A bend group is the bend line's direction relative to the grain (<see cref="BendGrain.Bucket"/>),
    /// named by a fixed letter so a group has the same letter on every part and sheet of a nest:
    /// A across the grain, B with it, then C and on for the 15° steps between, in bucket order.
    /// The grain changes springback, so bends sharing a letter take the same press brake angle
    /// correction and can be formed together. Like a dimension, the box only stores
    /// placement; the text is made when the part is planned, because the letters depend on how the
    /// part was rotated.
    /// </remarks>
    public static class PartLabels
    {
        /// <summary>The smallest share of the box height the text may be shrunk to; a longer name is not etched.</summary>
        public const double MinimumHeightShare = 0.5;

        /// <summary>
        /// Separates the name from the bend groups in a label's text (<c>1234 AB</c>). Names never
        /// hold one, so <see cref="Layout"/> circles every letter after it.
        /// </summary>
        public const char GroupSeparator = ' ';

        // Shares of the character height: the gap from the name to the first circle, the gap
        // between circles, and the height of a letter inside its circle (the circle's diameter is
        // the character height).
        private const double GroupGap = 0.375;
        private const double CircleGap = 0.125;
        private const double CircledLetterShare = 0.6;

        // Letter per bucket, indexed by BendGrain.Bucket: with the grain B, across it A, the rest C on.
        private static readonly char[] BucketLetters = BuildBucketLetters();

        private static char[] BuildBucketLetters()
        {
            var letters = new char[BendGrain.BucketCount];
            letters[0] = 'B';
            letters[BendGrain.BucketCount / 2] = 'A';
            var next = 'C';
            for (var bucket = 0; bucket < letters.Length; bucket++)
                if (letters[bucket] == 0)
                    letters[bucket] = next++;
            return letters;
        }

        /// <summary>The bend group letter of a bend line (already rotated into the plate) against the grain.</summary>
        public static char GroupLetter(double lineAngle, double grainAngle) =>
            BucketLetters[BendGrain.Bucket(lineAngle, grainAngle)];

        /// <summary>
        /// The distinct bend group letters of <paramref name="bends"/> for a part placed at
        /// <paramref name="partRotation"/>, in alphabetical order. Empty without bends.
        /// </summary>
        public static string GroupLetters(IEnumerable<Bend> bends, double partRotation, double grainAngle)
        {
            if (bends == null)
                return string.Empty;

            var letters = new SortedSet<char>();
            foreach (var bend in bends)
            {
                if (bend == null || bend.Length <= Tolerance.Epsilon)
                    continue;
                letters.Add(GroupLetter(bend.LineAngle + partRotation, grainAngle));
            }

            return new string(letters.ToArray());
        }

        /// <summary>
        /// The label text for a drawing placed at <paramref name="partRotation"/>: its name, then
        /// <see cref="GroupSeparator"/> and its bend groups when it has bends, for example
        /// <c>1234 AB</c>; <see cref="Layout"/> etches each group letter in a circle. Characters the
        /// stroke font lacks are dropped from the name. Empty when nothing is left to etch.
        /// With <see cref="PartLabelMode.PartNumber"/> it is the name alone, and with
        /// <see cref="PartLabelMode.Off"/> it is empty.
        /// </summary>
        public static string Text(Drawing drawing, double partRotation, double grainAngle,
            PartLabelMode mode = PartLabelMode.PartNumberAndBendGroups)
        {
            if (drawing == null || mode == PartLabelMode.Off)
                return string.Empty;

            var groups = mode == PartLabelMode.PartNumberAndBendGroups
                ? GroupLetters(drawing.Bends, partRotation, grainAngle)
                : string.Empty;
            return Join(EtchableName(drawing.Name), groups);
        }

        /// <summary>
        /// The label as the CAD Converter previews it before the part is placed: the full
        /// <see cref="PartLabelMode.PartNumberAndBendGroups"/> text for the part unrotated on a sheet
        /// whose grain runs along X. It is the longest the label gets for that placement, so a box
        /// that fits it fits the part number alone; other placements may change the letters.
        /// </summary>
        public static string PreviewText(string name, IEnumerable<Bend> bends) =>
            Join(EtchableName(name), GroupLetters(bends, 0, 0));

        // The name in capitals with characters the stroke font lacks, and spaces, dropped.
        private static string EtchableName(string name)
        {
            var etchable = new StringBuilder();
            foreach (var character in name ?? string.Empty)
                if (StrokeFont.Has(character) && !char.IsWhiteSpace(character))
                    etchable.Append(char.ToUpperInvariant(character));
            return etchable.ToString();
        }

        private static string Join(string name, string groups) =>
            groups.Length == 0 ? name : name + GroupSeparator + groups;

        /// <summary>
        /// The length of a label's <paramref name="text"/> at a character height of 1: the name,
        /// then a gap and a circle per bend group, the circles one character height across.
        /// </summary>
        public static double Measure(string text)
        {
            var (name, groups) = Split(text);
            if (groups.Length == 0)
                return StrokeFont.Measure(name);
            return StrokeFont.Measure(name) + (name.Length == 0 ? 0 : GroupGap)
                + groups.Length + (groups.Length - 1) * CircleGap;
        }

        private static (string Name, string Groups) Split(string text)
        {
            text ??= string.Empty;
            var separator = text.IndexOf(GroupSeparator);
            return separator < 0 ? (text, string.Empty) : (text[..separator], text[(separator + 1)..]);
        }

        // The text's strokes at height, lower left at the origin: the name, then each group letter
        // centered in a circle the height of the text.
        private static List<Stroke> TextStrokes(string text, double height)
        {
            var (name, groups) = Split(text);
            var strokes = StrokeFont.Strokes(name, height);
            if (groups.Length == 0)
                return strokes;

            var radius = height / 2;
            var letterHeight = height * CircledLetterShare;
            var left = name.Length == 0 ? 0 : (StrokeFont.Measure(name) + GroupGap) * height;
            foreach (var letter in groups)
            {
                var center = new Vector(left + radius, radius);
                // A full circle as two half arcs, since a stroke's arc is less than a full turn.
                var circle = new Stroke(new Vector(center.X + radius, center.Y));
                circle.Segments.Add(new StrokeSegment(new Vector(center.X - radius, center.Y), true, center, false));
                circle.Segments.Add(new StrokeSegment(new Vector(center.X + radius, center.Y), true, center, false));
                strokes.Add(circle);

                var corner = new Vector(center.X - letterHeight * StrokeFont.GlyphWidth / 2, center.Y - letterHeight / 2);
                foreach (var stroke in StrokeFont.Strokes(letter.ToString(), letterHeight))
                    strokes.Add(stroke.Map(point => point + corner));

                left += height * (1 + CircleGap);
            }
            return strokes;
        }

        /// <summary>
        /// Lays <paramref name="text"/> out in <paramref name="box"/>, in the box's own frame (the
        /// drawing's source frame), each bend group letter in a circle: centered, reading along the box at its height, or shrunk to fit
        /// its width. <paramref name="fits"/> is false when the text had to shrink below
        /// <see cref="MinimumHeightShare"/> of the box height, so Plan Cutting would not etch it;
        /// the shrunk strokes are still returned, for previewing. Null for an invalid box or no text.
        /// </summary>
        public static List<Stroke> Layout(LabelBox box, string text, out bool fits)
        {
            fits = false;
            if (box == null || !box.IsValid || string.IsNullOrEmpty(text))
                return null;

            var height = box.Height;
            var width = Measure(text) * height;
            if (width > box.Width)
            {
                height *= box.Width / width;
                width = box.Width;
            }

            fits = height >= box.Height * MinimumHeightShare - Tolerance.Epsilon;
            return TextStrokes(text, height).ConvertAll(stroke =>
                stroke.Map(point => box.ToWorld(new Vector(point.X - width / 2, point.Y - height / 2))));
        }

        /// <summary>
        /// The etch strokes of <paramref name="drawing"/>'s label for a part placed at
        /// <paramref name="partRotation"/>, as paths of lines and arcs in the drawing's program frame turned with
        /// the part (the frame <c>Part.Program</c> is in before it is located). Null when the drawing
        /// has no valid box, no text, or the text cannot be shrunk to fit the box legibly.
        /// </summary>
        /// <remarks>
        /// The text is centered in the box and reads along it. Its height is the box height, or
        /// less when the text is longer than the box: it is shrunk to fit, to
        /// <see cref="MinimumHeightShare"/> of the box height at the least.
        /// </remarks>
        public static List<Stroke> Strokes(Drawing drawing, double partRotation, double grainAngle,
            PartLabelMode mode = PartLabelMode.PartNumberAndBendGroups)
        {
            var box = drawing?.LabelBox;
            if (box == null || !box.IsValid || !double.IsFinite(partRotation) || !double.IsFinite(grainAngle))
                return null;

            var text = Text(drawing, partRotation, grainAngle, mode);
            if (text.Length == 0)
                return null;

            var laidOut = Layout(box, text, out var fits);
            if (!fits)
                return null;

            var offset = drawing.Source?.Offset ?? Vector.Zero;
            // The source frame loses the program's offset, then turns with the part, as bends do.
            return laidOut.ConvertAll(stroke => stroke.Map(point =>
            {
                var world = point - offset;
                return partRotation == 0 ? world : world.Rotate(partRotation);
            }));
        }

        /// <summary>
        /// Appends <paramref name="drawing"/>'s label to <paramref name="program"/> as scribe
        /// (etch) moves, each stroke started by a rapid. Returns false and leaves the program
        /// unchanged when <see cref="Strokes"/> has nothing to etch.
        /// </summary>
        /// <remarks>
        /// The strokes are points in the program's frame. An incremental program (every imported
        /// drawing's) takes each move, and each arc's center, as a step from where the move starts,
        /// so the moves are written as steps from where the program stops.
        /// </remarks>
        public static bool AddTo(Program program, Drawing drawing, double partRotation, double grainAngle,
            PartLabelMode mode = PartLabelMode.PartNumberAndBendGroups)
        {
            var strokes = Strokes(drawing, partRotation, grainAngle, mode);
            if (program == null || strokes == null || strokes.Count == 0)
                return false;

            var incremental = program.Mode == Mode.Incremental;
            var position = incremental ? ConvertMode.EndPosition(program) : Vector.Zero;
            Vector To(Vector point)
            {
                if (!incremental)
                    return point;
                var step = point - position;
                position = point;
                return step;
            }

            foreach (var stroke in strokes)
            {
                if (stroke.Segments.Count == 0)
                    continue;

                program.Codes.Add(new RapidMove(To(stroke.Start)));
                foreach (var segment in stroke.Segments)
                {
                    if (!segment.IsArc)
                    {
                        program.Codes.Add(new LinearMove(To(segment.End)) { Layer = LayerType.Scribe });
                        continue;
                    }
                    // An incremental arc's center is a step from where the arc starts, as its end is.
                    var center = incremental ? segment.Center - position : segment.Center;
                    program.Codes.Add(new ArcMove(To(segment.End), center,
                        segment.Clockwise ? RotationType.CW : RotationType.CCW)
                    { Layer = LayerType.Scribe });
                }
            }

            return true;
        }
    }
}
