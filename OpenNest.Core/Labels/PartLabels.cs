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
        /// The label text for a drawing placed at <paramref name="partRotation"/>: its name, then a
        /// dash and its bend groups when it has bends, for example <c>1234-AB</c>. Characters the
        /// stroke font lacks are dropped from the name. Empty when nothing is left to etch.
        /// With <see cref="PartLabelMode.PartNumber"/> it is the name alone, and with
        /// <see cref="PartLabelMode.Off"/> it is empty.
        /// </summary>
        public static string Text(Drawing drawing, double partRotation, double grainAngle,
            PartLabelMode mode = PartLabelMode.PartNumberAndBendGroups)
        {
            if (drawing == null || mode == PartLabelMode.Off)
                return string.Empty;

            var name = new StringBuilder();
            foreach (var character in drawing.Name ?? string.Empty)
                if (StrokeFont.Has(character) && !char.IsWhiteSpace(character))
                    name.Append(char.ToUpperInvariant(character));

            var groups = mode == PartLabelMode.PartNumberAndBendGroups
                ? GroupLetters(drawing.Bends, partRotation, grainAngle)
                : string.Empty;
            if (groups.Length == 0)
                return name.ToString();

            return name.Length == 0 ? groups : name + "-" + groups;
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

            var height = box.Height;
            var width = StrokeFont.Measure(text) * height;
            if (width > box.Width)
            {
                height *= box.Width / width;
                width = box.Width;
            }

            if (height < box.Height * MinimumHeightShare - Tolerance.Epsilon)
                return null;

            var offset = drawing.Source?.Offset ?? Vector.Zero;
            // The source frame loses the program's offset, then turns with the part, as bends do.
            var strokes = StrokeFont.Strokes(text, height).ConvertAll(stroke => stroke.Map(point =>
            {
                var world = box.ToWorld(new Vector(point.X - width / 2, point.Y - height / 2)) - offset;
                return partRotation == 0 ? world : world.Rotate(partRotation);
            }));

            return strokes;
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
