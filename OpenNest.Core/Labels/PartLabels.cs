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
    /// The part number etched in a part's <see cref="LabelBox"/>, and the bend group letters
    /// <see cref="BendLabels"/> etches beside its bend lines.
    /// </summary>
    /// <remarks>
    /// A bend group is the bend line's direction relative to the grain (<see cref="BendGrain.Bucket"/>),
    /// named by a fixed letter so a group has the same letter on every part and sheet of a nest:
    /// A across the grain, B with it, then C and on for the 15° steps between, in bucket order.
    /// The grain changes springback, so bends sharing a letter take the same press brake angle
    /// correction and can be formed together. Like a dimension, the box only stores placement;
    /// the text is made when the part is planned.
    /// </remarks>
    public static class PartLabels
    {
        /// <summary>The smallest share of the box height the text may be shrunk to; a longer name is not etched.</summary>
        public const double MinimumHeightShare = 0.5;

        // The height of a bend group letter inside its circle, as a share of the circle's diameter.
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
        /// The label text for a drawing: its name in capitals, with characters the stroke font
        /// lacks, and spaces, dropped. Empty when nothing is left to etch or the mode is
        /// <see cref="PartLabelMode.Off"/>. Bend groups are never part of it: each bend's letter
        /// is etched beside its own line (<see cref="BendLabels"/>).
        /// </summary>
        public static string Text(Drawing drawing, PartLabelMode mode = PartLabelMode.PartNumberAndBendGroups) =>
            drawing == null || mode == PartLabelMode.Off ? string.Empty : PreviewText(drawing.Name);

        /// <summary>The label as the CAD Converter previews it: the name as <see cref="Text"/> etches it.</summary>
        public static string PreviewText(string name)
        {
            var etchable = new StringBuilder();
            foreach (var character in name ?? string.Empty)
                if (StrokeFont.Has(character) && !char.IsWhiteSpace(character))
                    etchable.Append(char.ToUpperInvariant(character));
            return etchable.ToString();
        }

        /// <summary>
        /// A bend group letter in its circle: the circle <paramref name="diameter"/> across about
        /// <paramref name="center"/>, the letter centered in it and reading along
        /// <paramref name="rotation"/> (radians, counterclockwise).
        /// </summary>
        public static List<Stroke> CircledLetter(char letter, Vector center, double diameter, double rotation = 0)
        {
            var radius = diameter / 2;
            var letterHeight = diameter * CircledLetterShare;
            var strokes = new List<Stroke>();

            // A full circle as two half arcs, since a stroke's arc is less than a full turn.
            var circle = new Stroke(new Vector(center.X + radius, center.Y));
            circle.Segments.Add(new StrokeSegment(new Vector(center.X - radius, center.Y), true, center, false));
            circle.Segments.Add(new StrokeSegment(new Vector(center.X + radius, center.Y), true, center, false));
            strokes.Add(circle);

            var corner = new Vector(center.X - letterHeight * StrokeFont.GlyphWidth / 2, center.Y - letterHeight / 2);
            foreach (var stroke in StrokeFont.Strokes(letter.ToString(), letterHeight))
                strokes.Add(stroke.Map(point => point + corner));

            return rotation == 0
                ? strokes
                : strokes.ConvertAll(stroke => stroke.Map(point => center + (point - center).Rotate(rotation)));
        }

        /// <summary>
        /// Lays <paramref name="text"/> out in <paramref name="box"/>, in the box's own frame (the
        /// drawing's source frame): centered, reading along the box at its height, or shrunk to fit
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
            var width = StrokeFont.Measure(text) * height;
            if (width > box.Width)
            {
                height *= box.Width / width;
                width = box.Width;
            }

            fits = height >= box.Height * MinimumHeightShare - Tolerance.Epsilon;
            return StrokeFont.Strokes(text, height).ConvertAll(stroke =>
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
        public static List<Stroke> Strokes(Drawing drawing, double partRotation,
            PartLabelMode mode = PartLabelMode.PartNumberAndBendGroups)
        {
            var box = drawing?.LabelBox;
            if (box == null || !box.IsValid || !double.IsFinite(partRotation))
                return null;

            var text = Text(drawing, mode);
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
        public static bool AddTo(Program program, Drawing drawing, double partRotation,
            PartLabelMode mode = PartLabelMode.PartNumberAndBendGroups) =>
            Append(program, Strokes(drawing, partRotation, mode));

        // Appends strokes (points in the program's frame) as scribe moves, each started by a rapid.
        internal static bool Append(Program program, List<Stroke> strokes)
        {
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
