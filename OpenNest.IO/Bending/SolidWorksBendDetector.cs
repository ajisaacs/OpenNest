using ACadSharp;
using ACadSharp.Entities;
using OpenNest.Bending;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenNest.IO.Bending
{
    public class SolidWorksBendDetector : IBendDetector
    {
        public string Name => "SolidWorks";

        public double MaxBendRadius { get; set; } = 4.0;

        private static readonly Regex BendNoteRegex = new Regex(
            @"(?<direction>UP|DOWN|DN)\s+(?<angle>\d+(\.\d+)?)[^A-Z\d]*R\s*(?<radius>\d+(\.\d+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MTextFormatRegex = new Regex(
            @"\\[fHCTQWASpOoLlKk][^;]*;|\\P|[{}]|%%[dDpPcC]",
            RegexOptions.Compiled);

        public List<Bend> DetectBends(CadDocument document)
        {
            var bendLines = FindBendLines(document);
            var bendNotes = FindBendNotes(document);

            if (bendLines.Count == 0)
                return new List<Bend>();

            var bends = new List<Bend>();

            foreach (var line in bendLines)
            {
                var start = new Vector(line.StartPoint.X, line.StartPoint.Y);
                var end = new Vector(line.EndPoint.X, line.EndPoint.Y);

                var bend = new Bend
                {
                    StartPoint = start,
                    EndPoint = end,
                    Direction = BendDirection.Unknown
                };

                var note = FindClosestBendNote(line, bendNotes);
                if (note != null)
                {
                    var noteText = StripMTextFormatting(note.Value);
                    bend.Direction = GetBendDirection(noteText);
                    bend.NoteText = noteText;
                    ParseBendNote(noteText, bend);
                }

                if (!bend.Radius.HasValue || bend.Radius.Value <= MaxBendRadius)
                    bends.Add(bend);
            }

            return bends;
        }

        private List<ACadSharp.Entities.Line> FindBendLines(CadDocument document)
        {
            return document.Entities
                .OfType<ACadSharp.Entities.Line>()
                .Where(l => l.Layer?.Name == "BEND"
                    && (l.LineType?.Name?.Contains("CENTER") == true
                        || l.LineType?.Name == "CENTERX2"))
                .ToList();
        }

        private List<MText> FindBendNotes(CadDocument document)
        {
            return document.Entities
                .OfType<MText>()
                .Where(t => GetBendDirection(t.Value) != BendDirection.Unknown)
                .ToList();
        }

        private static BendDirection GetBendDirection(string text)
        {
            if (string.IsNullOrEmpty(text))
                return BendDirection.Unknown;

            var upper = text.ToUpperInvariant();

            if (upper.Contains("UP"))
                return BendDirection.Up;

            if (upper.Contains("DOWN") || upper.Contains("DN"))
                return BendDirection.Down;

            return BendDirection.Unknown;
        }

        private static void ParseBendNote(string text, Bend bend)
        {
            var normalized = text.ToUpperInvariant().Replace("SHARP", "R0");
            var match = BendNoteRegex.Match(normalized);

            if (match.Success)
            {
                if (double.TryParse(match.Groups["radius"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var radius))
                    bend.Radius = radius;

                if (double.TryParse(match.Groups["angle"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var angle))
                    bend.Angle = angle;
            }
        }

        private static string StripMTextFormatting(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Replace known DXF special characters
            var result = text
                .Replace("%%d", "°").Replace("%%D", "°")
                .Replace("%%p", "±").Replace("%%P", "±")
                .Replace("%%c", "⌀").Replace("%%C", "⌀");

            // Strip MText formatting codes and braces
            result = MTextFormatRegex.Replace(result, " ");

            // Collapse multiple spaces
            return Regex.Replace(result.Trim(), @"\s+", " ");
        }

        private MText FindClosestBendNote(ACadSharp.Entities.Line bendLine, List<MText> notes)
        {
            if (notes.Count == 0) return null;

            MText closest = null;
            var closestDist = double.MaxValue;

            foreach (var note in notes)
            {
                var notePos = new Vector(note.InsertPoint.X, note.InsertPoint.Y);
                var lineStart = new Vector(bendLine.StartPoint.X, bendLine.StartPoint.Y);
                var lineEnd = new Vector(bendLine.EndPoint.X, bendLine.EndPoint.Y);

                var geomLine = new OpenNest.Geometry.Line(lineStart, lineEnd);
                var perpPoint = geomLine.ClosestPointTo(notePos);
                var dist = notePos.DistanceTo(perpPoint);

                var maxAcceptable = note.Height * 2.0;
                if (dist > maxAcceptable) continue;

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = note;
                }
            }

            return closest;
        }
    }
}
