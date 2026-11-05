using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>The outcome of <see cref="CommonLineJoiner.Join"/>: the group made, or why not.</summary>
    public sealed record CommonLineJoinResult(CommonLineGroup Group, string Failure)
    {
        public bool Succeeded => Group != null;
    }

    /// <summary>
    /// Slides parts together one kerf apart along their best facing straight edges and groups them
    /// for common-line cutting. Parts already in a group move with their whole group, which joins
    /// the new one. Nothing moves unless every part finds a partner and the result is an intact group.
    /// </summary>
    public static class CommonLineJoiner
    {
        /// <param name="maxGap">How far apart facing edges may start (or overlap) and still be joined.</param>
        public static CommonLineJoinResult Join(Plate plate, IReadOnlyList<Part> parts, double kerf, double maxGap)
        {
            ArgumentNullException.ThrowIfNull(plate);
            ArgumentNullException.ThrowIfNull(parts);
            if (!double.IsFinite(kerf) || kerf <= 0)
                return new(null, "The plate has no kerf for its material and thickness, so parts cannot share a common line.");
            if (!double.IsFinite(maxGap) || maxGap < kerf)
                maxGap = kerf;
            var selected = parts.Where(part => part != null && !part.BaseDrawing.IsCutOff).Distinct().ToList();
            if (selected.Any(part => !plate.Parts.Contains(part)))
                return new(null, "Every part in a common line must be on the plate.");

            // A part already in a group brings its whole group, which moves as one unit.
            var units = new List<List<Part>>();
            foreach (var part in selected)
            {
                if (units.Any(unit => unit.Contains(part)))
                    continue;
                units.Add(plate.CommonLineGroupOf(part) is { } group ? group.Members.ToList() : new List<Part> { part });
            }
            if (units.Sum(unit => unit.Count) < 2)
                return new(null, "Select at least two parts to share a common line.");

            var start = units.SelectMany(unit => unit).ToDictionary(part => part, part => part.Location);
            // Anchor the unit nearest the plate origin; join the rest to whatever is placed.
            var placed = units.OrderBy(unit => unit.Min(part => part.BoundingBox.Left + part.BoundingBox.Bottom)).First();
            var anchored = new List<Part>(placed);
            var remaining = units.Where(unit => unit != placed).ToList();
            while (remaining.Count > 0)
            {
                var best = remaining
                    .SelectMany(unit => unit.SelectMany(candidate => anchored.Select(anchor =>
                        (Unit: unit, Offset: CommonLineGeometry.JoinOffset(anchor, candidate, kerf, maxGap)))))
                    .Where(choice => choice.Offset != null)
                    .OrderBy(choice => choice.Offset!.Value.DistanceTo(Vector.Zero))
                    .FirstOrDefault();
                if (best.Unit == null)
                {
                    Restore(start);
                    var names = string.Join(", ", remaining.SelectMany(unit => unit).Select(part => part.BaseDrawing.Name).Distinct());
                    return new(null, $"No straight edge of {names} faces another selected part within {maxGap:0.###}. "
                        + "Place the parts edge to edge first.");
                }
                foreach (var part in best.Unit)
                    part.Offset(best.Offset!.Value);
                anchored.AddRange(best.Unit);
                remaining.Remove(best.Unit);
            }

            var trial = new CommonLineGroup(anchored, kerf);
            var check = CommonLineGeometry.Check(trial);
            if (!check.IsIntact)
            {
                Restore(start);
                return new(null, $"'{check.Problems[0].Part.BaseDrawing.Name}' {check.Problems[0].Message}");
            }
            return new(plate.JoinCommonLine(anchored, kerf), null);
        }

        private static void Restore(Dictionary<Part, Vector> start)
        {
            foreach (var (part, location) in start)
                if (part.Location != location)
                    part.Offset(location - part.Location);
        }
    }
}
